using Npgsql;

namespace Api.Host;

public static class HostSchema
{
    public static async Task EnsureAsync(NpgsqlDataSource dataSource)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            """
            CREATE TABLE IF NOT EXISTS host (
                id         smallint      PRIMARY KEY,
                last_seen  timestamptz NOT NULL
            );
            -- Migrate any pre-existing last_seen column stored as timestamp to timestamptz.
            -- The USING clause interprets the stored value as UTC, matching the previous
            -- (DateTime, TimeSpan.Zero) read workaround, so host online/offline stays the same.
            ALTER TABLE host ALTER COLUMN last_seen TYPE timestamptz USING last_seen AT TIME ZONE 'UTC';
            """, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
