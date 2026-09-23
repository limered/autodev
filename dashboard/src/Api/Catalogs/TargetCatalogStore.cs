using Npgsql;

namespace Api.Catalogs;

public sealed class TargetCatalogStore(NpgsqlDataSource dataSource) : ITargetCatalogStore
{
    public async Task<TargetCatalog?> GetAsync(string repo, CancellationToken cancellationToken = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(
            "SELECT repo, sha, content, source, etag, fetched_at FROM repo_catalogs WHERE repo = @repo;",
            conn);
        cmd.Parameters.AddWithValue("repo", repo);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new TargetCatalog(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetFieldValue<DateTimeOffset>(5));
    }

    public async Task PutAsync(TargetCatalog catalog, CancellationToken cancellationToken = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO repo_catalogs (repo, sha, content, source, etag, fetched_at)
            VALUES (@repo, @sha, @content, @source, @etag, @fetchedAt)
            ON CONFLICT (repo) DO UPDATE SET
                sha = EXCLUDED.sha,
                content = EXCLUDED.content,
                source = EXCLUDED.source,
                etag = EXCLUDED.etag,
                fetched_at = EXCLUDED.fetched_at;
            """, conn);
        cmd.Parameters.AddWithValue("repo", catalog.Repo);
        cmd.Parameters.AddWithValue("sha", (object?)catalog.Sha ?? DBNull.Value);
        cmd.Parameters.AddWithValue("content", (object?)catalog.Content ?? DBNull.Value);
        cmd.Parameters.AddWithValue("source", catalog.Source);
        cmd.Parameters.AddWithValue("etag", (object?)catalog.Etag ?? DBNull.Value);
        cmd.Parameters.AddWithValue("fetchedAt", catalog.FetchedAt);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
