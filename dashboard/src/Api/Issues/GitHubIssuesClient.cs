using System.Net.Http.Json;

namespace Api.Issues;

public sealed class GitHubIssuesClient(HttpClient httpClient, ILogger<GitHubIssuesClient> logger) : IGitHubIssuesClient
{
    private const string ReadyForAgentLabel = "ready-for-agent";

    public async Task CloseIssueAsync(string repo, int issueNumber, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(issueNumber);

        await RemoveLabelAsync(repo, issueNumber, ReadyForAgentLabel, cancellationToken);
        await CloseAsync(repo, issueNumber, cancellationToken);
    }

    private async Task RemoveLabelAsync(
        string repo,
        int issueNumber,
        string label,
        CancellationToken cancellationToken)
    {
        var encodedLabel = Uri.EscapeDataString(label);
        using var response = await httpClient.DeleteAsync(
            $"repos/{repo}/issues/{issueNumber}/labels/{encodedLabel}",
            cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            logger.LogInformation(
                "Label {Label} was not present on issue {Repo}#{Number}; skipping removal.",
                label, repo, issueNumber);
            return;
        }

        response.EnsureSuccessStatusCode();
        logger.LogInformation(
            "Removed label {Label} from issue {Repo}#{Number}.",
            label, repo, issueNumber);
    }

    private async Task CloseAsync(string repo, int issueNumber, CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(new { state = "closed" });
        using var response = await httpClient.PatchAsync(
            $"repos/{repo}/issues/{issueNumber}",
            content,
            cancellationToken);

        response.EnsureSuccessStatusCode();
        logger.LogInformation("Closed issue {Repo}#{Number}.", repo, issueNumber);
    }
}
