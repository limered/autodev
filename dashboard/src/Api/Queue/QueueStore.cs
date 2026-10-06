using Api.Catalogs;
using Api.Host;
using Api.Issues;
using Npgsql;

namespace Api.Queue;

public interface IQueueStore
{
    Task<IReadOnlyList<QueueRow>> All();
    Task<QueueRow?> Enqueue(long issueId);
    Task<QueueRow?> SetWorkflow(long id, string? workflow);
    Task<QueueRow?> StartNext(long id);
    Task<QueueRow?> Restart(long id);
    Task<ClaimedQueueItem?> ClaimNext();
    Task Reorder(IReadOnlyList<long> ids);
    Task<bool> Delete(long id);
    Task<QueueRow?> PrepareResume(Guid oldRunId, string branch, string? resumeStage, Guid parentRunId);
}

public record ClaimedQueueItem(
    Guid RunId,
    string RepoUrl,
    string Spec,
    string? CatalogSha = null,
    string CatalogSource = CatalogRules.SourceFactoryFallback,
    string? CatalogContent = null,
    string? Branch = null,
    string? ResumeStage = null,
    Guid? ParentRunId = null,
    long? QueueId = null,
    string? Workflow = null);

public sealed class QueueStore : IQueueStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IHostStore _hostStore;
    private readonly IIssueResolver _resolver;
    private readonly ITargetCatalogService? _catalogs;
    private readonly IFactoryWorkflows? _factory;

    public QueueStore(
        NpgsqlDataSource dataSource,
        IHostStore hostStore,
        IIssueResolver resolver,
        ITargetCatalogService? catalogs = null,
        IFactoryWorkflows? factory = null)
    {
        _dataSource = dataSource;
        _hostStore = hostStore;
        _resolver = resolver;
        _catalogs = catalogs;
        _factory = factory;
    }

    public async Task<IReadOnlyList<QueueRow>> All()
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        var items = await QueryItems($"{SelectQueueSql} ORDER BY q.rank", conn);
        return items;
    }

    public async Task<QueueRow?> Enqueue(long issueId)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        // Mechanical read of the queue's own rows inside the transaction; QueueRules
        // makes the dedup and next-rank decisions so they live once, shared with the
        // fake. Rule decisions need no issue/run enrichment, so the snapshot skips
        // the API select's LEFT JOINs.
        var items = await QueryRuleItems(SelectQueueRuleSql, conn, tx);

        var existing = QueueRules.ExistingForIssue(items, issueId);
        if (existing is not null)
        {
            await tx.CommitAsync();
            return await GetById(existing.Id);
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

    /// <summary>
    /// Sets the row's workflow pick while it is unclaimed: the pick is changeable
    /// freely until the claim freezes it. A claimed row (or unknown id) answers the
    /// row unchanged (or null), so the caller re-syncs and shows the frozen pick.
    /// </summary>
    public async Task<QueueRow?> SetWorkflow(long id, string? workflow)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using var cmd = new NpgsqlCommand(
            """
            UPDATE queue
            SET workflow = @workflow
            WHERE id = @id AND run_id IS NULL
            RETURNING id;
            """, conn, tx);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("workflow", (object?)workflow ?? DBNull.Value);

        var updated = await cmd.ExecuteScalarAsync();
        await tx.CommitAsync();

        return await GetById(updated is long updatedId ? updatedId : id);
    }

    public async Task<QueueRow?> StartNext(long id)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var rowRepo = await GetRepoForQueueIdAsync(id, conn, tx);
        if (rowRepo is not null && _catalogs is not null)
        {
            await _catalogs.RevalidateAsync(rowRepo);
        }

        // QueueRules.ShouldRequestStart pushed to SQL: the timestamp is set only when
        // none was requested before, so a repeat call keeps the first timestamp.
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

    public async Task<QueueRow?> Restart(long id)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using var cmd = new NpgsqlCommand(
            """
            UPDATE queue
            SET run_id = NULL,
                start_requested_at = NULL,
                resume_branch = NULL,
                resume_stage = NULL,
                parent_run_id = NULL
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

        // Mechanical snapshot of the queue's own rows; QueueRules decides which rows
        // are claimable and in what order, shared with the fake so the predicate and
        // ordering cannot drift. Claiming needs no issue/run enrichment, so the
        // snapshot skips the API select's LEFT JOINs.
        var items = await QueryRuleItems(SelectQueueRuleSql, conn, tx);

        QueueRuleItem? claimed = null;
        IssueClaimPayload? payload = null;
        TargetCatalog? catalog = null;
        string? workflow = null;
        foreach (var candidate in QueueRules.ClaimableInRankOrder(items))
        {
            claimed = await TryLockClaimable(candidate.Id, conn, tx);
            if (claimed is null)
            {
                continue;
            }

            payload = await _resolver.ResolveClaimPayloadAsync(claimed.IssueId, conn, tx);
            if (payload is null)
            {
                break;
            }

            catalog = await ResolveCatalogForIssueAsync(claimed.IssueId, conn, tx);
            var (defaultName, names) = CatalogRules.WorkflowNames(catalog, _factory);
            var decision = QueueRules.DecidePick(claimed.Workflow, defaultName, names);
            if (decision.Skip)
            {
                // Stale pick: the row is released from the claim attempt — it stays
                // unclaimed with no run, and retries once the next Issue sync
                // refreshes the catalog or the pick is reset. Never a silent fallback
                // to the default.
                claimed = null;
                payload = null;
                catalog = null;
                continue;
            }

            workflow = decision.Resolved;
            break;
        }

        if (claimed is not null && payload is not null)
        {
            var resume = await GetResumeForQueueIdAsync(claimed.Id, conn, tx);
            await using var updateCmd = new NpgsqlCommand(
                "UPDATE queue SET run_id = @runId, resume_branch = NULL, resume_stage = NULL, parent_run_id = NULL WHERE id = @id;", conn, tx);
            updateCmd.Parameters.AddWithValue("runId", runId);
            updateCmd.Parameters.AddWithValue("id", claimed.Id);
            await updateCmd.ExecuteNonQueryAsync();

            await tx.CommitAsync();
            await _hostStore.StampLastSeen();

            return new ClaimedQueueItem(
                runId,
                payload.RepoUrl,
                payload.Spec,
                catalog?.Sha,
                catalog?.Source ?? CatalogRules.SourceFactoryFallback,
                catalog?.Content,
                resume?.Branch,
                resume?.Stage,
                resume?.ParentRunId,
                claimed.Id,
                workflow);
        }

        await tx.CommitAsync();
        await _hostStore.StampLastSeen();

        return null;
    }

    private async Task<TargetCatalog?> ResolveCatalogForIssueAsync(long issueId, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        var repo = await GetRepoForIssueIdAsync(issueId, conn, tx);
        if (repo is null)
        {
            return null;
        }

        if (_catalogs is null)
        {
            return null;
        }

        return await _catalogs.RevalidateAsync(repo);
    }

    private static async Task<string?> GetRepoForIssueIdAsync(long issueId, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT i.repo FROM issues i WHERE i.github_id = @issueId;", conn, tx);
        cmd.Parameters.AddWithValue("issueId", issueId);
        var value = await cmd.ExecuteScalarAsync();
        return value as string;
    }

    private static async Task<string?> GetRepoForQueueIdAsync(long queueId, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT i.repo
            FROM queue q
            LEFT JOIN issues i ON i.github_id = q.issue_id
            WHERE q.id = @id;
            """, conn, tx);
        cmd.Parameters.AddWithValue("id", queueId);
        var value = await cmd.ExecuteScalarAsync();
        return value as string;
    }

    public async Task<QueueRow?> PrepareResume(Guid oldRunId, string branch, string? resumeStage, Guid parentRunId)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using var cmd = new NpgsqlCommand(
            """
            UPDATE queue
            SET run_id = NULL,
                start_requested_at = now(),
                resume_branch = @branch,
                resume_stage = @resumeStage,
                parent_run_id = @parentRunId
            WHERE run_id = @oldRunId
            RETURNING id;
            """, conn, tx);
        cmd.Parameters.AddWithValue("oldRunId", oldRunId);
        cmd.Parameters.AddWithValue("branch", branch);
        cmd.Parameters.AddWithValue("resumeStage", (object?)resumeStage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("parentRunId", parentRunId);

        var id = await cmd.ExecuteScalarAsync();
        await tx.CommitAsync();

        if (id is null)
        {
            return null;
        }

        return await GetById((long)id);
    }

    private sealed record ResumeCarry(string? Branch, string? Stage, Guid? ParentRunId);

    private static async Task<ResumeCarry?> GetResumeForQueueIdAsync(long queueId, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT resume_branch, resume_stage, parent_run_id FROM queue WHERE id = @id;", conn, tx);
        cmd.Parameters.AddWithValue("id", queueId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var branch = reader.IsDBNull(0) ? null : reader.GetString(0);
        var stage = reader.IsDBNull(1) ? null : reader.GetString(1);
        Guid? parent = reader.IsDBNull(2) ? null : reader.GetGuid(2);
        return new ResumeCarry(branch, stage, parent);
    }

    private static async Task<QueueRuleItem?> TryLockClaimable(long id, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        // Lock exactly one candidate row. SKIP LOCKED keeps a row mid-claim by a
        // concurrent transaction invisible — the guarantee the old single-statement
        // claim had — so the caller falls through to the next candidate, never waits.
        var rows = await QueryRuleItems(
            $"{SelectQueueRuleSql} WHERE q.id = @id FOR UPDATE OF q SKIP LOCKED",
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

    private async Task<QueueRow?> GetById(long id)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        var items = await QueryItems($"{SelectQueueSql} WHERE q.id = @id", conn, parameters: new NpgsqlParameter("id", id));
        return items.SingleOrDefault();
    }

    private static async Task<List<T>> Query<T>(
        string sql,
        NpgsqlConnection conn,
        NpgsqlTransaction? tx,
        Func<NpgsqlDataReader, T> map,
        params NpgsqlParameter[] parameters)
    {
        var items = new List<T>();
        await using var cmd = tx is null ? new NpgsqlCommand(sql, conn) : new NpgsqlCommand(sql, conn, tx);
        foreach (var p in parameters)
        {
            cmd.Parameters.Add(p);
        }

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(map(reader));
        }

        return items;
    }

    private static Task<List<QueueRow>> QueryItems(
        string sql,
        NpgsqlConnection conn,
        NpgsqlTransaction? tx = null,
        params NpgsqlParameter[] parameters)
        => Query(sql, conn, tx, Map, parameters);

    private static Task<List<QueueRuleItem>> QueryRuleItems(
        string sql,
        NpgsqlConnection conn,
        NpgsqlTransaction? tx = null,
        params NpgsqlParameter[] parameters)
        => Query(sql, conn, tx, MapRuleItem, parameters);

    /// <summary>The joined-row select: the queue row plus the issue/run enrichments served as the response. The LEFT JOINs keep rows whose issue/run is missing.</summary>
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
            (i.github_id IS NOT NULL) AS issue_present,
            q.resume_branch,
            q.resume_stage,
            q.parent_run_id,
            q.workflow
        FROM queue q
        LEFT JOIN issues i ON i.github_id = q.issue_id
        LEFT JOIN runs r ON r.run_id = q.run_id
        """;

    /// <summary>The rule select: the queue's own rows, join-free — all QueueRules needs.</summary>
    private const string SelectQueueRuleSql =
        """
        SELECT
            q.id,
            q.issue_id,
            q.rank,
            q.run_id,
            q.start_requested_at,
            q.workflow
        FROM queue q
        """;

    private static QueueRow Map(NpgsqlDataReader r)
    {
        return new QueueRow(
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
            r.GetBoolean(r.GetOrdinal("issue_present")),
            GetStringOrNull(r, "resume_branch"),
            GetStringOrNull(r, "resume_stage"),
            GetGuidOrNull(r, "parent_run_id"),
            GetStringOrNull(r, "workflow"));
    }

    private static QueueRuleItem MapRuleItem(NpgsqlDataReader r) =>
        new(
            r.GetInt64(r.GetOrdinal("id")),
            r.GetInt64(r.GetOrdinal("issue_id")),
            r.GetInt32(r.GetOrdinal("rank")),
            GetGuidOrNull(r, "run_id"),
            GetDateTimeOffsetOrNull(r, "start_requested_at"),
            GetStringOrNull(r, "workflow"));

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
