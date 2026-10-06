namespace Api.Queue;

/// <summary>
/// The queue API contract: the queue row plus the issue/run enrichments from the
/// store's LEFT JOIN across issues/runs. The store returns this shape and the
/// endpoints serialize it directly — one shape, no projection seam.
/// Resume columns carry a same-branch retry across the clear/re-claim gap:
/// the branch to reuse, the stage to resume from, and the failed run it retries.
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
    bool IssuePresent,
    string? ResumeBranch = null,
    string? ResumeStage = null,
    Guid? ParentRunId = null);
