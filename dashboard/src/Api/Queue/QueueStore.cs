using Api.Host;
using Npgsql;

namespace Api.Queue;

public interface IQueueStore
{
    Task<IReadOnlyList<QueueListItem>> All();
    Task<QueueListItem?> Enqueue(long issueId);
    Task<QueueListItem?> StartNext(long id);
    Task<QueueListItem?> Restart(long id);
    Task<ClaimedQueueItem?> ClaimNext();
    Task Reorder(IReadOnlyList<long> ids);
    Task<bool> Delete(long id);
}

public record ClaimedQueueItem(Guid RunId, string RepoUrl, string Spec);

public sealed class QueueStore : IQueueStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IHostStore _hostStore;

    public QueueStore(NpgsqlDataSource dataSource, IHostStore hostStore)
    {
        _dataSource = dataSource;
        _hostStore = hostStore;
    }

    public async Task<IReadOnlyList<QueueListItem>> All()
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        var items = await QueryItems($"{SelectQueueSql} ORDER BY q.rank", conn);
        return items;
    }

    public async Task<QueueListItem?> Enqueue(long issueId)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        // Mechanical read of the queue inside the transaction; QueueRules makes the
        // dedup and next-rank decisions so they live once, shared with the fake.
        var items = await QueryItems(SelectQueueSql, conn, tx);

        var existing = QueueRules.ExistingForIssue(items, issueId);
        if (existing is not null)
        {
            await tx.CommitAsync();
            return existing;
        }

        var rank = QueueRules.NextRank(items.Select(i => i.Rank));

        await using var insertCmd = new NpgsqlCommand(
            """
            INSERT INTO queue (issue_id, rank)
            VALUES (@issueId, @rank)
            RETURNING id;
            """, conn, tx);
        insertCmd.Parameters.AddWithValue("issueId", issueId);
        insertCmd.Parameters.AddWithValue("rank", rank);
        var id = (long)(await insertCmd.ExecuteScalarAsync() ?? 0L);

        await tx.CommitAsync();

        return await GetById(id);
    }

    public async Task<QueueListItem?> StartNext(long id)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using var cmd = new NpgsqlCommand(
            """
            UPDATE queue
            SET start_requested_at = now()
            WHERE id = @id
              AND start_requested_at IS NULL
            RETURNING id;
            """, conn, tx);
        cmd.Parameters.AddWithValue("id", id);

        var updatedId = await cmd.ExecuteScalarAsync();
        await tx.CommitAsync();

        if (updatedId is null)
        {
            return await GetById(id);
        }

        return await GetById((long)updatedId);
    }

    public async Task<QueueListItem?> Restart(long id)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using var cmd = new NpgsqlCommand(
            """
            UPDATE queue
            SET run_id = NULL,
                start_requested_at = NULL
            WHERE id = @id
            RETURNING id;
            """, conn, tx);
        cmd.Parameters.AddWithValue("id", id);

        await cmd.ExecuteScalarAsync();
        await tx.CommitAsync();

        return await GetById(id);
    }

    public async Task<ClaimedQueueItem?> ClaimNext()
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var runId = Guid.NewGuid();

        // Mechanical snapshot; QueueRules decides which rows are claimable and in what
        // order, shared with the fake so the predicate and ordering cannot drift.
        var items = await QueryItems(SelectQueueSql, conn, tx);

        QueueListItem? claimed = null;
        foreach (var candidate in QueueRules.ClaimableInRankOrder(items))
        {
            claimed = await TryLockClaimable(candidate.Id, conn, tx);
            if (claimed is not null)
            {
                break;
            }
        }

        // The claim used to be one statement that inner-joined issues: a queue row
        // whose issue has no issues row matched nothing and claimed nothing. Keep that.
        string? repo = null;
        string? body = null;
        string? title = null;
        if (claimed is not null)
        {
            await using var issueCmd = new NpgsqlCommand(
                "SELECT i.repo, i.body, i.title FROM issues i WHERE i.github_id = @issueId;", conn, tx);
            issueCmd.Parameters.AddWithValue("issueId", claimed.IssueId);
            await using var reader = await issueCmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                repo = reader.GetString(0);
                body = reader.IsDBNull(1) ? null : reader.GetString(1);
                title = reader.IsDBNull(2) ? null : reader.GetString(2);
            }
        }

        if (claimed is not null && repo is not null)
        {
            await using var updateCmd = new NpgsqlCommand(
                "UPDATE queue SET run_id = @runId WHERE id = @id;", conn, tx);
            updateCmd.Parameters.AddWithValue("runId", runId);
            updateCmd.Parameters.AddWithValue("id", claimed.Id);
            await updateCmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        await _hostStore.StampLastSeen();

        if (claimed is null || repo is null)
        {
            return null;
        }

        var repoUrl = $"https://github.com/{repo}.git";
        var spec = !string.IsNullOrWhiteSpace(body) ? body.Trim() : title ?? repo;
        return new ClaimedQueueItem(runId, repoUrl, spec);
    }

    private static async Task<QueueListItem?> TryLockClaimable(long id, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        // Lock exactly one candidate row. SKIP LOCKED keeps a row mid-claim by a
        // concurrent transaction invisible — the guarantee the old single-statement
        // claim had — so the caller falls through to the next candidate, never waits.
        var rows = await QueryItems(
            $"{SelectQueueSql} WHERE q.id = @id FOR UPDATE OF q SKIP LOCKED",
            conn, tx, new NpgsqlParameter("id", id));
        var locked = rows.SingleOrDefault();
        if (locked is null)
        {
            return null;
        }

        // The snapshot may be stale (a concurrent claimer won the race and committed);
        // re-apply the shared rule against the row's current state under the lock.
        return QueueRules.IsClaimable(locked) ? locked : null;
    }

    public async Task Reorder(IReadOnlyList<long> ids)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        const string sql =
            """
            UPDATE queue
            SET rank = @rank
            WHERE id = @id;
            """;

        for (var i = 0; i < ids.Count; i++)
        {
            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("id", ids[i]);
            cmd.Parameters.AddWithValue("rank", i + 1);
            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    public async Task<bool> Delete(long id)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM queue WHERE id = @id;",
            conn);
        cmd.Parameters.AddWithValue("id", id);

        var rows = await cmd.ExecuteNonQueryAsync();
        return rows == 1;
    }

    private async Task<QueueListItem?> GetById(long id)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        var items = await QueryItems($"{SelectQueueSql} WHERE q.id = @id", conn, parameters: new NpgsqlParameter("id", id));
        return items.SingleOrDefault();
    }

    private static async Task<List<QueueListItem>> QueryItems(
        string sql,
        NpgsqlConnection conn,
        NpgsqlTransaction? tx = null,
        params NpgsqlParameter[] parameters)
    {
        var items = new List<QueueListItem>();
        await using var cmd = tx is null ? new NpgsqlCommand(sql, conn) : new NpgsqlCommand(sql, conn, tx);
        foreach (var p in parameters)
        {
            cmd.Parameters.Add(p);
        }

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(Map(reader));
        }

        return items;
    }

    private const string SelectQueueSql =
        """
        SELECT
            q.id,
            q.issue_id,
            q.rank,
            q.run_id,
            q.start_requested_at,
            r.status AS run_status,
            i.title,
            i.repo,
            i.number,
            i.html_url,
            i.state AS issue_state,
            (i.github_id IS NOT NULL) AS issue_present
        FROM queue q
        LEFT JOIN issues i ON i.github_id = q.issue_id
        LEFT JOIN runs r ON r.run_id = q.run_id
        """;

    private static QueueListItem Map(NpgsqlDataReader r)
    {
        return new QueueListItem(
            r.GetInt64(r.GetOrdinal("id")),
            r.GetInt64(r.GetOrdinal("issue_id")),
            r.GetInt32(r.GetOrdinal("rank")),
            GetGuidOrNull(r, "run_id"),
            GetDateTimeOffsetOrNull(r, "start_requested_at"),
            GetStringOrNull(r, "run_status"),
            GetStringOrNull(r, "title"),
            GetStringOrNull(r, "repo"),
            GetInt32OrNull(r, "number"),
            GetStringOrNull(r, "html_url"),
            GetStringOrNull(r, "issue_state"),
            r.GetBoolean(r.GetOrdinal("issue_present")));
    }

    private static DateTimeOffset? GetDateTimeOffsetOrNull(NpgsqlDataReader r, string column)
    {
        var ordinal = r.GetOrdinal(column);
        return r.IsDBNull(ordinal) ? null : r.GetFieldValue<DateTimeOffset>(ordinal);
    }

    private static string? GetStringOrNull(NpgsqlDataReader r, string column)
    {
        var ordinal = r.GetOrdinal(column);
        return r.IsDBNull(ordinal) ? null : r.GetString(ordinal);
    }

    private static Guid? GetGuidOrNull(NpgsqlDataReader r, string column)
    {
        var ordinal = r.GetOrdinal(column);
        return r.IsDBNull(ordinal) ? null : r.GetGuid(ordinal);
    }

    private static int? GetInt32OrNull(NpgsqlDataReader r, string column)
    {
        var ordinal = r.GetOrdinal(column);
        return r.IsDBNull(ordinal) ? null : r.GetInt32(ordinal);
    }
}
