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
        IReadOnlyList<RunStage>? stages = null) =>
        new(RunId, "owner/repo", "main", "spec", "glm-5.2", "vm-1", status, T0, null, T0, "https://pr", null, false, null, T0, stages, currentPhase);

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
}
