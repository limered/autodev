using Api.Queue;

namespace Api.Tests;

public sealed class FakeQueueStore : IQueueStore
{
    private readonly List<QueueListItem> _items = new();
    private long _nextId = 1;

    /// <summary>
    /// The boundary projection: QueueRules sees only the queue row fields, so the
    /// enrichments QueueListItem adds for the API response are dropped here.
    /// </summary>
    private IEnumerable<QueueRuleItem> RuleItems() =>
        _items.Select(i => new QueueRuleItem(i.Id, i.IssueId, i.Rank, i.RunId, i.StartRequestedAt));

    public Task<IReadOnlyList<QueueListItem>> All()
    {
        var ordered = _items.OrderBy(i => i.Rank).ToList();
        return Task.FromResult<IReadOnlyList<QueueListItem>>(ordered);
    }

    public Task<QueueListItem?> Enqueue(long issueId)
    {
        // Rank/dedup derivation is QueueRules', shared with the real SQL store; only
        // the storage here is fake.
        var existing = QueueRules.ExistingForIssue(RuleItems(), issueId);
        if (existing is not null)
        {
            return Task.FromResult<QueueListItem?>(_items.Single(i => i.Id == existing.Id));
        }

        var item = new QueueListItem(
            _nextId++,
            issueId,
            QueueRules.NextRank(_items.Select(i => i.Rank)),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false);

        _items.Add(item);
        return Task.FromResult<QueueListItem?>(item);
    }

    public Task<QueueListItem?> StartNext(long id)
    {
        var item = _items.FirstOrDefault(i => i.Id == id);
        if (item is null)
        {
            return Task.FromResult<QueueListItem?>(null);
        }

        var idx = _items.IndexOf(item);
        _items[idx] = item with { StartRequestedAt = DateTimeOffset.UtcNow };
        return Task.FromResult<QueueListItem?>(_items[idx]);
    }

    public Task<QueueListItem?> Restart(long id)
    {
        var item = _items.FirstOrDefault(i => i.Id == id);
        if (item is null)
        {
            return Task.FromResult<QueueListItem?>(null);
        }

        var idx = _items.IndexOf(item);
        _items[idx] = item with { RunId = null, StartRequestedAt = null };
        return Task.FromResult<QueueListItem?>(_items[idx]);
    }

    public Task<ClaimedQueueItem?> ClaimNext()
    {
        var next = QueueRules.NextClaimable(RuleItems());

        if (next is null)
        {
            return Task.FromResult<ClaimedQueueItem?>(null);
        }

        var runId = Guid.NewGuid();
        var item = _items.Single(i => i.Id == next.Id);
        var idx = _items.IndexOf(item);
        _items[idx] = item with { RunId = runId };
        return Task.FromResult<ClaimedQueueItem?>(new ClaimedQueueItem(runId, "https://github.com/test/repo.git", "spec"));
    }

    public Task Reorder(IReadOnlyList<long> ids)
    {
        for (var i = 0; i < ids.Count; i++)
        {
            var item = _items.FirstOrDefault(q => q.Id == ids[i]);
            if (item is not null)
            {
                var idx = _items.IndexOf(item);
                _items[idx] = item with { Rank = i + 1 };
            }
        }

        return Task.CompletedTask;
    }

    public Task<bool> Delete(long id)
    {
        var item = _items.FirstOrDefault(q => q.Id == id);
        if (item is null)
        {
            return Task.FromResult(false);
        }

        _items.Remove(item);
        return Task.FromResult(true);
    }
}
