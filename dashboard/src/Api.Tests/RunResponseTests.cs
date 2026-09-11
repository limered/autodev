using Api.Runs;
using Xunit;

namespace Api.Tests;

public class RunResponseTests
{
    private static readonly Guid RunId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset T0 = new(2024, 3, 1, 0, 0, 0, TimeSpan.Zero);

    private static RunState State(
        string status = "running",
        string? currentPhase = null,
        IReadOnlyList<RunStage>? stages = null,
        IReadOnlyList<RunStep>? steps = null,
        string? currentCategory = null) =>
        new(RunId, "owner/repo", "main", "spec", "glm-5.2", "vm-1", status, T0, null, T0, "https://pr", null, false, null, T0, stages, currentPhase, steps, currentCategory);

    private static readonly RunStage[] Pipeline =
    {
        new("feature-builder", "m1", "feature-builder"),
        new("test-runner", "m2", "test-runner"),
        new("pr-author", "m3", "pr-author"),
    };

    [Fact]
    public void From_ProjectsDerivedStageStatus()
    {
        var state = State(status: "running", currentPhase: "test-runner", stages: Pipeline, currentCategory: "test-runner");

        var response = RunResponse.From(state);

        Assert.Equal(RunId, response.RunId);
        Assert.Equal("owner/repo", response.Repo);
        Assert.Equal("running", response.Status);
        Assert.Equal("test-runner", response.CurrentPhase);
        Assert.Equal("test-runner", response.CurrentCategory);
        Assert.Equal("https://pr", response.PrUrl);

        Assert.NotNull(response.Stages);
        Assert.Equal(3, response.Stages!.Count);
        Assert.Equal("done", response.Stages[0].Status);
        Assert.Equal("running", response.Stages[1].Status);
        Assert.Equal("pending", response.Stages[2].Status);
        Assert.Equal("feature-builder", response.Stages[0].Agent);
        Assert.Equal("m1", response.Stages[0].Model);
    }

    [Fact]
    public void From_WorkerPhaseAlone_LightsNothing()
    {
        // The heartbeat worker name no longer drives the lights: without a
        // reported category every stage stays pending.
        var state = State(status: "running", currentPhase: "test-runner", stages: Pipeline);

        var response = RunResponse.From(state);

        Assert.NotNull(response.Stages);
        Assert.Equal(3, response.Stages!.Count);
        Assert.All(response.Stages, s => Assert.Equal("pending", s.Status));
    }

    [Fact]
    public void From_RunWithoutStages_ReturnsEmptyStageList()
    {
        var state = State(status: "launching", stages: null);

        var response = RunResponse.From(state);

        Assert.NotNull(response.Stages);
        Assert.Empty(response.Stages);
    }

    [Fact]
    public void From_ProjectsFlatStepsList()
    {
        var steps = new[]
        {
            new RunStep("feature-builder", 0, 61000, 100, 50, 0.0123m, "done", "m1"),
            new RunStep("static-analysis", 1, 2000, 10, 5, null, "done", "m2"),
            new RunStep("feature-builder", 1, 3000, 30, 15, null, "failed", "m1"),
        };
        var state = State(status: "done", currentPhase: "pr-author", stages: Pipeline, steps: steps);

        var response = RunResponse.From(state);

        Assert.Equal(3, response.Steps.Count);
        Assert.Equal(new RunStepView("feature-builder", 0, "m1", "done", 100, 50, 61000, 0.0123m), response.Steps[0]);
        Assert.Equal(new RunStepView("static-analysis", 1, "m2", "done", 10, 5, 2000, null), response.Steps[1]);
        Assert.Equal(new RunStepView("feature-builder", 1, "m1", "failed", 30, 15, 3000, null), response.Steps[2]);
        // The existing stages field is unchanged: a finished run collapses it to done.
        Assert.Equal(3, response.Stages.Count);
        Assert.All(response.Stages, s => Assert.Equal("done", s.Status));
    }

    [Fact]
    public void From_RunWithoutSteps_ReturnsEmptyStepList()
    {
        // Pre-steps runs still render via the stages fallback.
        var state = State(status: "running", currentPhase: "test-runner", stages: Pipeline, steps: null, currentCategory: "test-runner");

        var response = RunResponse.From(state);

        Assert.NotNull(response.Steps);
        Assert.Empty(response.Steps);
        Assert.Equal(3, response.Stages.Count);
        Assert.Equal("running", response.Stages[1].Status);
    }

