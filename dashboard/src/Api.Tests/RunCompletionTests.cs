using Api.Issues;
using Api.Runs;
using Xunit;

namespace Api.Tests;

/// <summary>
/// The run-completion composition over the fakes: <see cref="FakeRunStore"/> performs
/// the same side effects the real <see cref="RunStore"/> does — host stamping on every
/// event, and on run-finished the queue-slot release plus linked-issue resolve and
/// best-effort GitHub close — via the injected fakes, so unit tests no longer silently
/// skip them. Mirrors what RunStoreIntegrationTests pins over real Postgres.
/// </summary>
public class RunCompletionTests
{
    private static readonly DateTimeOffset T0 = new(2024, 2, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddMinutes(1);

    private static IssueSnapshot Snapshot(long id, int number, string title, string? body = null) =>
        new(
            id,
            number,
            title,
            $"https://github.com/owner/repo/issues/{number}",
            new[] { "ready-for-agent" },
            body,
            "open",
            T0);

    private sealed record Stack(FakeQueueStore Queue, FakeRunStore Runs, RecordingGitHubIssuesClient GitHub, FakeHostStore Host, Guid RunId);

    /// <summary>
    /// The full fake stack — issues, queue, resolver, host, GitHub — wired the way
    /// Program.cs wires the real stores, with the queue's next item already claimed so
    /// <see cref="RunId"/> is the id the events must carry.
    /// </summary>
    private static async Task<Stack> StackWithClaimedIssue()
    {
        var issues = new FakeIssuesStore();
        await issues.SyncRepo("owner/repo", new[] { Snapshot(501, 7, "The issue", body: "Do the thing") });

        var queue = new FakeQueueStore();
        var enqueued = await queue.Enqueue(501);
        await queue.StartNext(enqueued!.Id);

        var host = new FakeHostStore();
        var gitHub = new RecordingGitHubIssuesClient();
        var runs = new FakeRunStore(host, new FakeIssueResolver(queue, issues), gitHub);

        // Claim the way the host relay does: the queue attaches the run id the events use.
        var claim = await queue.ClaimNext();
        Assert.NotNull(claim);

        return new Stack(queue, runs, gitHub, host, claim!.RunId);
    }

    [Fact]
    public async Task Apply_RunFinished_ReleasesQueueSlot_ClosesLinkedIssue_StampsHost()
    {
        var stack = await StackWithClaimedIssue();

        await stack.Runs.Apply(stack.RunId, new RunStartedEvent("owner/repo", "feat/x", "do the thing", "gpt-x") { At = T0 });
        await stack.Runs.Apply(stack.RunId, new RunFinishedEvent { At = T1 });

        Assert.Null(stack.Queue.FindByRunId(stack.RunId));                    // slot released
        Assert.Equal(("owner/repo", 7), Assert.Single(stack.GitHub.Closed)); // linked issue closed
        Assert.NotNull(await stack.Host.GetState());                          // host stamped
    }

    [Fact]
    public async Task Apply_RunFinished_NoLinkedIssue_DoesNotClose()
    {
        var host = new FakeHostStore();
        var gitHub = new RecordingGitHubIssuesClient();
        var runs = new FakeRunStore(host, new FakeIssueResolver(new FakeQueueStore(), new FakeIssuesStore()), gitHub);
        var runId = Guid.NewGuid();

        await runs.Apply(runId, new RunStartedEvent("owner/repo", "feat/x", "do the thing", "gpt-x") { At = T0 });
        await runs.Apply(runId, new RunFinishedEvent { At = T1 });

        Assert.Empty(gitHub.Closed);
    }

    [Fact]
    public async Task Apply_NonFinishedEvent_KeepsQueueSlotAndDoesNotClose()
    {
        var stack = await StackWithClaimedIssue();

        await stack.Runs.Apply(stack.RunId, new RunStartedEvent("owner/repo", "feat/x", "do the thing", "gpt-x") { At = T0 });
        await stack.Runs.Apply(stack.RunId, new AgentStartedEvent("vm-1") { At = T1 });

        // Only run-finished composes the completion: the slot survives, nothing closes.
        Assert.Equal(stack.RunId, stack.Queue.FindByRunId(stack.RunId)!.RunId);
        Assert.Empty(stack.GitHub.Closed);
    }

    [Fact]
    public async Task Apply_NoOpEvent_StillStampsHost()
    {
        // Any event proves the host alive — the real store stamps before folding, so
        // even an event the fold rejects still counts.
        var host = new FakeHostStore();
        var runs = new FakeRunStore(host, resolver: null, gitHub: null);
        var runId = Guid.NewGuid();
        await runs.Apply(runId, new RunStartedEvent("owner/repo", "feat/x", "do the thing", "gpt-x") { At = T1 });

        await runs.Apply(runId, new RunStartedEvent("owner/repo", "feat/y", "ignored", "gpt-x") { At = T0 });

        Assert.NotNull(await host.GetState());
    }

    [Fact]
    public async Task Delete_ReleasesQueueSlot()
    {
        var stack = await StackWithClaimedIssue();
        await stack.Runs.Apply(stack.RunId, new RunStartedEvent("owner/repo", "feat/x", "do the thing", "gpt-x") { At = T0 });

        var deleted = await stack.Runs.Delete(stack.RunId);

        // The real delete releases the queue slot with the run's removal, atomically;
        // the fake composes the same release.
        Assert.True(deleted);
        Assert.Null(stack.Queue.FindByRunId(stack.RunId));
        Assert.Null(await stack.Runs.GetRun(stack.RunId));
    }
}
