using Api.Catalogs;
using Api.Issues;
using Api.Queue;
using Xunit;

namespace Api.Tests;

/// <summary>
/// The claim-time workflow freeze through the fake queue store's seam: the pick
/// resolves into the claim payload as an explicit workflow string; a pick the
/// catalog cannot verify skips the row (left unclaimed, no run) and is retryable
/// after the catalog resyncs — never a silent fallback to the default. The
/// decision is shared with the SQL store by QueueRules + CatalogRules, so these
/// pin both stores' claim behaviour modulo the fake's single-row claim.
/// </summary>
public class QueueClaimPickTests
{
    private const string CatalogFullOnly =
        """{"stages":{"implementation":{"type":"sequential","agents":["agent"]},"pr-author":{"type":"sequential","agents":["pr-author"]}},"workflows":{"full":["implementation","pr-author"]},"defaultWorkflow":"full"}""";

    private const string CatalogWithQuick =
        """{"stages":{"implementation":{"type":"sequential","agents":["agent"]},"pr-author":{"type":"sequential","agents":["pr-author"]}},"workflows":{"full":["implementation","pr-author"],"quick":["implementation"]},"defaultWorkflow":"full"}""";

    private const string CatalogWithRetired =
        """{"stages":{"implementation":{"type":"sequential","agents":["agent"]},"pr-author":{"type":"sequential","agents":["pr-author"]}},"workflows":{"full":["implementation","pr-author"],"quick":["implementation"],"retired-flow":["pr-author"]},"defaultWorkflow":"full"}""";

    private static async Task<FakeQueueStore> ClaimableQueue(string repo = "owner/repo")
    {
        var issues = new FakeIssuesStore();
        await issues.SyncRepo(repo, new[]
        {
            new IssueSnapshot(
                1, 7, "Issue", "https://github.com/owner/repo/issues/7",
                new[] { "ready-for-agent" }, "Do it", "open", DateTimeOffset.UtcNow),
        });
        var queue = new FakeQueueStore { Issues = issues };
        queue.Resolver = new FakeIssueResolver(queue, issues);
        return queue;
    }

    // Seeds the catalog cache with one fetch, then hands back a store wired to
    // read the same cache with no further fetches (NotModified lands on the
    // cached row every time), so tests control catalog drift by re-seeding.
    private static async Task<ITargetCatalogService> CachedCatalog(
        FakeTargetCatalogStore store, string sha, string content)
    {
        await new TargetCatalogService(FakeTargetCatalogFetcher.Target(sha, content), store)
            .RefreshOnSyncAsync("owner/repo");
        return new TargetCatalogService(FakeTargetCatalogFetcher.NotModified(), store);
    }

    [Fact]
    public async Task ClaimNext_NoPick_ClaimsExplicitDefaultWorkflow()
    {
        var queue = await ClaimableQueue();
        queue.Catalogs = await CachedCatalog(new FakeTargetCatalogStore(), "sha-1", CatalogFullOnly);
        queue.Factory = new FixedFactoryWorkflows(null);

        var enqueued = await queue.Enqueue(1);
        await queue.StartNext(enqueued!.Id);

        var claim = await queue.ClaimNext();

        Assert.NotNull(claim);
        Assert.Equal("full", claim!.Workflow);
    }

    [Fact]
    public async Task ClaimNext_VerifiedPick_FreezesPickIntoPayload()
    {
        var queue = await ClaimableQueue();
        queue.Catalogs = await CachedCatalog(new FakeTargetCatalogStore(), "sha-1", CatalogWithQuick);
        queue.Factory = new FixedFactoryWorkflows(null);

        var enqueued = await queue.Enqueue(1);
        await queue.SetWorkflow(enqueued!.Id, "quick");
        await queue.StartNext(enqueued.Id);

        var claim = await queue.ClaimNext();

        Assert.NotNull(claim);
        Assert.Equal("quick", claim!.Workflow);
        Assert.Equal(claim.RunId, (await queue.All()).Single().RunId);
    }

