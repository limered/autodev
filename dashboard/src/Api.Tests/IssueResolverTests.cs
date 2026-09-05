using Api.Issues;
using Npgsql;
using Xunit;

namespace Api.Tests;

/// <summary>
/// The issue-resolver seam: the issues domain's answers to the cross-domain questions
/// the queue and run stores used to embed SQL for. These tests pin the in-memory
/// <see cref="FakeIssueResolver"/> (which serves the injected fakes' rows, ignoring the
/// connection/transaction); the SQL implementation is pinned by the queue/run
/// integration tests that now route through it.
/// </summary>
public class IssueResolverTests
{
    private static readonly DateTimeOffset T0 = new(2024, 2, 1, 0, 0, 0, TimeSpan.Zero);

    private static IssueSnapshot S(
        long id,
        int number,
        string title,
        string? body = null,
        DateTimeOffset? updatedAt = null)
    {
        return new IssueSnapshot(
            id,
            number,
            title,
            $"https://github.com/owner/repo/issues/{number}",
            new[] { "ready-for-agent" },
            body,
            "open",
            updatedAt ?? T0);
    }

    // The fake ignores the caller's connection/transaction — that is its contract —
    // so the tests pass nulls where the real store would pass its locked conn/tx.
    private static readonly NpgsqlConnection NoConn = null!;
    private static readonly NpgsqlTransaction NoTx = null!;

    [Fact]
    public async Task ResolveClaimPayloadAsync_IssueWithBody_ReturnsRepoUrlAndBodyAsSpec()
    {
        var issues = new FakeIssuesStore();
        await issues.SyncRepo("owner/repo", new[] { S(501, 7, "The title", body: "  do the thing  ") });
        var resolver = new FakeIssueResolver(new FakeQueueStore(), issues);

        var payload = await resolver.ResolveClaimPayloadAsync(501, NoConn, NoTx);

        Assert.NotNull(payload);
        Assert.Equal("https://github.com/owner/repo.git", payload!.RepoUrl);
        Assert.Equal("do the thing", payload.Spec);
    }

    [Fact]
    public async Task ResolveClaimPayloadAsync_BodyMissing_FallsBackToTitle()
    {
        var issues = new FakeIssuesStore();
        await issues.SyncRepo("owner/repo", new[] { S(502, 8, "Do the thing") });
        var resolver = new FakeIssueResolver(new FakeQueueStore(), issues);

        var payload = await resolver.ResolveClaimPayloadAsync(502, NoConn, NoTx);

        Assert.NotNull(payload);
        Assert.Equal("Do the thing", payload!.Spec);
    }

    [Fact]
    public async Task ResolveClaimPayloadAsync_BodyBlank_FallsBackToTitle()
    {
        var issues = new FakeIssuesStore();
        await issues.SyncRepo("owner/repo", new[] { S(503, 9, "Title wins", body: "   ") });
        var resolver = new FakeIssueResolver(new FakeQueueStore(), issues);

        var payload = await resolver.ResolveClaimPayloadAsync(503, NoConn, NoTx);

        Assert.NotNull(payload);
        Assert.Equal("Title wins", payload!.Spec);
    }

    [Fact]
    public async Task ResolveClaimPayloadAsync_NoIssueRow_ReturnsNull()
    {
        // The old single-statement claim inner-joined issues: no issues row meant the
        // queue row matched nothing and claimed nothing. The seam keeps that guarantee.
        var resolver = new FakeIssueResolver(new FakeQueueStore(), new FakeIssuesStore());

        var payload = await resolver.ResolveClaimPayloadAsync(999, NoConn, NoTx);

        Assert.Null(payload);
    }

    /// <summary>Claims the next queue item and returns the run id now attached to it.</summary>
    private static async Task<Guid> ClaimedRunId(FakeQueueStore queue)
    {
        var claim = await queue.ClaimNext();
        Assert.NotNull(claim);
        return claim!.RunId;
    }

    [Fact]
    public async Task ResolveLinkedIssueAsync_RunHasQueueRow_ReturnsLinkedIssue()
    {
        var issues = new FakeIssuesStore();
        await issues.SyncRepo("owner/repo", new[] { S(501, 7, "The title") });
        var queue = new FakeQueueStore();
        await queue.Enqueue(501);
        await queue.StartNext((await queue.All()).Single().Id);
        var resolver = new FakeIssueResolver(queue, issues);

        var runId = await ClaimedRunId(queue);
        var linked = await resolver.ResolveLinkedIssueAsync(runId, NoConn, NoTx);

        Assert.NotNull(linked);
        Assert.Equal(("owner/repo", 7), (linked!.Repo, linked.Number));
    }

    [Fact]
    public async Task ResolveLinkedIssueAsync_NoQueueRow_ReturnsNull()
    {
        var issues = new FakeIssuesStore();
        await issues.SyncRepo("owner/repo", new[] { S(501, 7, "The title") });
        var resolver = new FakeIssueResolver(new FakeQueueStore(), issues);

        var linked = await resolver.ResolveLinkedIssueAsync(Guid.NewGuid(), NoConn, NoTx);

        Assert.Null(linked);
    }

    [Fact]
    public async Task ResolveLinkedIssueAsync_QueueRowWithoutIssueRow_ReturnsNull()
    {
        // The SQL resolver inner-joins queue to issues, so a queue row whose issue has
        // no issues row answers no linked issue — no close fires for it.
        var queue = new FakeQueueStore();
        await queue.Enqueue(404);
        await queue.StartNext((await queue.All()).Single().Id);
        var resolver = new FakeIssueResolver(queue, new FakeIssuesStore());

        var runId = await ClaimedRunId(queue);
        var linked = await resolver.ResolveLinkedIssueAsync(runId, NoConn, NoTx);

        Assert.Null(linked);
    }

    [Fact]
    public async Task ReleaseQueueSlotAsync_RemovesTheRunsQueueRow()
    {
        var issues = new FakeIssuesStore();
        await issues.SyncRepo("owner/repo", new[] { S(501, 7, "The title") });
        var queue = new FakeQueueStore();
        await queue.Enqueue(501);
        await queue.StartNext((await queue.All()).Single().Id);
        var resolver = new FakeIssueResolver(queue, issues);

        var runId = await ClaimedRunId(queue);
        await resolver.ReleaseQueueSlotAsync(runId, NoConn, NoTx);

        Assert.Null(queue.FindByRunId(runId));
        Assert.Empty(await queue.All());
    }

    [Fact]
    public async Task ReleaseQueueSlotAsync_NoQueueRow_IsNoOp()
    {
        var queue = new FakeQueueStore();
        await queue.Enqueue(501); // queued, but never attached to a run
        var resolver = new FakeIssueResolver(queue, new FakeIssuesStore());

        await resolver.ReleaseQueueSlotAsync(Guid.NewGuid(), NoConn, NoTx);

        Assert.Single(await queue.All());
    }
}
