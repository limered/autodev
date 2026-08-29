using Api.Runs;
using Xunit;

namespace Api.Tests;

public class RunStageStatusTests
{
    private static readonly RunStage[] Pipeline =
    {
        new("feature-builder", "opencode-go/glm-5.2"),
        new("test-runner", "opencode-go/kimi-k2.7-code"),
        new("pr-author", "opencode-go/kimi-k2.7-code"),
    };

    private static IReadOnlyList<RunStageView> Derive(string? currentPhase, string runStatus, RunStage[]? stages = null) =>
        RunStageStatus.Derive(stages ?? Pipeline, currentPhase, runStatus);

    [Fact]
    public void Derive_NullStages_ReturnsEmpty()
    {
        var views = RunStageStatus.Derive(null, "feature-builder", "running");

        Assert.Empty(views);
    }

    [Fact]
    public void Derive_EmptyStages_ReturnsEmpty()
    {
        var views = RunStageStatus.Derive(Array.Empty<RunStage>(), "feature-builder", "running");

        Assert.Empty(views);
    }

    [Fact]
    public void Derive_NoCurrentPhase_MarksAllPending()
    {
        // A run that has not reported a phase yet (launching, or running before
        // the first phase heartbeat) has not started any stage.
        var views = Derive(null, "running");

        Assert.Equal(3, views.Count);
        Assert.All(views, v => Assert.Equal("pending", v.Status));
        Assert.Equal("feature-builder", views[0].Agent);
        Assert.Equal("opencode-go/glm-5.2", views[0].Model);
    }

    [Fact]
    public void Derive_CurrentPhaseFirst_OnlyFirstRunning()
    {
        var views = Derive("feature-builder", "running");

        Assert.Equal("running", views[0].Status);
        Assert.Equal("pending", views[1].Status);
        Assert.Equal("pending", views[2].Status);
    }

    [Fact]
    public void Derive_CurrentPhaseMiddle_SplitsDoneRunningPending()
    {
        var views = Derive("test-runner", "running");

        Assert.Equal("done", views[0].Status);
        Assert.Equal("running", views[1].Status);
        Assert.Equal("pending", views[2].Status);
    }

    [Fact]
    public void Derive_CurrentPhaseLast_LastRunningEarlierDone()
    {
        var views = Derive("pr-author", "running");

        Assert.Equal("done", views[0].Status);
        Assert.Equal("done", views[1].Status);
        Assert.Equal("running", views[2].Status);
    }

    [Fact]
    public void Derive_DoneRun_MarksAllStagesDone()
    {
        // A finished run completed every stage; the phase marker still points at
        // the last agent, so positional derivation alone would leave it running.
        var views = Derive("pr-author", "done");

        Assert.Equal(3, views.Count);
        Assert.All(views, v => Assert.Equal("done", v.Status));
    }

    [Fact]
    public void Derive_FailedRun_LeavesActiveStageRunning()
    {
        // No separate failed state: a failed run simply stops advancing, so the
        // last active phase stays running and marks where it failed.
        var views = Derive("test-runner", "failed");

        Assert.Equal("done", views[0].Status);
        Assert.Equal("running", views[1].Status);
        Assert.Equal("pending", views[2].Status);
    }

    [Fact]
    public void Derive_StalledRun_LeavesActiveStageRunning()
    {
        var views = Derive("test-runner", "stalled");

        Assert.Equal("done", views[0].Status);
        Assert.Equal("running", views[1].Status);
        Assert.Equal("pending", views[2].Status);
    }

    [Fact]
    public void Derive_UnknownPhase_MarksAllPending()
    {
        // A phase that does not match any seeded stage tells us nothing about
        // progress; fall back to pending rather than guessing.
        var views = Derive("some-other-phase", "running");

        Assert.Equal(3, views.Count);
        Assert.All(views, v => Assert.Equal("pending", v.Status));
    }

    [Fact]
    public void Derive_PreservesAgentAndModelPerStage()
    {
        var views = Derive("feature-builder", "running");

        Assert.Equal("feature-builder", views[0].Agent);
        Assert.Equal("opencode-go/glm-5.2", views[0].Model);
        Assert.Equal("test-runner", views[1].Agent);
        Assert.Equal("opencode-go/kimi-k2.7-code", views[1].Model);
        Assert.Equal("pr-author", views[2].Agent);
        Assert.Equal("opencode-go/kimi-k2.7-code", views[2].Model);
    }
}
