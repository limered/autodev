namespace Api.Queue;

/// <summary>
/// The queue's four business decisions, stated once so the SQL <see cref="QueueStore"/>
/// and the in-memory <c>FakeQueueStore</c> derive them identically and cannot drift:
/// next-rank assignment, start-request idempotency, claim eligibility + ordering, and
/// enqueue dedup. The stores own storage and execution; this class owns only the
/// derivation. Decisions run on <see cref="QueueRuleItem"/> — the queue row's own
/// fields — so the API response shape stays outside this module.
/// </summary>
public static class QueueRules
{
    /// <summary>
    /// Rank for a new queue row: one past the current max (1 for an empty queue), so
    /// enqueue appends to the back of the queue.
    /// </summary>
    public static int NextRank(IEnumerable<int> ranks) => ranks.DefaultIfEmpty(0).Max() + 1;

    /// <summary>
    /// Start is requested once: only when none was requested before. The SQL store
    /// pushes this to its UPDATE's <c>WHERE start_requested_at IS NULL</c>; the fake
    /// guards on it — a repeat call keeps the first timestamp.
    /// </summary>
    public static bool ShouldRequestStart(QueueRuleItem item) => item.StartRequestedAt is null;

    /// <summary>
    /// Claimable = start requested but not yet attached to a run. <see cref="QueueStore"/>
    /// re-applies this under the row lock when claiming; the fake applies it directly.
    /// </summary>
    public static bool IsClaimable(QueueRuleItem item) =>
        item.StartRequestedAt is not null && item.RunId is null;

    /// <summary>
    /// Claim candidates: claimable rows in rank order, lowest first. The real store
    /// walks these in order, skipping rows a concurrent claimer already locked; the
    /// first one is the next claim.
    /// </summary>
    public static IEnumerable<QueueRuleItem> ClaimableInRankOrder(IEnumerable<QueueRuleItem> items) =>
        items.Where(IsClaimable).OrderBy(i => i.Rank);

    /// <summary>The next claim: the lowest-ranked claimable row, or none.</summary>
    public static QueueRuleItem? NextClaimable(IEnumerable<QueueRuleItem> items) =>
        ClaimableInRankOrder(items).FirstOrDefault();

    /// <summary>
    /// Enqueue is idempotent per issue: an issue occupies at most one queue row, so the
    /// existing row for the issue (if any) is returned instead of enqueueing again.
    /// </summary>
    public static QueueRuleItem? ExistingForIssue(IEnumerable<QueueRuleItem> items, long issueId) =>
        items.FirstOrDefault(i => i.IssueId == issueId);
}