    [Fact]
    public void From_AnalysisReport_LightsQualityLoopCategory()
    {
        var stages = new[]
        {
            new RunStage("feature-builder", "m1", "feature-builder"),
            new RunStage("test-runner", "m2", "test-runner"),
            new RunStage("quality-loop", "m3", "quality-loop"),
            new RunStage("pr-author", "m4", "pr-author"),
        };
        var state = State(status: "running", currentPhase: "static-analysis", stages: stages, currentCategory: "quality-loop");

        var response = RunResponse.From(state);

        Assert.Equal("quality-loop", response.CurrentCategory);
        Assert.Equal(4, response.Stages.Count);
        Assert.Equal("done", response.Stages[0].Status);
        Assert.Equal("done", response.Stages[1].Status);
        Assert.Equal("running", response.Stages[2].Status);
        Assert.Equal("pending", response.Stages[3].Status);
        Assert.Equal("quality-loop", response.Stages[2].Category);
    }

    [Fact]
    public void From_ProjectsStepCategoriesForGroupedDetail()
    {
        var steps = new[]
        {
            new RunStep("feature-builder", 0, 61000, 100, 50, null, "done", "m1", "implementation"),
            new RunStep("static-analysis", 1, 2000, 10, 5, null, "done", "m2", "quality-loop"),
            new RunStep("feature-builder", 1, 3000, 30, 15, null, "done", "m1", "quality-loop"),
        };
        var stages = new[]
        {
            new RunStage("implementation", "m1", "implementation"),
            new RunStage("quality-loop", "m2", "quality-loop"),
        };
        var state = State(status: "done", currentPhase: "pr-author", stages: stages, steps: steps, currentCategory: "pr-author");

        var response = RunResponse.From(state);

        Assert.Equal(3, response.Steps.Count);
        Assert.Equal("implementation", response.Steps[0].Category);
        Assert.Equal("quality-loop", response.Steps[1].Category);
        Assert.Equal("quality-loop", response.Steps[2].Category);
        // Detail granularity is unchanged and the lights still collapse to done.
        Assert.Equal(new RunStepView("feature-builder", 0, "m1", "done", 100, 50, 61000, null, "implementation"), response.Steps[0]);
        Assert.All(response.Stages, s => Assert.Equal("done", s.Status));
    }

    [Fact]
    public void From_LegacyStepsWithoutCategory_ProjectNull()
    {
        var steps = new[]
        {
            new RunStep("feature-builder", 0, 61000, 100, 50, null, "done", "m1"),
        };
        var state = State(status: "done", currentPhase: "feature-builder", stages: Pipeline, steps: steps);

        var response = RunResponse.From(state);

        Assert.Null(Assert.Single(response.Steps).Category);
    }

    [Fact]
    public void From_FailedRun_MarksActiveCategoryFailed()
    {
        var stages = new[]
        {
            new RunStage("feature-builder", "m1", "feature-builder"),
            new RunStage("quality-loop", "m3", "quality-loop"),
            new RunStage("pr-author", "m4", "pr-author"),
        };
        var state = State(status: "failed", currentPhase: "static-analysis", stages: stages, currentCategory: "quality-loop");

        var response = RunResponse.From(state);

        Assert.Equal(3, response.Stages.Count);
        Assert.Equal("done", response.Stages[0].Status);
        Assert.Equal("failed", response.Stages[1].Status);
        Assert.Equal("pending", response.Stages[2].Status);
    }

    [Fact]
    public void From_RunWithoutCategories_ReadsUncategorized()
    {
        var legacy = new[]
        {
            new RunStage("feature-builder", "m1"),
            new RunStage("test-runner", "m2"),
        };
        var state = State(status: "running", currentPhase: "test-runner", stages: legacy);

        var response = RunResponse.From(state);

        Assert.Null(response.CurrentCategory);
        Assert.Equal(2, response.Stages.Count);
        Assert.All(response.Stages, s => Assert.Equal(RunStageStatus.Uncategorized, s.Category));
        Assert.All(response.Stages, s => Assert.Equal("pending", s.Status));
    }
}
