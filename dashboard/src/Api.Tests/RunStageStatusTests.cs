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

    private static IReadOnlyList<RunStageView> Derive(string? currentPhase, string runStatus, RunStage[]? stages = null, string? currentCategory = null) =>
        RunStageStatus.Derive(stages ?? Pipeline, currentPhase, runStatus, currentCategory);

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

    private static readonly RunStage[] CategorizedPipeline =
    {
        new("feature-builder", "opencode-go/glm-5.2", "feature-builder"),
        new("test-runner", "opencode-go/kimi-k2.7-code", "test-runner"),
        new("quality-loop", "opencode-go/kimi-k2.7-code", "quality-loop"),
        new("test-runner", "opencode-go/kimi-k2.7-code", "test-runner"),
        new("agentic-review", "opencode-go/kimi-k2.7-code", "agentic-review"),
        new("pr-author", "opencode-go/kimi-k2.7-code", "pr-author"),
    };

    [Fact]
    public void Derive_AnalysisWorkerReport_LightsQualityLoopCategory()
    {
        // The original dark-lights failure: the seed says quality-loop but the
        // reporting worker is static-analysis. The heartbeat carries the
        // category, so the quality-loop slot reads running.
        var views = Derive("static-analysis", "running", CategorizedPipeline, "quality-loop");

        Assert.Equal("done", views[0].Status);
        Assert.Equal("done", views[1].Status);
        Assert.Equal("running", views[2].Status);
        Assert.Equal("pending", views[3].Status);
        Assert.Equal("pending", views[4].Status);
        Assert.Equal("pending", views[5].Status);
    }

    [Fact]
    public void Derive_CategoryReport_SplitsEarlierDoneLaterPending()
    {
        var views = Derive("test-runner", "running", CategorizedPipeline, "test-runner");

        Assert.Equal("done", views[0].Status);
        Assert.Equal("running", views[1].Status);
        Assert.Equal("pending", views[2].Status);
    }

    [Fact]
    public void Derive_CategoryViews_CarryCategoryIdentity()
    {
        var views = Derive("static-analysis", "running", CategorizedPipeline, "quality-loop");

        Assert.Equal("quality-loop", views[2].Category);
        Assert.Equal("quality-loop", views[2].Agent);
        Assert.Equal("feature-builder", views[0].Category);
    }

    [Fact]
    public void Derive_LegacyStagesWithoutCategory_FallBackToWorkerName()
    {
        // Runs persisted before categories carry no category on their stages;
        // the effective category is the worker name, so they render unchanged.
        var views = Derive("test-runner", "running", Pipeline);

        Assert.Equal("test-runner", views[1].Category);
        Assert.Equal("done", views[0].Status);
        Assert.Equal("running", views[1].Status);
        Assert.Equal("pending", views[2].Status);
    }

    [Fact]
    public void Derive_LegacyStages_MatchCategoryReportOnWorkerName()
    {
        // A legacy seed (no categories) still lights when the heartbeat
        // reports the category: the fallback category is the worker name.
        var legacy = new[]
        {
            new RunStage("feature-builder", "m1"),
            new RunStage("quality-loop", "m2"),
        };

        var views = Derive("static-analysis", "running", legacy, "quality-loop");

        Assert.Equal("done", views[0].Status);
        Assert.Equal("running", views[1].Status);
    }

    [Fact]
    public void Derive_UnknownCategory_MarksAllPending()
    {
        var views = Derive("some-worker", "running", CategorizedPipeline, "some-unknown-category");

        Assert.Equal(6, views.Count);
        Assert.All(views, v => Assert.Equal("pending", v.Status));
    }

    [Fact]
    public void Derive_DoneRunWithCategories_MarksAllStagesDone()
    {
        var views = Derive("pr-author", "done", CategorizedPipeline, "pr-author");

        Assert.Equal(6, views.Count);
        Assert.All(views, v => Assert.Equal("done", v.Status));
    }
}
