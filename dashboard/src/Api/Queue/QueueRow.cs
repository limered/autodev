namespace Api.Queue;

/// <summary>
/// The raw queue joined-row record: the queue row plus the issue/run enrichments
/// from the store's LEFT JOIN across issues/runs. This is the store's read shape;
/// the API response DTO (<see cref="QueueListItem"/>) is projected from it at the
/// queue endpoint seam via <see cref="QueueListItem.From"/>.
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
