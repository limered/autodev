using System.Reflection;
using Npgsql;

namespace Api.Issues;

public interface IIssuesStore
{
    Task<IReadOnlyList<Issue>> All();
    Task SyncRepo(string repo, IReadOnlyList<IssueSnapshot> snapshot);
}

public sealed class IssuesStore : IIssuesStore
{
    private readonly NpgsqlDataSource _dataSource;

    private const string IssueColumns =
        "github_id, repo, number, title, html_url, labels, body, state, updated_at";

    private static readonly string[] Columns = IssueColumns.Split(',').Select(c => c.Trim()).ToArray();
    private static readonly string SelectSql = $"SELECT {IssueColumns} FROM issues";

    static IssuesStore()
    {
        var issueFieldCount = typeof(Issue)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Length;

        if (Columns.Length != issueFieldCount)
        {
            throw new InvalidOperationException(
                $"IssuesStore mapping mismatch: {Columns.Length} SQL columns but Issue has {issueFieldCount} fields.");
        }
    }

    public IssuesStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IReadOnlyList<Issue>> All()
    {
        return await Query($"{SelectSql} ORDER BY updated_at DESC");
    }

    public async Task SyncRepo(string repo, IReadOnlyList<IssueSnapshot> snapshot)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var ids = snapshot.Select(s => s.Id).ToArray();

        if (ids.Length == 0)
        {
            await using var deleteCmd = new NpgsqlCommand(
                "DELETE FROM issues WHERE repo = @repo", conn, tx);
            deleteCmd.Parameters.AddWithValue("repo", repo);
            await deleteCmd.ExecuteNonQueryAsync();
        }
        else
        {
            await using var deleteCmd = new NpgsqlCommand(
                "DELETE FROM issues WHERE repo = @repo AND github_id <> ALL(@ids)", conn, tx);
            deleteCmd.Parameters.AddWithValue("repo", repo);
            deleteCmd.Parameters.AddWithValue("ids", ids);
            await deleteCmd.ExecuteNonQueryAsync();
        }

        foreach (var issue in snapshot)
        {
            await using var upsertCmd = new NpgsqlCommand(
                """
                INSERT INTO issues (github_id, repo, number, title, html_url, labels, body, state, updated_at)
                VALUES (@githubId, @repo, @number, @title, @htmlUrl, @labels, @body, @state, @updatedAt)
                ON CONFLICT (github_id) DO UPDATE SET
                    repo = EXCLUDED.repo,
                    number = EXCLUDED.number,
                    title = EXCLUDED.title,
                    html_url = EXCLUDED.html_url,
                    labels = EXCLUDED.labels,
                    body = EXCLUDED.body,
                    state = EXCLUDED.state,
                    updated_at = EXCLUDED.updated_at;
                """, conn, tx);
            upsertCmd.Parameters.AddWithValue("githubId", issue.Id);
            upsertCmd.Parameters.AddWithValue("repo", repo);
            upsertCmd.Parameters.AddWithValue("number", issue.Number);
            upsertCmd.Parameters.AddWithValue("title", issue.Title);
            upsertCmd.Parameters.AddWithValue("htmlUrl", issue.HtmlUrl);
            upsertCmd.Parameters.AddWithValue("labels", issue.Labels);
            upsertCmd.Parameters.AddWithValue("body", (object?)issue.Body ?? DBNull.Value);
            upsertCmd.Parameters.AddWithValue("state", issue.State);
            upsertCmd.Parameters.AddWithValue("updatedAt", issue.UpdatedAt);
            await upsertCmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    private async Task<IReadOnlyList<Issue>> Query(string sql)
    {
        var issues = new List<Issue>();
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync();

        while (await r.ReadAsync())
        {
            issues.Add(MapIssue(r));
        }

        return issues;
    }

    private static Issue MapIssue(NpgsqlDataReader r)
    {
        if (r.FieldCount != Columns.Length)
        {
            throw new InvalidOperationException(
                $"IssuesStore mapping mismatch: expected {Columns.Length} columns but reader returned {r.FieldCount}.");
        }

        return new Issue(
            r.GetInt64(r.GetOrdinal("github_id")),
            r.GetString(r.GetOrdinal("repo")),
            r.GetInt32(r.GetOrdinal("number")),
            r.GetString(r.GetOrdinal("title")),
            r.GetString(r.GetOrdinal("html_url")),
            r.GetFieldValue<string[]>(r.GetOrdinal("labels")),
            GetStringOrNull(r, "body"),
            r.GetString(r.GetOrdinal("state")),
            r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("updated_at")));
    }

    private static string? GetStringOrNull(NpgsqlDataReader r, string column)
    {
        var ordinal = r.GetOrdinal(column);
        return r.IsDBNull(ordinal) ? null : r.GetString(ordinal);
    }
}
