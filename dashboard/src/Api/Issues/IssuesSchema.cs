using Npgsql;

namespace Api.Issues;

public static class IssuesSchema
{
    public static async Task EnsureAsync(NpgsqlDataSource dataSource)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            """
            CREATE TABLE IF NOT EXISTS issues (
                github_id  bigint       PRIMARY KEY,
                repo       text         NOT NULL,
                number     int          NOT NULL,
                title      text         NOT NULL,
                html_url   text         NOT NULL,
                labels     text[]       NOT NULL DEFAULT '{}',
                body       text,
                state      text         NOT NULL,
                updated_at timestamptz  NOT NULL
            );
            CREATE INDEX IF NOT EXISTS issues_repo_updated_idx ON issues (repo, updated_at DESC);
            """, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
