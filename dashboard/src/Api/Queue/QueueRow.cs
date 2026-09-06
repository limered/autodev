namespace Api.Queue;

/// <summary>
/// The queue API contract: the queue row plus the issue/run enrichments from the
/// store's LEFT JOIN across issues/runs. The store returns this shape and the
/// endpoints serialize it directly — one shape, no projection seam.
/// </summary>
public record QueueRow(
    long Id,
    long IssueId,
    int Rank,
    Guid? RunId,
    DateTimeOffset? StartRequestedAt,
    string? RunStatus,
    string? Title,
    string? Repo,
    int? Number,
    string? HtmlUrl,
    string? IssueState,
    bool IssuePresent);
