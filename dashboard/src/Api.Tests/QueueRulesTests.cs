using Api.Queue;
using Xunit;

namespace Api.Tests;

/// <summary>
/// Pins the three queue decisions the real SQL <see cref="QueueStore"/> and the
/// in-memory <c>FakeQueueStore</c> share via <see cref="QueueRules"/>: next-rank
/// assignment, claim eligibility + ordering, and enqueue dedup. A rule change
/// happens here once and both stores follow.
/// </summary>
public class QueueRulesTests
{
    private static QueueRuleItem Item(
        long id,
        long issueId,
        int rank,
        Guid? runId = null,
        DateTimeOffset? startRequestedAt = null) => new(
        id, issueId, rank, runId, startRequestedAt);

    [Fact]
    public void NextRank_EmptyQueue_ReturnsOne()
    {
        Assert.Equal(1, QueueRules.NextRank(Array.Empty<int>()));
    }

    [Fact]
    public void NextRank_AppendsOnePastMax_EvenWithRankHoles()
    {
        // Deletes leave rank holes; the next rank is max+1, not count+1.
        Assert.Equal(6, QueueRules.NextRank(new[] { 5, 2 }));
    }

    [Fact]
    public void IsClaimable_RequiresStartRequestedAndUnclaimed()
    {
        var at = DateTimeOffset.UtcNow;

        Assert.False(QueueRules.IsClaimable(Item(1, 1, 1, startRequestedAt: null)));
        Assert.False(QueueRules.IsClaimable(Item(2, 2, 2, runId: Guid.NewGuid(), startRequestedAt: at)));
        Assert.True(QueueRules.IsClaimable(Item(3, 3, 3, startRequestedAt: at)));
    }

    [Fact]
    public void NextClaimable_PicksLowestRankedClaimableRow()
    {
        var at = DateTimeOffset.UtcNow;
        var items = new[]
        {
            Item(1, 1, 3, startRequestedAt: at),                        // claimable, rank 3
            Item(2, 2, 1),                                              // never start-requested
            Item(3, 3, 2, runId: Guid.NewGuid(), startRequestedAt: at), // already claimed
            Item(4, 4, 5, startRequestedAt: at),                        // claimable, rank 5
        };

        var next = QueueRules.NextClaimable(items);

        Assert.NotNull(next);
        Assert.Equal(1, next!.Id);
    }

    [Fact]
    public void NextClaimable_NothingClaimable_ReturnsNull()
    {
        Assert.Null(QueueRules.NextClaimable(new[] { Item(1, 1, 1) }));
    }

    [Fact]
    public void ClaimableInRankOrder_ExcludesUnclaimableAndOrdersByRank()
    {
        var at = DateTimeOffset.UtcNow;
        var items = new[]
        {
            Item(1, 1, 5, startRequestedAt: at),
            Item(2, 2, 2),
            Item(3, 3, 1, runId: Guid.NewGuid(), startRequestedAt: at),
            Item(4, 4, 3, startRequestedAt: at),
        };

        var claimable = QueueRules.ClaimableInRankOrder(items).ToList();

        Assert.Equal(new[] { 4L, 1L }, claimable.Select(i => i.Id));
    }

    [Fact]
    public void ExistingForIssue_ReturnsTheRowForThatIssue_OrNull()
    {
        var items = new[] { Item(1, 10, 1), Item(2, 20, 2) };

        Assert.Equal(2, QueueRules.ExistingForIssue(items, 20)!.Id);
        Assert.Null(QueueRules.ExistingForIssue(items, 99));
    }
}
