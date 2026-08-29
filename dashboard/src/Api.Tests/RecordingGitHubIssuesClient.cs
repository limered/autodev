using Api.Issues;

namespace Api.Tests;

/// <summary>
/// Test double for <see cref="IGitHubIssuesClient"/> that records every
/// <see cref="CloseIssueAsync"/> call, so integration tests can assert the run-finished
/// path resolved the linked issue and asked to close it with the right repo/number.
/// </summary>
public sealed class RecordingGitHubIssuesClient : IGitHubIssuesClient
{
    public List<(string Repo, int Number)> Closed { get; } = new();

    public Task CloseIssueAsync(string repo, int issueNumber, CancellationToken cancellationToken = default)
    {
        Closed.Add((repo, issueNumber));
        return Task.CompletedTask;
    }
}
