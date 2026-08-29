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
    Task<RunState?> Get(Guid runId);
    Task<RunState?> Apply(Guid runId, RunEvent ev);
}

public sealed class RunStore : IRunStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<RunStore> _logger;
    private readonly IGitHubIssuesClient _gitHub;
    private readonly IHostStore _hostStore;
    private readonly IRunCompletion _completion;

    private const string RunColumns =
        "run_id, repo, branch, spec, model, vm_name, status, started_at, finished_at, last_heartbeat_at, pr_url, failure_reason, freeze_captured, freeze_local_path, updated_at, stages, current_phase";

    private static readonly JsonSerializerOptions StagesJsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] Columns = RunColumns.Split(',').Select(c => c.Trim()).ToArray();
    private static readonly string SelectSql = $"SELECT {RunColumns} FROM runs";

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
    }

    public RunStore(NpgsqlDataSource dataSource, ILogger<RunStore> logger, IGitHubIssuesClient gitHub, IHostStore hostStore, IRunCompletion completion)
    {
        _dataSource = dataSource;
        _logger = logger;
        _gitHub = gitHub;
        _hostStore = hostStore;
        _completion = completion;
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
        return await Query($"{SelectSql} WHERE status IN ('launching', 'running', 'stalled') ORDER BY started_at DESC");
    }

    public async Task<RunState?> Get(Guid runId)
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

        (string Repo, int Number)? issueToClose = null;
        if (ev.Type == "run-finished")
        {
            issueToClose = await _completion.OnFinished(runId, conn, tx);
        }

        await tx.CommitAsync();

        if (issueToClose.HasValue)
        {
            try
            {
                await _gitHub.CloseIssueAsync(issueToClose.Value.Repo, issueToClose.Value.Number);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to close GitHub issue {Repo}#{Number} for finished run {RunId}; continuing.",
                    issueToClose.Value.Repo,
                    issueToClose.Value.Number,
                    runId);
            }
        }

        return next;
    }

    private async Task<RunState?> GetLocked(Guid runId, NpgsqlConnection conn, NpgsqlTransaction tx)
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
            INSERT INTO runs (run_id, repo, branch, spec, model, status, started_at, updated_at, stages)
            VALUES (@id, @repo, @branch, @spec, @model, @status, @startedAt, @updatedAt, @stages)
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
            Value = (object?)SerializeStages(next.Stages) ?? DBNull.Value,
        });
        await cmd.ExecuteNonQueryAsync();
    }

    private static string? SerializeStages(IReadOnlyList<RunStage>? stages)
    {
        if (stages is null || stages.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(stages, StagesJsonOptions);
    }

    private static bool IsHeartbeatOnly(RunState current, RunState next)
    {
        return next.LastHeartbeatAt != current.LastHeartbeatAt &&
               next.UpdatedAt == current.UpdatedAt &&
               next.VmName == current.VmName &&
               next.Status == current.Status &&
               next.FinishedAt == current.FinishedAt &&
               next.PrUrl == current.PrUrl &&
               next.FailureReason == current.FailureReason &&
               next.FreezeCaptured == current.FreezeCaptured &&
               next.FreezeLocalPath == current.FreezeLocalPath;
    }

    private static async Task UpdateHeartbeat(RunState next, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        // currentPhase rides the heartbeat: it advances only at phase transitions,
        // so it is written on the same cheap path as last_heartbeat_at. The
        // last_heartbeat_at guard rejects out-of-order heartbeats wholesale.
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE runs
            SET last_heartbeat_at = @lastHeartbeatAt,
                current_phase = @currentPhase
            WHERE run_id = @id
              AND (last_heartbeat_at IS NULL OR @lastHeartbeatAt > last_heartbeat_at);
            """, conn, tx);
        cmd.Parameters.AddWithValue("id", next.RunId);
        cmd.Parameters.AddWithValue("lastHeartbeatAt", next.LastHeartbeatAt!.Value);
        cmd.Parameters.AddWithValue("currentPhase", (object?)next.CurrentPhase ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task UpdateDiff(RunState current, RunState next, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        var changes = new List<(string column, string param, object? value)>();
        if (next.VmName != current.VmName) changes.Add(("vm_name", "vm", (object?)next.VmName ?? DBNull.Value));
        if (next.Status != current.Status) changes.Add(("status", "status", next.Status));
        if (next.FinishedAt != current.FinishedAt) changes.Add(("finished_at", "finishedAt", (object?)next.FinishedAt ?? DBNull.Value));
        if (next.PrUrl != current.PrUrl) changes.Add(("pr_url", "prUrl", (object?)next.PrUrl ?? DBNull.Value));
        if (next.FailureReason != current.FailureReason) changes.Add(("failure_reason", "failureReason", (object?)next.FailureReason ?? DBNull.Value));
        if (next.FreezeCaptured != current.FreezeCaptured) changes.Add(("freeze_captured", "freezeCaptured", next.FreezeCaptured));
        if (next.FreezeLocalPath != current.FreezeLocalPath) changes.Add(("freeze_local_path", "freezeLocalPath", (object?)next.FreezeLocalPath ?? DBNull.Value));
        if (next.UpdatedAt != current.UpdatedAt) changes.Add(("updated_at", "updatedAt", next.UpdatedAt));

        if (changes.Count == 0)
        {
            return;
        }

        var setClauses = changes.Select(c => $"{c.column} = @{c.param}");
        var sql = $"UPDATE runs SET {string.Join(", ", setClauses)} WHERE run_id = @id AND @updatedAt > updated_at";

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("id", next.RunId);
        foreach (var (_, param, value) in changes)
        {
            cmd.Parameters.AddWithValue(param, value ?? DBNull.Value);
        }
        await cmd.ExecuteNonQueryAsync();
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
            GetStagesOrNull(r, "stages"),
            GetStringOrNull(r, "current_phase"));
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

    private static IReadOnlyList<RunStage>? GetStagesOrNull(NpgsqlDataReader r, string column)
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

        return JsonSerializer.Deserialize<List<RunStage>>(json, StagesJsonOptions);
    }
}
