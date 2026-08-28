namespace Api.Queue;

public record QueueListItem(
    long Id,
    long IssueId,
    int Rank,
    Guid? RunId,
    string? RunStatus,
    string? Title,
    string? Repo,
    int? Number,
    string? HtmlUrl,
    string? IssueState,
    bool IssuePresent);
