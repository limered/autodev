using Npgsql;

namespace Api.Issues;

/// <summary>The claim payload for a queued issue: where to clone and what to do.</summary>
public record IssueClaimPayload(string RepoUrl, string Spec);

/// <summary>The GitHub issue linked to a run via its queue row, as repo and number.</summary>
public record LinkedIssue(string Repo, int Number);

/// <summary>
/// The issues domain's narrow resolver seam: the answers to the cross-domain questions
/// the queue and run stores used to embed issues/queue SQL for. Methods take the
/// caller's open connection and transaction and run on that same locked transaction,
/// so the caller's atomicity is preserved — the queue's <c>FOR UPDATE ... SKIP LOCKED</c>
/// claim lock keeps its "missing issue row means claim nothing" guarantee, and the
/// run's queue-slot release stays atomic with its terminal write.
/// </summary>
public interface IIssueResolver
{
    /// <summary>
    /// The claim payload (repo url, spec) for an issue, or <see langword="null"/> when
    /// no issues row exists — the caller claims nothing, exactly like the old
    /// inner-join claim.
    /// </summary>
    Task<IssueClaimPayload?> ResolveClaimPayloadAsync(long issueId, NpgsqlConnection conn, NpgsqlTransaction tx);

    /// <summary>The GitHub issue linked to a run via its queue row, or none.</summary>
    Task<LinkedIssue?> ResolveLinkedIssueAsync(Guid runId, NpgsqlConnection conn, NpgsqlTransaction tx);

    /// <summary>Releases the run's queue slot (if any) within the caller's transaction.</summary>
    Task ReleaseQueueSlotAsync(Guid runId, NpgsqlConnection conn, NpgsqlTransaction tx);
}
