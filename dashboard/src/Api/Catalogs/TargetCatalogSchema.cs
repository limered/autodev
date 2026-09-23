using Npgsql;

namespace Api.Catalogs;

public static class TargetCatalogSchema
{
    public static async Task EnsureAsync(NpgsqlDataSource dataSource)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            """
            CREATE TABLE IF NOT EXISTS repo_catalogs (
                repo        text        PRIMARY KEY,
                sha         text,
                content     text,
                source      text        NOT NULL,
                etag        text,
                fetched_at  timestamptz NOT NULL
            );
            """, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
