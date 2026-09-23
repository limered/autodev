using Api.Catalogs;
using Api.Host;
using Api.Issues;
using Api.Queue;
using Api.Runs;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Api.Tests;

/// <summary>
/// Spins up a throwaway Postgres container, applies every schema the stores read
/// (queue, issues, runs, host), and constructs real stores over an
/// <see cref="NpgsqlDataSource"/>. Shared across an integration test class via
/// <see cref="IClassFixture{TFixture}"/> so the container starts once per class.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer? _container = TryBuildContainer();

    private NpgsqlDataSource _dataSource = null!;
    private HostStore _hostStore = null!;

    // Building the container validates Docker config eagerly and throws when Docker is
    // absent; swallow that so tests skip cleanly instead of failing at fixture construction.
    private static PostgreSqlContainer? TryBuildContainer()
    {
        try { return new PostgreSqlBuilder().Build(); }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// <see langword="true"/> when the Postgres container started successfully and the
    /// schema is in place. When Docker is unavailable this stays <see langword="false"/>
    /// and tests skip cleanly instead of failing.
    /// </summary>
    public bool IsDockerAvailable { get; private set; }

    /// <summary>
    /// Constructs a real <see cref="QueueStore"/> over the container's data source, with
    /// the SQL <see cref="IssueResolver"/> answering its claim payload. Only valid to
    /// call when <see cref="IsDockerAvailable"/> is <see langword="true"/>.
    /// </summary>
    public QueueStore CreateStore() => new(_dataSource, _hostStore, new IssueResolver());

    /// <summary>
    /// Constructs a real <see cref="RunStore"/> (queue release + linked-issue resolve
    /// composed in Apply via the <see cref="IssueResolver"/> seam) and a
    /// <see cref="RecordingGitHubIssuesClient"/> so tests can assert the finish path
    /// resolved and closed the linked issue. Only valid when
    /// <see cref="IsDockerAvailable"/>.
    /// </summary>
    public (RunStore Store, RecordingGitHubIssuesClient GitHub) CreateRunStore()
    {
        var gitHub = new RecordingGitHubIssuesClient();
        var store = new RunStore(
            _dataSource,
            NullLogger<RunStore>.Instance,
            gitHub,
            _hostStore,
            new IssueResolver());
        return (store, gitHub);
    }

    /// <summary>Seeds a queue row linking an issue to a run, for the run-finished path.</summary>
    public async Task SeedQueueRowAsync(long issueId, Guid runId)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO queue (issue_id, rank, run_id, start_requested_at) VALUES (@issueId, 1, @runId, now());",
            conn);
        cmd.Parameters.AddWithValue("issueId", issueId);
        cmd.Parameters.AddWithValue("runId", runId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Counts queue rows for a run, so tests can assert the finish path deleted it.</summary>
    public async Task<int> CountQueueRowsAsync(Guid runId)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM queue WHERE run_id = @runId;", conn);
        cmd.Parameters.AddWithValue("runId", runId);
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>Reads a run's persisted columns for assertions the fold alone can't cover.</summary>
    public async Task<RunState?> GetRunAsync(Guid runId)
    {
        var (store, _) = CreateRunStore();
        return await store.GetRun(runId);
    }

    public async Task InitializeAsync()
    {
        // Starting the container is the only Docker-dependent step; a failure here means
        // Docker is not available, so tests should skip rather than fail. Everything after
        // this point (schema setup, store construction) runs against a real DB and any
        // error there is a genuine failure we want surfaced.
        if (_container is null)
        {
            IsDockerAvailable = false;
            return;
        }

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
        await TargetCatalogSchema.EnsureAsync(_dataSource);

        _hostStore = new HostStore(_dataSource);
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
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
            "TRUNCATE TABLE queue, issues, runs, repo_catalogs RESTART IDENTITY CASCADE;", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Seeds an issues row directly via SQL. QueueStore LEFT JOINs issues on
    /// github_id = issue_id, and the claim resolver inner-joins it, so a matching row
    /// must exist for the JOINs to surface real data.
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
