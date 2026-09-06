using Api.Issues;
using Api.Queue;
using Xunit;

namespace Api.Tests;

public class QueueTests
{
    [Fact]
    public async Task Enqueue_AppendsAtEndWithIncrementingRank()
    {
        var store = new FakeQueueStore();

        var first = await store.Enqueue(1);
        var second = await store.Enqueue(2);
        var third = await store.Enqueue(3);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotNull(third);
        Assert.Equal(1, first.Rank);
        Assert.Equal(2, second.Rank);
        Assert.Equal(3, third.Rank);
    }

    [Fact]
    public async Task Enqueue_SameIssueTwice_IsIdempotent()
    {
        var store = new FakeQueueStore();

        var first = await store.Enqueue(42);
        var second = await store.Enqueue(42);

        Assert.NotNull(first);
        Assert.Equal(first, second);

        var all = await store.All();
        Assert.Single(all);
    }

    [Fact]
    public async Task All_ReturnsItemsOrderedByRank()
    {
        var store = new FakeQueueStore();
        await store.Enqueue(3);
        await store.Enqueue(1);
        await store.Enqueue(2);

        var all = await store.All();

        Assert.Equal(3, all.Count);
        Assert.Equal(new[] { 3L, 1L, 2L }, all.Select(i => i.IssueId));
    }

    [Fact]
    public async Task Reorder_RewritesRanks()
    {
        var store = new FakeQueueStore();
        var first = await store.Enqueue(1);
        var second = await store.Enqueue(2);
        var third = await store.Enqueue(3);

        await store.Reorder(new[] { third!.Id, first!.Id, second!.Id });

        var all = await store.All();
        Assert.Equal(new[] { 3L, 1L, 2L }, all.Select(i => i.IssueId));
        Assert.Equal(1, all[0].Rank);
        Assert.Equal(2, all[1].Rank);
        Assert.Equal(3, all[2].Rank);
    }

    [Fact]
    public async Task Delete_RemovesItem()
    {
        var store = new FakeQueueStore();
        var item = await store.Enqueue(42);

        var deleted = await store.Delete(item!.Id);

        Assert.True(deleted);
        Assert.Empty(await store.All());
    }

    [Fact]
    public async Task Delete_MissingItem_ReturnsFalse()
    {
        var store = new FakeQueueStore();

        var deleted = await store.Delete(999);

        Assert.False(deleted);
    }

    [Fact]
    public async Task StartNext_SetsStartRequestedAt()
    {
        var store = new FakeQueueStore();
        var item = await store.Enqueue(42);

        var updated = await store.StartNext(item!.Id);

        Assert.NotNull(updated);
        Assert.NotNull(updated.StartRequestedAt);
    }

    [Fact]
    public async Task StartNext_MissingItem_ReturnsNull()
    {
        var store = new FakeQueueStore();

        var updated = await store.StartNext(999);

        Assert.Null(updated);
    }

    [Fact]
    public async Task StartNext_Twice_KeepsFirstTimestamp()
    {
        // The shared rule: start is requested only when none was requested before.
        Assert.True(QueueRules.ShouldRequestStart(new QueueRuleItem(1, 1, 1, null, null)));
        Assert.False(QueueRules.ShouldRequestStart(new QueueRuleItem(1, 1, 1, null, DateTimeOffset.UtcNow)));

        var store = new FakeQueueStore();
        var item = await store.Enqueue(42);
        var first = await store.StartNext(item!.Id);
        var second = await store.StartNext(item.Id);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.StartRequestedAt, second!.StartRequestedAt);
    }

    [Fact]
    public async Task ClaimNext_ClaimsHighestRankedStartRequestedItem()
    {
        var store = new FakeQueueStore();
        var first = await store.Enqueue(1);
        var second = await store.Enqueue(2);
        await store.StartNext(first!.Id);
        await store.StartNext(second!.Id);

        var claim = await store.ClaimNext();

        Assert.NotNull(claim);
        var all = await store.All();
        Assert.Equal(claim.RunId, all[0].RunId);
        Assert.Equal(1, all[0].IssueId);
    }

    [Fact]
    public async Task ClaimNext_NoStartRequestedItems_ReturnsNull()
    {
        var store = new FakeQueueStore();
        await store.Enqueue(1);

        var claim = await store.ClaimNext();

        Assert.Null(claim);
    }

    [Fact]
    public async Task ClaimNext_IssueRowMissing_ClaimsNothing()
    {
        // Same guarantee as the SQL store: no issues row means the resolver answers
        // null, so the claim attaches no run — pinned here without Postgres.
        var queue = new FakeQueueStore();
        queue.Resolver = new FakeIssueResolver(queue, new FakeIssuesStore());
        var enqueued = await queue.Enqueue(77);
        await queue.StartNext(enqueued!.Id);

        var claim = await queue.ClaimNext();

        Assert.Null(claim);
        Assert.Null((await queue.All()).Single(i => i.Id == enqueued.Id).RunId);
    }

    [Fact]
    public async Task ClaimNext_IssueRowPresent_ClaimsWithResolverPayload()
    {
        var queue = new FakeQueueStore();
        var issues = new FakeIssuesStore();
        await issues.SyncRepo("owner/repo", new[]
        {
            new IssueSnapshot(
                1, 7, "First issue", "https://github.com/owner/repo/issues/7",
                new[] { "ready-for-agent" }, "Do the first thing", "open", DateTimeOffset.UtcNow),
        });
        queue.Resolver = new FakeIssueResolver(queue, issues);
        var enqueued = await queue.Enqueue(1);
        await queue.StartNext(enqueued!.Id);

        var claim = await queue.ClaimNext();

        Assert.NotNull(claim);
        Assert.Equal("https://github.com/owner/repo.git", claim!.RepoUrl);
        Assert.Equal("Do the first thing", claim.Spec);
        Assert.Equal(claim.RunId, (await queue.All()).Single().RunId);
    }

    [Fact]
    public async Task Restart_ClearsRunIdAndStartRequestedAt()
    {
        var store = new FakeQueueStore();
        var item = await store.Enqueue(42);
        await store.StartNext(item!.Id);
        await store.ClaimNext();

        var restarted = await store.Restart(item.Id);

        Assert.NotNull(restarted);
        Assert.Null(restarted.RunId);
        Assert.Null(restarted.StartRequestedAt);
    }

    [Fact]
    public async Task Restart_MissingItem_ReturnsNull()
    {
        var store = new FakeQueueStore();

        var restarted = await store.Restart(999);

        Assert.Null(restarted);
    }

    [Fact]
    public async Task Restart_AfterRestart_ItemCanBeClaimedAgain()
    {
        var store = new FakeQueueStore();
        var item = await store.Enqueue(42);
        await store.StartNext(item!.Id);
        await store.ClaimNext();

        await store.Restart(item.Id);
        await store.StartNext(item.Id);
        var claim = await store.ClaimNext();

        Assert.NotNull(claim);
        var all = await store.All();
        Assert.Equal(claim.RunId, all[0].RunId);
    }
}
