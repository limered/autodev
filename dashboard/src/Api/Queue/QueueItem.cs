namespace Api.Queue;

public record QueueItem(
    long Id,
    long IssueId,
    int Rank,
    Guid? RunId,
    DateTimeOffset? StartRequestedAt,
    DateTimeOffset EnqueuedAt);
