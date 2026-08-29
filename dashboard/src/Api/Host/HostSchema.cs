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
            """, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
