using Api.Runs;
using Xunit;

namespace Api.Tests;

public class RunResumeTests
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

    [Fact]
    public void DeriveResumeStage_ReviewLoopFailure_ResumesReviewLoop()
    {
        Assert.Equal("review-loop", RunResume.DeriveResumeStage(Pipeline, "review-loop"));
    }

    [Fact]
    public void DeriveResumeStage_QualityLoopFailure_ResumesStaticLoop()
    {
        Assert.Equal("static-loop", RunResume.DeriveResumeStage(Pipeline, "static-loop"));
    }

    [Fact]
    public void DeriveResumeStage_LateFailure_ResumesThatStage()
    {
        Assert.Equal("pr-author", RunResume.DeriveResumeStage(Pipeline, "pr-author"));
        Assert.Equal("test-rerun", RunResume.DeriveResumeStage(Pipeline, "test-rerun"));
    }

    [Fact]
    public void DeriveResumeStage_ImplementCrash_MapsToEarliestLoop()
    {
        // No category ever reported: the guest always skips implement on a
        // present branch, so the resume enters at the earliest loop.
        Assert.Equal("review-loop", RunResume.DeriveResumeStage(Pipeline, null));
        Assert.Equal("review-loop", RunResume.DeriveResumeStage(Pipeline, ""));
    }

    [Fact]
    public void DeriveResumeStage_ImplementStageFailure_ResumesImplementation()
    {
        Assert.Equal("implementation", RunResume.DeriveResumeStage(Pipeline, "implementation"));
    }

    [Fact]
    public void DeriveResumeStage_NullOrEmptyStages_ReturnsNull()
    {
        Assert.Null(RunResume.DeriveResumeStage(null, "review-loop"));
        Assert.Null(RunResume.DeriveResumeStage(Array.Empty<RunStage>(), "review-loop"));
    }

    [Fact]
    public void DeriveResumeStage_UsesStageOrderNotHardcodedNames()
    {
        var renamed = new[]
        {
            new RunStage("a", "m", "alpha", "sequential"),
            new RunStage("b", "m", "beta", "loop"),
        };

        Assert.Equal("beta", RunResume.DeriveResumeStage(renamed, "beta"));
        Assert.Equal("beta", RunResume.DeriveResumeStage(renamed, null));
    }

    [Fact]
    public void DeriveResumeStage_LegacyQualityLoopLabel_ResolvesToStaticLoop()
    {
        Assert.Equal("static-loop", RunResume.DeriveResumeStage(Pipeline, "quality-loop"));
    }

    [Fact]
    public void DeriveResumeStage_ExactMatchWinsOverLegacyAlias()
    {
        var legacy = new[]
        {
            new RunStage("quality-loop", "m", "quality-loop", "loop"),
        };

        Assert.Equal("quality-loop", RunResume.DeriveResumeStage(legacy, "quality-loop"));
    }
}
