using Api.Issues;
using Npgsql;

namespace Api.Tests;

/// <summary>
/// In-memory <see cref="IIssueResolver"/>: same signature as the SQL resolver, but the
/// caller's connection/transaction are ignored and the answers are served from the
/// injected fakes' rows, so unit tests can exercise the cross-domain claim and
/// run-composition paths without Postgres.
/// </summary>
public sealed class FakeIssueResolver : IIssueResolver
{
    private readonly FakeQueueStore _queue;
    private readonly FakeIssuesStore _issues;

    public FakeIssueResolver(FakeQueueStore queue, FakeIssuesStore issues)
    {
        _queue = queue;
        _issues = issues;
    }

    public async Task<IssueClaimPayload?> ResolveClaimPayloadAsync(long issueId, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        var issue = (await _issues.All()).FirstOrDefault(i => i.GitHubId == issueId);
        return issue is null
            ? null
            : IssueClaimRules.PayloadFor(issue.Repo, issue.Body, issue.Title);
    }

    public async Task<LinkedIssue?> ResolveLinkedIssueAsync(Guid runId, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        // Inner-join semantics, same as the SQL resolver: no queue row, or a queue row
        // whose issue has no issues row, answers no linked issue.
        var slot = _queue.FindByRunId(runId);
        if (slot is null)
        {
            return null;
        }

        var issue = (await _issues.All()).FirstOrDefault(i => i.GitHubId == slot.IssueId);
        return issue is null ? null : new LinkedIssue(issue.Repo, issue.Number);
    }

    public Task ReleaseQueueSlotAsync(Guid runId, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        _queue.DeleteByRunId(runId);
        return Task.CompletedTask;
    }
}
