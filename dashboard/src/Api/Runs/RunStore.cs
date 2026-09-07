using System.Reflection;
using System.Text.Json;
using Api.Host;
using Api.Issues;
using Npgsql;
using NpgsqlTypes;

namespace Api.Runs;

public interface IRunStore
{
    Task<IReadOnlyList<RunState>> All();
    Task<IReadOnlyList<RunState>> All(int skip, int take);
    Task<IReadOnlyList<RunState>> Active();
    Task<RunState?> GetRun(Guid runId);
    Task<RunState?> Apply(Guid runId, RunEvent ev);
    Task<bool> Delete(Guid runId);
}

public sealed class RunStore : IRunStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<RunStore> _logger;
    private readonly IGitHubIssuesClient _gitHub;
    private readonly IHostStore _hostStore;
    private readonly IIssueResolver _resolver;

    private const string RunColumns =
        "run_id, repo, branch, spec, model, vm_name, status, started_at, finished_at, last_heartbeat_at, pr_url, failure_reason, freeze_captured, freeze_local_path, updated_at, stages, current_phase, steps";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] Columns = RunColumns.Split(',').Select(c => c.Trim()).ToArray();
    private static readonly string SelectSql = $"SELECT {RunColumns} FROM runs";

    // Single description of the mutable shape: drives IsHeartbeatOnly, UpdateHeartbeat
    // and UpdateDiff, so adding a column touches one list. Heartbeat:true marks the
    // cheap-path pair (last_heartbeat_at + current_phase, which rides it); everything
    // else diffs on the ordered path. Identity/stages columns never change post-insert
    // and stay out of both paths.
    private sealed record ColumnSync(string Column, string Param, Func<RunState, object?> Get, bool Heartbeat = false, NpgsqlDbType? DbType = null);

    private static readonly ColumnSync[] SyncColumns =
    [
        new("vm_name", "vm", s => s.VmName),
        new("status", "status", s => s.Status),
        new("finished_at", "finishedAt", s => s.FinishedAt),
        new("pr_url", "prUrl", s => s.PrUrl),
        new("failure_reason", "failureReason", s => s.FailureReason),
        new("freeze_captured", "freezeCaptured", s => s.FreezeCaptured),
        new("freeze_local_path", "freezeLocalPath", s => s.FreezeLocalPath),
        new("updated_at", "updatedAt", s => s.UpdatedAt),
        // Steps ride the ordered diff path, never the heartbeat cheap path: the
        // getter returns the canonical JSON so unchanged steps compare equal
        // and are not rewritten on unrelated ordered updates.
        new("steps", "steps", s => SerializeJson(s.Steps), DbType: NpgsqlDbType.Jsonb),
        new("last_heartbeat_at", "lastHeartbeatAt", s => s.LastHeartbeatAt, Heartbeat: true),
        new("current_phase", "currentPhase", s => s.CurrentPhase, Heartbeat: true),
    ];

    // Active set derived from the fold's single source; statuses are codebase constants.
    private static readonly string ActiveStatusList =
        string.Join(", ", RunFold.ActiveStatuses.Select(s => $"'{s}'"));

    static RunStore()
    {
        var runStateFieldCount = typeof(RunState)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Length;

        if (Columns.Length != runStateFieldCount)
        {
            throw new InvalidOperationException(
                $"RunStore mapping mismatch: {Columns.Length} SQL columns but RunState has {runStateFieldCount} fields.");
        }

        var unknown = SyncColumns.Select(c => c.Column).Except(Columns).ToArray();
        if (unknown.Length > 0)
        {
            throw new InvalidOperationException(
                $"RunStore sync mismatch: {string.Join(", ", unknown)} not in SQL columns.");
        }
    }

    public RunStore(
        NpgsqlDataSource dataSource,
        ILogger<RunStore> logger,
        IGitHubIssuesClient gitHub,
        IHostStore hostStore,
        IIssueResolver resolver)
    {
        _dataSource = dataSource;
        _logger = logger;
        _gitHub = gitHub;
        _hostStore = hostStore;
        _resolver = resolver;
    }

    public async Task<IReadOnlyList<RunState>> All()
    {
        return await Query($"{SelectSql} ORDER BY started_at DESC");
    }

    public async Task<IReadOnlyList<RunState>> All(int skip, int take)
    {
        return await Query(
            $"{SelectSql} ORDER BY started_at DESC LIMIT @take OFFSET @skip",
            new NpgsqlParameter("skip", skip),
            new NpgsqlParameter("take", take));
    }

    public async Task<IReadOnlyList<RunState>> Active()
    {
        return await Query($"{SelectSql} WHERE status IN ({ActiveStatusList}) ORDER BY started_at DESC");
    }

    public async Task<RunState?> GetRun(Guid runId)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand($"{SelectSql} WHERE run_id = @id", conn);
        cmd.Parameters.AddWithValue("id", runId);
        await using var r = await cmd.ExecuteReaderAsync();

        if (!await r.ReadAsync())
        {
            return null;
        }

        return MapRun(r);
    }

    public async Task<RunState?> Apply(Guid runId, RunEvent ev)
    {
        ev = ev with { RunId = runId };

        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        // A run event only arrives via the host relay, so any event proves the
        // host is alive — including during a blocking job when it never claims.
        await _hostStore.StampLastSeen();

        var current = await GetLocked(runId, conn, tx);
        var next = RunFold.Apply(current, ev);
        if (next is null)
        {
            await tx.CommitAsync();
            return current;
        }

        await Persist(next, current, conn, tx);

        LinkedIssue? linkedIssue = null;
        if (ev is RunFinishedEvent)
        {
            // ponytail: the linked-issue resolve and the queue-slot release live here in
            // Apply deliberately, composed on this transaction rather than orchestrated
            // elsewhere. They only make sense inside it — the resolve must see the queue
            // row before the release deletes it, and the release must be atomic with the
            // run's terminal write. That is also why the resolver seam takes the
            // caller's conn/tx: the old conn/tx-passing IRunCompletion seam was deleted
            // for being one implementation with zero abstracted behaviour, while
            // IIssueResolver is a real seam (SQL and in-memory implementations, queue
            // and run callers) that moves the cross-domain issues/queue SQL to its
            // owning domain without moving it out of this transaction.
            linkedIssue = await _resolver.ResolveLinkedIssueAsync(runId, conn, tx);
            await _resolver.ReleaseQueueSlotAsync(runId, conn, tx);
        }

        await tx.CommitAsync();

        if (linkedIssue is not null)
        {
            // Best-effort and post-commit on purpose: the run's terminal write is durable
            // regardless of GitHub's answer, and a failed close never fails the event.
            try
            {
                await _gitHub.CloseIssueAsync(linkedIssue.Repo, linkedIssue.Number);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to close GitHub issue {Repo}#{Number} for finished run {RunId}; continuing.",
                    linkedIssue.Repo,
                    linkedIssue.Number,
                    runId);
            }
        }

        return next;
    }

    // Hard-delete a run and release any queue slot still linked to it, atomically.
    // Used by the dashboard to clear stuck runs (e.g. a "launching" row whose VM
    // never sent another event). The queue release mirrors the run-finished release in
    // Apply so a deleted run leaves no orphaned queue row.
    public async Task<bool> Delete(Guid runId)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await _resolver.ReleaseQueueSlotAsync(runId, conn, tx);

        await using var deleteRunCmd = new NpgsqlCommand(
            "DELETE FROM runs WHERE run_id = @runId;", conn, tx);
        deleteRunCmd.Parameters.AddWithValue("runId", runId);
        var rows = await deleteRunCmd.ExecuteNonQueryAsync();

        await tx.CommitAsync();
        return rows == 1;
    }

    private static async Task<RunState?> GetLocked(Guid runId, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        await using var cmd = new NpgsqlCommand($"{SelectSql} WHERE run_id = @id FOR UPDATE", conn, tx);
        cmd.Parameters.AddWithValue("id", runId);
        await using var r = await cmd.ExecuteReaderAsync();

        if (!await r.ReadAsync())
        {
            return null;
        }

        return MapRun(r);
    }

    private static async Task Persist(RunState next, RunState? current, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        if (current is null)
        {
            await Insert(next, conn, tx);
            return;
        }

        if (IsHeartbeatOnly(current, next))
        {
            await UpdateHeartbeat(next, conn, tx);
            return;
        }

        await UpdateDiff(current, next, conn, tx);
    }

    private static async Task Insert(RunState next, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO runs (run_id, repo, branch, spec, model, status, started_at, updated_at, stages, steps)
            VALUES (@id, @repo, @branch, @spec, @model, @status, @startedAt, @updatedAt, @stages, @steps)
            ON CONFLICT (run_id) DO NOTHING;
            """, conn, tx);
        cmd.Parameters.AddWithValue("id", next.RunId);
        cmd.Parameters.AddWithValue("repo", next.Repo);
        cmd.Parameters.AddWithValue("branch", next.Branch);
        cmd.Parameters.AddWithValue("spec", next.Spec);
        cmd.Parameters.AddWithValue("model", next.Model);
        cmd.Parameters.AddWithValue("status", next.Status);
        cmd.Parameters.AddWithValue("startedAt", next.StartedAt);
        cmd.Parameters.AddWithValue("updatedAt", next.UpdatedAt);
        cmd.Parameters.Add(new NpgsqlParameter("stages", NpgsqlDbType.Jsonb)
        {
            Value = (object?)SerializeJson(next.Stages) ?? DBNull.Value,
        });
        cmd.Parameters.Add(new NpgsqlParameter("steps", NpgsqlDbType.Jsonb)
        {
            Value = (object?)SerializeJson(next.Steps) ?? DBNull.Value,
        });
        await cmd.ExecuteNonQueryAsync();
    }

    private static string? SerializeJson<T>(IReadOnlyList<T>? value)
    {
        if (value is null || value.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static bool IsHeartbeatOnly(RunState current, RunState next)
    {
        return next.LastHeartbeatAt != current.LastHeartbeatAt &&
            SyncColumns.Where(c => !c.Heartbeat).All(c => Equals(c.Get(current), c.Get(next)));
    }

    private static async Task UpdateHeartbeat(RunState next, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        // currentPhase rides the heartbeat: it advances only at phase transitions,
        // so it is written on the same cheap path as last_heartbeat_at. The
        // last_heartbeat_at guard rejects out-of-order heartbeats wholesale.
        var hb = SyncColumns.Where(c => c.Heartbeat).ToArray();
        var beatAt = hb.Single(c => c.Column == "last_heartbeat_at");
        var sql = $"UPDATE runs SET {string.Join(", ", hb.Select(c => $"{c.Column} = @{c.Param}"))} " +
            $"WHERE run_id = @id AND (last_heartbeat_at IS NULL OR @{beatAt.Param} > last_heartbeat_at);";
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("id", next.RunId);
        foreach (var c in hb)
        {
            AddParam(cmd, c, c.Get(next));
        }
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task UpdateDiff(RunState current, RunState next, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        var changes = SyncColumns
            .Where(c => !c.Heartbeat && !Equals(c.Get(current), c.Get(next)))
            .ToList();
        if (changes.Count == 0)
        {
            return;
        }

        var updatedAt = SyncColumns.Single(c => c.Column == "updated_at");
        var setClauses = changes.Select(c => $"{c.Column} = @{c.Param}");
        var sql = $"UPDATE runs SET {string.Join(", ", setClauses)} WHERE run_id = @id AND @{updatedAt.Param} > updated_at";

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("id", next.RunId);
        foreach (var c in changes)
        {
            AddParam(cmd, c, c.Get(next));
        }
        await cmd.ExecuteNonQueryAsync();
    }

    private static void AddParam(NpgsqlCommand cmd, ColumnSync column, object? value)
    {
        if (column.DbType.HasValue)
        {
            cmd.Parameters.Add(new NpgsqlParameter(column.Param, column.DbType.Value)
            {
                Value = value ?? DBNull.Value,
            });
            return;
        }

        cmd.Parameters.AddWithValue(column.Param, value ?? DBNull.Value);
    }

    private async Task<IReadOnlyList<RunState>> Query(string sql, params NpgsqlParameter[] parameters)
    {
        var runs = new List<RunState>();
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var p in parameters)
        {
            cmd.Parameters.Add(p);
        }
        await using var r = await cmd.ExecuteReaderAsync();

        while (await r.ReadAsync())
        {
            runs.Add(MapRun(r));
        }

        return runs;
    }

    private static RunState MapRun(NpgsqlDataReader r)
    {
        if (r.FieldCount != Columns.Length)
        {
            throw new InvalidOperationException(
                $"RunStore mapping mismatch: expected {Columns.Length} columns but reader returned {r.FieldCount}.");
        }

        return new RunState(
            r.GetGuid(r.GetOrdinal("run_id")),
            r.GetString(r.GetOrdinal("repo")),
            r.GetString(r.GetOrdinal("branch")),
            r.GetString(r.GetOrdinal("spec")),
            r.GetString(r.GetOrdinal("model")),
            GetStringOrNull(r, "vm_name"),
            r.GetString(r.GetOrdinal("status")),
            r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("started_at")),
            GetDateTimeOffsetOrNull(r, "finished_at"),
            GetDateTimeOffsetOrNull(r, "last_heartbeat_at"),
            GetStringOrNull(r, "pr_url"),
            GetStringOrNull(r, "failure_reason"),
            r.GetBoolean(r.GetOrdinal("freeze_captured")),
            GetStringOrNull(r, "freeze_local_path"),
            r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("updated_at")),
            GetJsonList<RunStage>(r, "stages"),
            GetStringOrNull(r, "current_phase"),
            GetJsonList<RunStep>(r, "steps"));
    }

    private static string? GetStringOrNull(NpgsqlDataReader r, string column)
    {
        var ordinal = r.GetOrdinal(column);
        return r.IsDBNull(ordinal) ? null : r.GetString(ordinal);
    }

    private static DateTimeOffset? GetDateTimeOffsetOrNull(NpgsqlDataReader r, string column)
    {
        var ordinal = r.GetOrdinal(column);
        return r.IsDBNull(ordinal) ? null : r.GetFieldValue<DateTimeOffset>(ordinal);
    }

    private static List<T>? GetJsonList<T>(NpgsqlDataReader r, string column)
    {
        var ordinal = r.GetOrdinal(column);
        if (r.IsDBNull(ordinal))
        {
            return null;
        }

        var json = r.GetString(ordinal);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<List<T>>(json, JsonOptions);
    }
}
