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
        IReadOnlyList<RunStep>? steps = null) =>
        new(RunId, "owner/repo", "main", "spec", "glm-5.2", "vm-1", status, T0, null, T0, "https://pr", null, false, null, T0, stages, currentPhase, steps);

    private static readonly RunStage[] Pipeline =
    {
        new("feature-builder", "m1"),
        new("test-runner", "m2"),
        new("pr-author", "m3"),
    };

    [Fact]
    public void From_ProjectsDerivedStageStatus()
    {
        var state = State(status: "running", currentPhase: "test-runner", stages: Pipeline);

        var response = RunResponse.From(state);

        Assert.Equal(RunId, response.RunId);
        Assert.Equal("owner/repo", response.Repo);
        Assert.Equal("running", response.Status);
        Assert.Equal("test-runner", response.CurrentPhase);
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
        var state = State(status: "running", currentPhase: "test-runner", stages: Pipeline, steps: null);

        var response = RunResponse.From(state);

        Assert.NotNull(response.Steps);
        Assert.Empty(response.Steps);
        Assert.Equal(3, response.Stages.Count);
        Assert.Equal("running", response.Stages[1].Status);
    }
}
