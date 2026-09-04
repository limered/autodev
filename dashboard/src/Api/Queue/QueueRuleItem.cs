namespace Api.Queue;

/// <summary>
/// The queue fields <see cref="QueueRules"/> decides on: the queue row's own columns,
/// none of the issue/run enrichments <see cref="QueueListItem"/> adds for the API
/// response. The stores map to this at their boundary — the SQL store reads queue
/// rows directly, the fake projects its DTOs — so enrichment changes to the response
/// shape cannot reach rule logic.
/// </summary>
public record QueueRuleItem(
    long Id,
    long IssueId,
    int Rank,
    Guid? RunId,
    DateTimeOffset? StartRequestedAt);
