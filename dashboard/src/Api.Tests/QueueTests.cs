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
}
