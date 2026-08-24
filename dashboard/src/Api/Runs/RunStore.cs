using System.Reflection;
using Npgsql;

namespace Api.Runs;

public interface IRunStore
{
    Task<IReadOnlyList<RunState>> All();
    Task<IReadOnlyList<RunState>> Active();
    Task<RunState?> Get(Guid runId);
}

public sealed class RunStore : IRunStore
{
    private readonly NpgsqlDataSource _dataSource;

    private const string RunColumns =
        "run_id, repo, branch, spec, model, vm_name, status, started_at, finished_at, last_heartbeat_at, pr_url, failure_reason, freeze_captured, freeze_local_path, updated_at";

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

    public RunStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IReadOnlyList<RunState>> All()
    {
        return await Query($"{SelectSql} ORDER BY started_at DESC");
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

    private async Task<IReadOnlyList<RunState>> Query(string sql)
    {
        var runs = new List<RunState>();
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
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
            r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("updated_at")));
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
}
