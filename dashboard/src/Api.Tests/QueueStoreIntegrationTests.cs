using Api.Queue;
using Xunit;

namespace Api.Tests;

/// <summary>
/// Integration tests that exercise the <em>real</em> <see cref="QueueStore"/> SQL against a
/// real Postgres container (via <see cref="PostgresFixture"/>). They re-run the
/// meaningful behaviours <see cref="QueueTests"/> asserts against <c>FakeQueueStore</c>,
/// but through actual SQL so the SQL-shaped bugs a fake cannot catch (a reader held open
/// across a commit, a WHERE appended after an ORDER BY in the shared select, an aggregate
/// unboxed to the wrong integer type) fail in CI instead of on Render. The shared
/// decisions themselves are pinned once in <see cref="QueueRulesTests"/>.
/// </summary>
public sealed class QueueStoreIntegrationTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly QueueStore _store;

    public QueueStoreIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        // Only meaningful when Docker is available; tests skip otherwise before touching _store.
        _store = fixture.IsDockerAvailable ? fixture.CreateStore() : null!;
    }

    // Per-test: start from a clean slate. Skips do nothing here; the test body re-checks.
    public Task InitializeAsync() =>
        _fixture.IsDockerAvailable ? _fixture.ResetAsync() : Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;

    [SkippableFact]
    public async Task Enqueue_EmptyQueue_ReturnsRankOneThenTwo()
    {
        Skip.IfNot(_fixture.IsDockerAvailable, "Docker is not available; skipping QueueStore integration tests.");

        var first = await _store.Enqueue(101);
        var second = await _store.Enqueue(102);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(1, first!.Rank);
        Assert.Equal(2, second!.Rank);
    }

    [SkippableFact]
    public async Task Enqueue_AlreadyQueuedIssue_IsIdempotent()
    {
        Skip.IfNot(_fixture.IsDockerAvailable, "Docker is not available; skipping QueueStore integration tests.");

        var first = await _store.Enqueue(42);
        var second = await _store.Enqueue(42);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first, second);

        var all = await _store.All();
        Assert.Single(all);
    }

    [SkippableFact]
    public async Task GetById_ReturnsRowForKnownId()
    {
        Skip.IfNot(_fixture.IsDockerAvailable, "Docker is not available; skipping QueueStore integration tests.");

        await _fixture.SeedIssueAsync(7, "owner/repo", 7, "Seed issue", "the body");
        var enqueued = await _store.Enqueue(7);
        Assert.NotNull(enqueued);

        // QueueStore.GetById is private; StartNext (and Restart) route through it, exercising
        // the SelectQueueSql + "WHERE q.id = @id" filtered select and the LEFT JOIN to issues.
        var fetched = await _store.StartNext(enqueued!.Id);

        Assert.NotNull(fetched);
        Assert.Equal(enqueued.Id, fetched!.Id);
        Assert.Equal(7, fetched.IssueId);
        Assert.Equal(1, fetched.Rank);
        Assert.NotNull(fetched.StartRequestedAt);

        // The LEFT JOIN surfaces the seeded issue row.
        Assert.True(fetched.IssuePresent);
        Assert.Equal("Seed issue", fetched.Title);
        Assert.Equal("owner/repo", fetched.Repo);
        Assert.Equal(7, fetched.Number);
    }

    [SkippableFact]
    public async Task All_ReturnsRowsOrderedByRank()
    {
        Skip.IfNot(_fixture.IsDockerAvailable, "Docker is not available; skipping QueueStore integration tests.");

        await _store.Enqueue(3);
        await _store.Enqueue(1);
        await _store.Enqueue(2);

        var all = await _store.All();

        Assert.Equal(3, all.Count);
        Assert.Equal(new[] { 3L, 1L, 2L }, all.Select(i => i.IssueId));
        Assert.Equal(new[] { 1, 2, 3 }, all.Select(i => i.Rank));
    }

    [SkippableFact]
    public async Task ClaimNext_ClaimsHighestRankedStartRequestedItem()
    {
        Skip.IfNot(_fixture.IsDockerAvailable, "Docker is not available; skipping QueueStore integration tests.");

        // ClaimNext resolves the claim payload from the linked issues row (inner-join
        // semantics: no row, no claim), so seed both.
        await _fixture.SeedIssueAsync(1, "owner/repo-a", 1, "First issue", "Do the first thing");
        await _fixture.SeedIssueAsync(2, "owner/repo-b", 2, "Second issue", "Do the second thing");

        var first = await _store.Enqueue(1);   // rank 1 = highest priority
        var second = await _store.Enqueue(2);  // rank 2
        await _store.StartNext(first!.Id);
        await _store.StartNext(second!.Id);

        var claim = await _store.ClaimNext();

        Assert.NotNull(claim);
        Assert.NotEqual(Guid.Empty, claim!.RunId);
        Assert.Equal("https://github.com/owner/repo-a.git", claim.RepoUrl);
        Assert.Equal("Do the first thing", claim.Spec);

        var all = await _store.All();
        var claimed = all.Single(i => i.IssueId == 1);
        Assert.Equal(claim.RunId, claimed.RunId);
    }

    [SkippableFact]
    public async Task ClaimNext_NothingEligible_ReturnsNullWithoutThrowing()
    {
        Skip.IfNot(_fixture.IsDockerAvailable, "Docker is not available; skipping QueueStore integration tests.");

        // Queued but not start-requested, so nothing is eligible to claim.
        await _store.Enqueue(1);

        var claim = await _store.ClaimNext();

        Assert.Null(claim);
    }

    [SkippableFact]
    public async Task ClaimNext_IssueRowMissing_ClaimsNothing()
    {
        Skip.IfNot(_fixture.IsDockerAvailable, "Docker is not available; skipping QueueStore integration tests.");

        // The old single-statement claim inner-joined issues: a queue row whose issue
        // has no issues row matched nothing and claimed nothing. The resolver seam must
        // keep that guarantee — a null payload claims nothing and attaches no run.
        var enqueued = await _store.Enqueue(77);
        await _store.StartNext(enqueued!.Id);

        var claim = await _store.ClaimNext();

        Assert.Null(claim);
        var all = await _store.All();
        Assert.Null(all.Single(i => i.Id == enqueued.Id).RunId);
    }
}
