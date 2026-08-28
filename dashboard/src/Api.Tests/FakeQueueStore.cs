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
            false);

        _items.Add(item);
        return Task.FromResult<QueueListItem?>(item);
    }
}
