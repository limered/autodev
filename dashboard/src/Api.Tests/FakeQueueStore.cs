using Api.Queue;

namespace Api.Tests;

public sealed class FakeQueueStore : IQueueStore
{
    private readonly List<QueueListItem> _items = new();
    private long _nextId = 1;

    public Task<IReadOnlyList<QueueListItem>> All()
    {
        var ordered = _items.OrderBy(i => i.Rank).ToList();
        return Task.FromResult<IReadOnlyList<QueueListItem>>(ordered);
    }

    public Task<QueueListItem?> Enqueue(long issueId)
    {
        var existing = _items.FirstOrDefault(i => i.IssueId == issueId);
        if (existing is not null)
        {
            return Task.FromResult<QueueListItem?>(existing);
        }

        var nextRank = _items.Any() ? _items.Max(i => i.Rank) + 1 : 1;
        var item = new QueueListItem(
            _nextId++,
            issueId,
            nextRank,
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

    public Task<ClaimedQueueItem?> ClaimNext()
    {
        var item = _items
            .Where(i => i.StartRequestedAt.HasValue && i.RunId is null)
            .OrderBy(i => i.Rank)
            .FirstOrDefault();

        if (item is null)
        {
            return Task.FromResult<ClaimedQueueItem?>(null);
        }

        var runId = Guid.NewGuid();
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
