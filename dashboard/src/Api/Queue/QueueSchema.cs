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
                enqueued_at         timestamptz NOT NULL DEFAULT now(),
                resume_branch       text,
                resume_stage        text,
                parent_run_id       uuid
            );
            CREATE INDEX IF NOT EXISTS queue_rank_idx ON queue (rank);
            -- Idempotent migration: same-branch resume carry across clear/re-claim.
            ALTER TABLE queue ADD COLUMN IF NOT EXISTS resume_branch text;
            ALTER TABLE queue ADD COLUMN IF NOT EXISTS resume_stage text;
            ALTER TABLE queue ADD COLUMN IF NOT EXISTS parent_run_id uuid;
            -- the row's workflow pick, frozen at claim time.
            ALTER TABLE queue ADD COLUMN IF NOT EXISTS workflow text;
            """, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
