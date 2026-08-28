using Npgsql;

namespace Api.Queue;

public interface IQueueStore
{
    Task<IReadOnlyList<QueueListItem>> All();
    Task<QueueListItem?> Enqueue(long issueId);
    Task Reorder(IReadOnlyList<long> ids);
    Task<bool> Delete(long id);
}

public sealed class QueueStore : IQueueStore
{
    private readonly NpgsqlDataSource _dataSource;

    public QueueStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IReadOnlyList<QueueListItem>> All()
    {
        var items = new List<QueueListItem>();
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(SelectQueueSql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            items.Add(Map(reader));
        }

        return items;
    }

    public async Task<QueueListItem?> Enqueue(long issueId)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        QueueListItem? existing = null;
        {
            await using var existingCmd = new NpgsqlCommand(
                $"{SelectQueueSql} WHERE q.issue_id = @issueId", conn, tx);
            existingCmd.Parameters.AddWithValue("issueId", issueId);
            await using var reader = await existingCmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                existing = Map(reader);
            }
        }

        if (existing is not null)
        {
            await tx.CommitAsync();
            return existing;
        }

        await using var rankCmd = new NpgsqlCommand(
            "SELECT COALESCE(MAX(rank), 0) FROM queue;", conn, tx);
        var maxRank = (int)(long)(await rankCmd.ExecuteScalarAsync() ?? 0L);

        await using var insertCmd = new NpgsqlCommand(
            """
            INSERT INTO queue (issue_id, rank)
            VALUES (@issueId, @rank)
            RETURNING id;
            """, conn, tx);
        insertCmd.Parameters.AddWithValue("issueId", issueId);
        insertCmd.Parameters.AddWithValue("rank", maxRank + 1);
        var id = (long)(await insertCmd.ExecuteScalarAsync() ?? 0L);

        await tx.CommitAsync();

        return await GetById(id);
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
        await using var cmd = new NpgsqlCommand($"{SelectQueueSql} WHERE q.id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return Map(reader);
    }

    private const string SelectQueueSql =
        """
        SELECT
            q.id,
            q.issue_id,
            q.rank,
            q.run_id,
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
        ORDER BY q.rank
        """;

    private static QueueListItem Map(NpgsqlDataReader r)
    {
        return new QueueListItem(
            r.GetInt64(r.GetOrdinal("id")),
            r.GetInt64(r.GetOrdinal("issue_id")),
            r.GetInt32(r.GetOrdinal("rank")),
            GetGuidOrNull(r, "run_id"),
            GetStringOrNull(r, "run_status"),
            GetStringOrNull(r, "title"),
            GetStringOrNull(r, "repo"),
            GetInt32OrNull(r, "number"),
            GetStringOrNull(r, "html_url"),
            GetStringOrNull(r, "issue_state"),
            r.GetBoolean(r.GetOrdinal("issue_present")));
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
