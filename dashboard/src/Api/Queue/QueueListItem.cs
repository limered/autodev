namespace Api.Queue;

/// <summary>
/// The queue API contract: the queue row plus the issue/run enrichments.
/// Built from the store's raw joined row (<see cref="QueueRow"/>) at the queue
/// endpoint seam, mirroring <c>RunResponse.From</c> — the store never assembles
/// this DTO.
/// </summary>
public record QueueListItem(
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
    bool IssuePresent)
{
    public static QueueListItem From(QueueRow r) => new(
        r.Id,
        r.IssueId,
        r.Rank,
        r.RunId,
        r.StartRequestedAt,
        r.RunStatus,
        r.Title,
        r.Repo,
        r.Number,
        r.HtmlUrl,
        r.IssueState,
        r.IssuePresent);
}