    [Fact]
    public async Task ClaimNext_StalePick_SkipsRowLeftUnclaimedAndRetriesAfterResync()
    {
        var queue = await ClaimableQueue();
        var store = new FakeTargetCatalogStore();
        queue.Catalogs = await CachedCatalog(store, "sha-1", CatalogFullOnly);
        queue.Factory = new FixedFactoryWorkflows(null);

        var enqueued = await queue.Enqueue(1);
        await queue.SetWorkflow(enqueued!.Id, "retired-flow");
        await queue.StartNext(enqueued.Id);

        var claim = await queue.ClaimNext();

        // Skipped: the row stays unclaimed (no run created) but still start-requested,
        // so the runner retries it after the next Issue sync refreshes the catalog.
        Assert.Null(claim);
        var row = (await queue.All()).Single();
        Assert.Null(row.RunId);
        Assert.NotNull(row.StartRequestedAt);
        Assert.Equal("retired-flow", row.Workflow);

        // Resync moves the cache to a catalog that names the pick again.
        await new TargetCatalogService(FakeTargetCatalogFetcher.Target("sha-2", CatalogWithRetired), store)
            .RefreshOnSyncAsync("owner/repo");

        var retried = await queue.ClaimNext();

        Assert.NotNull(retried);
        Assert.Equal("retired-flow", retried!.Workflow);
    }

    [Fact]
    public async Task ClaimNext_UnverifiablePick_SkipsRowInsteadOfFallingBack()
    {
        var queue = await ClaimableQueue();
        queue.Catalogs = await CachedCatalog(new FakeTargetCatalogStore(), "sha-1", "{not json");
        queue.Factory = new FixedFactoryWorkflows(null);

        var enqueued = await queue.Enqueue(1);
        await queue.SetWorkflow(enqueued!.Id, "quick");
        await queue.StartNext(enqueued.Id);

        var claim = await queue.ClaimNext();

        Assert.Null(claim);
        Assert.Null((await queue.All()).Single().RunId);
    }

    [Fact]
    public async Task ClaimNext_FallbackCatalog_VerifiesPickAgainstFactoryNames()
    {
        var queue = await ClaimableQueue();
        // Fetcher says absent every time, so the effective catalog stays the factory
        // fallback (no content) and the factory catalog names are the verifier.
        queue.Catalogs = new TargetCatalogService(FakeTargetCatalogFetcher.Missing(), new FakeTargetCatalogStore());
        queue.Factory = new FixedFactoryWorkflows(new FactoryWorkflows(
            "full", [new WorkflowEntry("full", 2), new WorkflowEntry("quick", 1)]));

        var enqueued = await queue.Enqueue(1);
        await queue.SetWorkflow(enqueued!.Id, "full");
        await queue.StartNext(enqueued.Id);

        var claim = await queue.ClaimNext();

        Assert.NotNull(claim);
        Assert.Equal("full", claim!.Workflow);

        // The factory catalog does not carry the pick, so the claim skips instead of
        // silently running the factory default.
        await queue.SetWorkflow(enqueued.Id, "quick");
        var stale = await queue.ClaimNext();

        Assert.Null(stale);
    }

    [Fact]
    public async Task SetWorkflow_ClaimedRow_KeepsFrozenPick()
    {
        var queue = await ClaimableQueue();
        queue.Catalogs = new TargetCatalogService(FakeTargetCatalogFetcher.Missing(), new FakeTargetCatalogStore());
        queue.Factory = new FixedFactoryWorkflows(new FactoryWorkflows(
            "full", [new WorkflowEntry("full", 2), new WorkflowEntry("quick", 1)]));

        var enqueued = await queue.Enqueue(1);
        await queue.SetWorkflow(enqueued!.Id, "quick");
        await queue.StartNext(enqueued.Id);
        var claim = await queue.ClaimNext();
        Assert.NotNull(claim);

        var bumped = await queue.SetWorkflow(enqueued.Id, "full");

        Assert.Equal("quick", bumped!.Workflow);
    }

    private sealed class FixedFactoryWorkflows(FactoryWorkflows? workflows) : IFactoryWorkflows
    {
        public FactoryWorkflows? TryRead() => workflows;
    }
}
