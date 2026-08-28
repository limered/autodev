using Api.Issues;
using Xunit;

namespace Api.Tests;

public class IssuesSyncTests
{
    private static readonly DateTimeOffset T0 = new(2024, 2, 1, 0, 0, 0, TimeSpan.Zero);

    private static IssueSnapshot S(long id, int number, string title, DateTimeOffset? updatedAt = null)
    {
        return new IssueSnapshot(
            id,
            number,
            title,
            $"https://github.com/owner/repo/issues/{number}",
            new[] { "ready-for-agent" },
            null,
            "open",
            updatedAt ?? T0);
    }

    [Fact]
    public async Task SyncRepo_AddsIssues()
    {
        var store = new FakeIssuesStore();

        await store.SyncRepo("owner/repo", new[] { S(1, 1, "First issue"), S(2, 2, "Second issue") });

        var issues = await store.All();
        Assert.Equal(2, issues.Count);
        Assert.Contains(issues, i => i.Number == 1 && i.Title == "First issue");
        Assert.Contains(issues, i => i.Number == 2 && i.Title == "Second issue");
    }

    [Fact]
    public async Task SyncRepo_RemovesIssuesNotInSnapshot()
    {
        var store = new FakeIssuesStore();
        await store.SyncRepo("owner/repo", new[] { S(1, 1, "First issue"), S(2, 2, "Second issue") });

        await store.SyncRepo("owner/repo", new[] { S(1, 1, "First issue") });

        var issues = await store.All();
        Assert.Single(issues);
        Assert.Equal(1, issues[0].Number);
    }

    [Fact]
    public async Task SyncRepo_UpdatesExistingIssue()
    {
        var store = new FakeIssuesStore();
        await store.SyncRepo("owner/repo", new[] { S(1, 1, "Original title") });

        await store.SyncRepo("owner/repo", new[] { S(1, 1, "Updated title", T0.AddHours(1)) });

        var issues = await store.All();
        Assert.Single(issues);
        Assert.Equal("Updated title", issues[0].Title);
    }

    [Fact]
    public async Task SyncRepo_IsolatedByRepo()
    {
        var store = new FakeIssuesStore();
        await store.SyncRepo("owner/repo-a", new[] { S(1, 1, "Repo A issue") });
        await store.SyncRepo("owner/repo-b", new[] { S(2, 2, "Repo B issue") });

        await store.SyncRepo("owner/repo-a", Array.Empty<IssueSnapshot>());

        var issues = await store.All();
        Assert.Single(issues);
        Assert.Equal("owner/repo-b", issues[0].Repo);
    }
}
