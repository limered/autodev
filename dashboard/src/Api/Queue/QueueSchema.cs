using Npgsql;

namespace Api.Queue;

public static class QueueSchema
{
    public static async Task EnsureAsync(NpgsqlDataSource dataSource)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            """
            CREATE TABLE IF NOT EXISTS queue (
                id                  bigserial   PRIMARY KEY,
                issue_id            bigint      NOT NULL UNIQUE,
                rank                int         NOT NULL,
                run_id              uuid,
                start_requested_at  timestamptz,
                enqueued_at         timestamptz NOT NULL DEFAULT now()
            );
            CREATE INDEX IF NOT EXISTS queue_rank_idx ON queue (rank);
            """, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
