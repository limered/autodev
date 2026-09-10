using Npgsql;

namespace Api.Runs;

public static class RunsSchema
{
    public static async Task EnsureAsync(NpgsqlDataSource dataSource)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            """
            CREATE TABLE IF NOT EXISTS runs (
                run_id            uuid        PRIMARY KEY,
                repo              text        NOT NULL,
                branch            text        NOT NULL,
                spec              text        NOT NULL,
                model             text        NOT NULL,
                vm_name           text,
                status            text        NOT NULL,
                started_at        timestamptz NOT NULL,
                finished_at       timestamptz,
                last_heartbeat_at timestamptz,
                pr_url            text,
                failure_reason    text,
                freeze_captured   boolean     NOT NULL DEFAULT false,
                freeze_local_path text,
                updated_at        timestamptz NOT NULL DEFAULT now(),
                stages            jsonb,
                current_phase     text,
                steps             jsonb,
                current_category  text
            );
            CREATE INDEX IF NOT EXISTS runs_status_started_idx ON runs (status, started_at DESC);
            -- Idempotent migration: add the stages JSON column to pre-existing runs tables.
            ALTER TABLE runs ADD COLUMN IF NOT EXISTS stages jsonb;
            -- Idempotent migration: add the current_phase column to pre-existing runs tables.
            ALTER TABLE runs ADD COLUMN IF NOT EXISTS current_phase text;
            -- Idempotent migration: add the current_category column to pre-existing runs tables.
            ALTER TABLE runs ADD COLUMN IF NOT EXISTS current_category text;
            -- Idempotent migration: add the steps JSON column to pre-existing runs tables.
            ALTER TABLE runs ADD COLUMN IF NOT EXISTS steps jsonb;
            """, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
