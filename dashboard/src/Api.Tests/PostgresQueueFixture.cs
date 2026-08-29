using Api.Host;
using Api.Issues;
using Api.Queue;
using Api.Runs;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Api.Tests;

/// <summary>
/// Spins up a throwaway Postgres container, applies the queue schema and the
/// sibling schemas <see cref="QueueStore"/> LEFT JOINs against (issues, runs),
/// and constructs a real <see cref="QueueStore"/> over an <see cref="NpgsqlDataSource"/>.
/// Shared across every test in <see cref="QueueStoreIntegrationTests"/> via
/// <see cref="IClassFixture{TFixture}"/> so the container starts once per class.
/// </summary>
public sealed class PostgresQueueFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder().Build();

    private NpgsqlDataSource _dataSource = null!;
    private HostStore _hostStore = null!;

    /// <summary>
    /// <see langword="true"/> when the Postgres container started successfully and the
    /// schema is in place. When Docker is unavailable this stays <see langword="false"/>
    /// and tests skip cleanly instead of failing.
    /// </summary>
    public bool IsDockerAvailable { get; private set; }

    /// <summary>
    /// Constructs a real <see cref="QueueStore"/> over the container's data source.
    /// Only valid to call when <see cref="IsDockerAvailable"/> is <see langword="true"/>.
    /// </summary>
    public QueueStore CreateStore() => new(_dataSource, _hostStore);

    public async Task InitializeAsync()
    {
        // Starting the container is the only Docker-dependent step; a failure here means
        // Docker is not available, so tests should skip rather than fail. Everything after
        // this point (schema setup, store construction) runs against a real DB and any
        // error there is a genuine failure we want surfaced.
        try
        {
            await _container.StartAsync();
        }
        catch (Exception)
        {
            IsDockerAvailable = false;
            return;
        }

        IsDockerAvailable = true;

        // Wire the connection string in the same way Program.cs does.
        _dataSource = NpgsqlDataSource.Create(_container.GetConnectionString());

        await RunsSchema.EnsureAsync(_dataSource);
        await IssuesSchema.EnsureAsync(_dataSource);
        await QueueSchema.EnsureAsync(_dataSource);
        await HostSchema.EnsureAsync(_dataSource);

        _hostStore = new HostStore(_dataSource);
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }

        await _container.DisposeAsync();
    }

    /// <summary>
    /// Wipes the queue/issues/runs tables between tests so each test starts from a
    /// clean slate. RESTART IDENTITY resets the queue.id bigserial sequence so ids are
    /// deterministic per test.
    /// </summary>
    public async Task ResetAsync()
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "TRUNCATE TABLE queue, issues, runs RESTART IDENTITY CASCADE;", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Seeds an issues row directly via SQL. QueueStore LEFT JOINs issues on
    /// github_id = issue_id, and ClaimNext inner-joins it, so a matching row must exist
    /// for the JOINs to surface real data.
    /// </summary>
    public async Task SeedIssueAsync(
        long githubId,
        string repo,
        int number,
        string title,
        string? body = null,
        string state = "open")
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO issues (github_id, repo, number, title, html_url, labels, body, state, updated_at)
            VALUES (@githubId, @repo, @number, @title, @htmlUrl, @labels, @body, @state, @updatedAt)
            ON CONFLICT (github_id) DO NOTHING;
            """, conn);
        cmd.Parameters.AddWithValue("githubId", githubId);
        cmd.Parameters.AddWithValue("repo", repo);
        cmd.Parameters.AddWithValue("number", number);
        cmd.Parameters.AddWithValue("title", title);
        cmd.Parameters.AddWithValue("htmlUrl", $"https://github.com/{repo}/issues/{number}");
        cmd.Parameters.AddWithValue("labels", new[] { "ready-for-agent" });
        cmd.Parameters.AddWithValue("body", (object?)body ?? DBNull.Value);
        cmd.Parameters.AddWithValue("state", state);
        cmd.Parameters.AddWithValue("updatedAt", DateTimeOffset.UtcNow);
        await cmd.ExecuteNonQueryAsync();
    }
}
