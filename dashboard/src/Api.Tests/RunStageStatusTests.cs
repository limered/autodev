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

    private static IReadOnlyList<RunStageView> Derive(string runStatus, RunStage[]? stages = null, string? currentCategory = null) =>
        RunStageStatus.Derive(stages ?? Pipeline, runStatus, currentCategory);

    [Fact]
    public void Derive_NullStages_ReturnsEmpty()
    {
        var views = RunStageStatus.Derive(null, "running", "feature-builder");

        Assert.Empty(views);
    }

    [Fact]
    public void Derive_EmptyStages_ReturnsEmpty()
    {
        var views = RunStageStatus.Derive(Array.Empty<RunStage>(), "running", "feature-builder");

        Assert.Empty(views);
    }

    [Fact]
    public void Derive_NoCurrentCategory_MarksAllPending()
    {
        // A run that has not reported a category yet (launching, or running
        // before the first category heartbeat) has not started any stage.
        var views = Derive("running", CategorizedPipeline);

        Assert.Equal(6, views.Count);
        Assert.All(views, v => Assert.Equal("pending", v.Status));
        Assert.Equal("feature-builder", views[0].Agent);
        Assert.Equal("opencode-go/glm-5.2", views[0].Model);
    }

    [Fact]
    public void Derive_StalledRun_LeavesActiveCategoryRunning()
    {
        var views = Derive("stalled", CategorizedPipeline, "quality-loop");

        Assert.Equal("done", views[0].Status);
        Assert.Equal("done", views[1].Status);
        Assert.Equal("running", views[2].Status);
        Assert.Equal("pending", views[3].Status);
    }

    [Fact]
    public void Derive_PreservesAgentAndModelPerStage()
    {
        var views = Derive("running", CategorizedPipeline, "feature-builder");

        Assert.Equal("feature-builder", views[0].Agent);
        Assert.Equal("opencode-go/glm-5.2", views[0].Model);
        Assert.Equal("test-runner", views[1].Agent);
        Assert.Equal("opencode-go/kimi-k2.7-code", views[1].Model);
        Assert.Equal("pr-author", views[5].Agent);
        Assert.Equal("opencode-go/kimi-k2.7-code", views[5].Model);
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
        var views = Derive("running", CategorizedPipeline, "quality-loop");

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
        var views = Derive("running", CategorizedPipeline, "test-runner");

        Assert.Equal("done", views[0].Status);
        Assert.Equal("running", views[1].Status);
        Assert.Equal("pending", views[2].Status);
    }

    [Fact]
    public void Derive_CategoryViews_CarryCategoryIdentity()
    {
        var views = Derive("running", CategorizedPipeline, "quality-loop");

        Assert.Equal("quality-loop", views[2].Category);
        Assert.Equal("quality-loop", views[2].Agent);
        Assert.Equal("feature-builder", views[0].Category);
    }

    [Fact]
    public void Derive_UnknownCategory_MarksAllPending()
    {
        var views = Derive("running", CategorizedPipeline, "some-unknown-category");

        Assert.Equal(6, views.Count);
        Assert.All(views, v => Assert.Equal("pending", v.Status));
    }

    [Fact]
    public void Derive_WorkerNameReport_DoesNotMatchAnyStage()
    {
        // Category matching is the only path: a reported worker name never
        // lights a stage, even one seeded under that worker's name.
        var views = Derive("running", CategorizedPipeline, "static-analysis");

        Assert.Equal(6, views.Count);
        Assert.All(views, v => Assert.Equal("pending", v.Status));
    }

    [Fact]
    public void Uncategorized_PinsSharedLiteral()
    {
        Assert.Equal("uncategorized", RunStageStatus.Uncategorized);
    }

    [Fact]
    public void Derive_StagesWithoutCategory_ReadAsUncategorized()
    {
        // Runs persisted before categories carry no category on their stages;
        // they read as the uncategorized bucket instead of the worker name.
        var views = Derive("running");

        Assert.Equal(3, views.Count);
        Assert.All(views, v => Assert.Equal("uncategorized", v.Category));
        Assert.All(views, v => Assert.Equal("pending", v.Status));
    }

    [Fact]
    public void Derive_UncategorizedBucketNeverGoesActive()
    {
        var views = Derive("running", Pipeline, "uncategorized");

        Assert.Equal(3, views.Count);
        Assert.All(views, v => Assert.Equal("pending", v.Status));
    }

    [Fact]
    public void Derive_DoneRunWithCategories_MarksAllStagesDone()
    {
        var views = Derive("done", CategorizedPipeline, "pr-author");

        Assert.Equal(6, views.Count);
        Assert.All(views, v => Assert.Equal("done", v.Status));
    }

    [Fact]
    public void Derive_DoneRunWithoutCategories_MarksAllStagesDone()
    {
        var views = Derive("done", Pipeline, "feature-builder");

        Assert.Equal(3, views.Count);
        Assert.All(views, v => Assert.Equal("done", v.Status));
    }

    [Fact]
    public void Derive_FailedRun_MarksActiveCategoryFailed()
    {
        // A failed run stops advancing: earlier categories read done, the
        // active category marks where it failed, later ones stay pending.
        var views = Derive("failed", CategorizedPipeline, "quality-loop");

        Assert.Equal("done", views[0].Status);
        Assert.Equal("done", views[1].Status);
        Assert.Equal("failed", views[2].Status);
        Assert.Equal("pending", views[3].Status);
        Assert.Equal("pending", views[4].Status);
        Assert.Equal("pending", views[5].Status);
    }

    [Fact]
    public void Derive_FailedRunWithUnknownCategory_MarksAllPending()
    {
        // With no matching category the failure location is unknown, so
        // nothing is marked failed rather than guessing.
        var views = Derive("failed", CategorizedPipeline, "some-unknown-category");

        Assert.Equal(6, views.Count);
        Assert.All(views, v => Assert.Equal("pending", v.Status));
    }

    [Fact]
    public void Derive_FailedRunWithoutCategory_MarksAllPending()
    {
        var views = Derive("failed", CategorizedPipeline);

        Assert.Equal(6, views.Count);
        Assert.All(views, v => Assert.Equal("pending", v.Status));
    }

    [Fact]
    public void Derive_PropagatesSeededStageType_ForLoopMembership()
    {
        var stages = new[]
        {
            new RunStage("implementation", "m1", "implementation", "sequential"),
            new RunStage("quality-loop", "m2", "quality-loop", "loop"),
        };

        var views = Derive("running", stages, "quality-loop");

        Assert.Equal("sequential", views[0].Type);
        Assert.Equal("loop", views[1].Type);
        Assert.Equal("done", views[0].Status);
        Assert.Equal("running", views[1].Status);
    }

    [Fact]
    public void Derive_LegacyStagesWithoutType_ProjectNullType()
    {
        var views = Derive("running");

        Assert.Equal(3, views.Count);
        Assert.All(views, v => Assert.Null(v.Type));
    }
}
