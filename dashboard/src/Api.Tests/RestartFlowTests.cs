using Api.Queue;
using Api.Runs;
using Xunit;

namespace Api.Tests;

/// <summary>
/// The restart flow through the fakes, mirroring POST /runs/{id}/restart plus
/// the claim that follows it: a failed run derives a resume stage, the bound
/// queue row (when present) is reused for the retry, and the new run links back
/// while the failed run keeps its terminal status.
/// </summary>
public class RestartFlowTests
{
    private static readonly RunStage[] Pipeline =
    {
        new("implementation", "m", "implementation", "sequential"),
        new("review-loop", "m", "review-loop", "loop"),
        new("static-loop", "m", "static-loop", "loop"),
        new("test-rerun", "m", "test-rerun", "sequential"),
        new("architecture-review", "m", "architecture-review", "sequential"),
        new("pr-author", "m", "pr-author", "sequential"),
    };

    private static async Task<RunState> FailedRun(FakeRunStore runs, string branch = "factory/issue-1-abc", string? category = "review-loop")
    {
        var runId = Guid.NewGuid();
        await runs.Apply(runId, new RunStartedEvent("owner/repo", branch, "Do the thing", "m", Pipeline));
        await runs.Apply(runId, new AgentStartedEvent("vm-1"));
        if (category is not null)
        {
            await runs.Apply(runId, new HeartbeatEvent("code-review", category));
        }
        await runs.Apply(runId, new RunFailedEvent("provider timeout"));
        return (await runs.GetRun(runId))!;
    }

    [Fact]
    public async Task RestartFlow_QueueClaimedRun_ReusesSameQueueRow()
    {
        var runs = new FakeRunStore();
        var queue = new FakeQueueStore();
        var failed = await FailedRun(runs);

        var enqueued = await queue.Enqueue(42);
        await queue.StartNext(enqueued!.Id);
        var firstClaim = await queue.ClaimNext();
        Assert.NotNull(firstClaim);

        // Bind the queue row to the failed run, as ClaimNext + run-started do.
        var bound = (await queue.All()).Single(i => i.Id == enqueued.Id);
        Assert.Equal(firstClaim.RunId, bound.RunId);

        var resumeStage = RunResume.DeriveResumeStage(failed.Stages, failed.CurrentCategory);
        Assert.Equal("review-loop", resumeStage);

        var reused = await queue.PrepareResume(firstClaim.RunId, failed.Branch, resumeStage, failed.RunId);

        Assert.NotNull(reused);
        Assert.Equal(enqueued.Id, reused.Id);
        Assert.Null(reused.RunId);
        Assert.NotNull(reused.StartRequestedAt);
        Assert.Equal(failed.Branch, reused.ResumeBranch);
        Assert.Equal("review-loop", reused.ResumeStage);
        Assert.Equal(failed.RunId, reused.ParentRunId);
    }

    [Fact]
    public async Task RestartFlow_ClaimAfterPrepare_CarriesResumeInputs()
    {
        var runs = new FakeRunStore();
        var queue = new FakeQueueStore();
        var failed = await FailedRun(runs);

        var enqueued = await queue.Enqueue(42);
        await queue.StartNext(enqueued!.Id);
        var firstClaim = await queue.ClaimNext();
        await queue.PrepareResume(firstClaim!.RunId, failed.Branch, "review-loop", failed.RunId);

        var retry = await queue.ClaimNext();

        Assert.NotNull(retry);
        Assert.Equal(failed.Branch, retry.Branch);
        Assert.Equal("review-loop", retry.ResumeStage);
        Assert.Equal(failed.RunId, retry.ParentRunId);

        // The carry is one-shot: the row no longer holds resume state.
        var row = (await queue.All()).Single(i => i.Id == enqueued.Id);
        Assert.Null(row.ResumeBranch);
        Assert.Null(row.ResumeStage);
        Assert.Null(row.ParentRunId);
    }

    [Fact]
    public async Task RestartFlow_ManualRunWithoutQueueRow_WorksStandalone()
    {
        var runs = new FakeRunStore();
        var queue = new FakeQueueStore();
        var failed = await FailedRun(runs);

        var reused = await queue.PrepareResume(failed.RunId, failed.Branch, "review-loop", failed.RunId);

        Assert.Null(reused);
    }

    [Fact]
    public async Task RestartFlow_NewRun_LinksToFailedRunAndLeavesItTerminal()
    {
        var runs = new FakeRunStore();
        var failed = await FailedRun(runs);

        var retryId = Guid.NewGuid();
        await runs.Apply(retryId, new RunStartedEvent(
            failed.Repo, failed.Branch, failed.Spec, "m", Pipeline,
            ParentRunId: failed.RunId, ResumeStage: "review-loop"));

        var retry = await runs.GetRun(retryId);
        Assert.NotNull(retry);
        Assert.Equal(failed.RunId, retry.ParentRunId);
        Assert.Equal("review-loop", retry.ResumeStage);

        var stillFailed = await runs.GetRun(failed.RunId);
        Assert.Equal(RunStatus.Failed, stillFailed!.Status);

        var response = RunResponse.From(retry);
        Assert.Equal(failed.RunId, response.ParentRunId);

        var children = (await runs.All()).Where(r => r.ParentRunId == failed.RunId).ToArray();
        Assert.Single(children);
    }
}
