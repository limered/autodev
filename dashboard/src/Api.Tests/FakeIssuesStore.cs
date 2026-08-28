using Api.Issues;

namespace Api.Tests;

public sealed class FakeIssuesStore : IIssuesStore
{
    private readonly Dictionary<long, Issue> _issues = new();

    public Task<IReadOnlyList<Issue>> All()
    {
        var issues = _issues.Values.OrderByDescending(i => i.UpdatedAt).ToList();
        return Task.FromResult<IReadOnlyList<Issue>>(issues);
    }

    public Task SyncRepo(string repo, IReadOnlyList<IssueSnapshot> snapshot)
    {
        foreach (var existing in _issues.Values.Where(i => i.Repo == repo).ToList())
        {
            if (!snapshot.Any(s => s.Id == existing.GitHubId))
            {
                _issues.Remove(existing.GitHubId);
            }
        }

        foreach (var issue in snapshot)
        {
            _issues[issue.Id] = new Issue(
                issue.Id,
                repo,
                issue.Number,
                issue.Title,
                issue.HtmlUrl,
                issue.Labels,
                issue.Body,
                issue.State,
                issue.UpdatedAt);
        }

        return Task.CompletedTask;
    }
}
