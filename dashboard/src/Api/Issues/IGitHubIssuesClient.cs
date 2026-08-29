namespace Api.Issues;

public interface IGitHubIssuesClient
{
    Task CloseIssueAsync(string repo, int issueNumber, CancellationToken cancellationToken = default);
}
