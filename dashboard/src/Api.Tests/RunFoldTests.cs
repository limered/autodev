using Api.Runs;
using Xunit;

namespace Api.Tests;

public class RunFoldTests
{
    private static readonly Guid RunId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddMinutes(1);
    private static readonly DateTimeOffset T2 = T0.AddMinutes(2);

    private static RunState State(string status = "launching", DateTimeOffset? updatedAt = null, DateTimeOffset? lastHeartbeatAt = null, string? currentPhase = null, string? currentCategory = null) =>
        new(RunId, "repo", "branch", "spec", "model", null, status, T0, null, lastHeartbeatAt, null, null, false, null, updatedAt ?? T0, null, currentPhase, null, currentCategory);

    [Fact]
    public void RunStarted_CreatesNewRun()
    {
        var next = RunFold.Apply(null, new RunStartedEvent("r", "b", "s", "m") { At = T1, RunId = RunId });

        Assert.NotNull(next);
        Assert.Equal(RunId, next.RunId);
        Assert.Equal("r", next.Repo);
        Assert.Equal("b", next.Branch);
        Assert.Equal("s", next.Spec);
        Assert.Equal("m", next.Model);
        Assert.Equal("launching", next.Status);
        Assert.Equal(T1, next.StartedAt);
        Assert.Equal(T1, next.UpdatedAt);
        Assert.Null(next.VmName);
        Assert.Null(next.FinishedAt);
        Assert.Null(next.LastHeartbeatAt);
        Assert.Null(next.PrUrl);
        Assert.Null(next.FailureReason);
        Assert.False(next.FreezeCaptured);
        Assert.Null(next.FreezeLocalPath);
        Assert.Null(next.Stages);
        Assert.Null(next.CurrentPhase);
    }

    [Fact]
    public void RunStarted_CarriesStages()
    {
        var stages = new[]
        {
            new RunStage("feature-builder", "opencode-go/glm-5.2"),
            new RunStage("test-runner", "opencode-go/kimi-k2.7-code"),
            new RunStage("pr-author", "opencode-go/kimi-k2.7-code"),
        };

        var next = RunFold.Apply(null, new RunStartedEvent(Repo: "r", Stages: stages) { At = T1, RunId = RunId });

        Assert.NotNull(next);
        Assert.NotNull(next.Stages);
        Assert.Equal(3, next.Stages.Count);
        Assert.Equal("feature-builder", next.Stages[0].Agent);
        Assert.Equal("opencode-go/glm-5.2", next.Stages[0].Model);
        Assert.Equal("test-runner", next.Stages[1].Agent);
        Assert.Equal("opencode-go/kimi-k2.7-code", next.Stages[1].Model);
        Assert.Equal("pr-author", next.Stages[2].Agent);
        Assert.Equal("opencode-go/kimi-k2.7-code", next.Stages[2].Model);
    }

    [Fact]
    public void RunStarted_CarriesStageCategories()
    {
        var stages = new[]
        {
            new RunStage("feature-builder", "opencode-go/glm-5.2", "feature-builder"),
            new RunStage("quality-loop", "opencode-go/kimi-k2.7-code", "quality-loop"),
        };

        var next = RunFold.Apply(null, new RunStartedEvent(Repo: "r", Stages: stages) { At = T1, RunId = RunId });

        Assert.NotNull(next);
        Assert.NotNull(next.Stages);
        Assert.Equal(2, next.Stages.Count);
        Assert.Equal("quality-loop", next.Stages[1].Category);
        Assert.Equal("quality-loop", next.Stages[1].Agent);
        Assert.Null(next.CurrentCategory);
    }

    [Fact]
    public void RunStarted_CarriesStageTypes()
    {
        var stages = new[]
        {
            new RunStage("implementation", "m1", "implementation", "sequential"),
            new RunStage("quality-loop", "m2", "quality-loop", "loop"),
        };

        var next = RunFold.Apply(null, new RunStartedEvent(Repo: "r", Stages: stages) { At = T1, RunId = RunId });

        Assert.NotNull(next);
        Assert.NotNull(next.Stages);
        Assert.Equal(2, next.Stages.Count);
        Assert.Equal("sequential", next.Stages[0].Type);
        Assert.Equal("loop", next.Stages[1].Type);
    }

    [Fact]
    public void RunStarted_OnExistingRun_IsNoOp()
    {
        var current = State();

        var next = RunFold.Apply(current, new RunStartedEvent(Repo: "r") { At = T1, RunId = RunId });

        Assert.Null(next);
    }

    [Fact]
    public void AgentStarted_TransitionsLaunchingToRunning()
    {
        var current = State("launching");

        var next = RunFold.Apply(current, new AgentStartedEvent("vm-1") { At = T1, RunId = RunId });

        Assert.NotNull(next);
        Assert.Equal("running", next.Status);
        Assert.Equal("vm-1", next.VmName);
        Assert.Equal(T1, next.UpdatedAt);
    }

    [Fact]
    public void AgentStarted_OutOfOrder_IsNoOp()
    {
        var current = State("running", updatedAt: T2);

        var next = RunFold.Apply(current, new AgentStartedEvent("vm-1") { At = T1, RunId = RunId });

        Assert.Null(next);
    }

    [Theory]
    [InlineData("done")]
    [InlineData("failed")]
    public void AgentStarted_AfterTerminal_IsNoOp(string terminalStatus)
    {
        var current = State(terminalStatus);

        var next = RunFold.Apply(current, new AgentStartedEvent("vm-1") { At = T1, RunId = RunId });

        Assert.Null(next);
    }

    [Fact]
    public void Heartbeat_UpdatesLastHeartbeatAt()
    {
        var current = State("running", updatedAt: T0);

        var next = RunFold.Apply(current, new HeartbeatEvent { At = T1, RunId = RunId });

        Assert.NotNull(next);
        Assert.Equal(T1, next.LastHeartbeatAt);
        Assert.Equal(T0, next.UpdatedAt); // unchanged
        Assert.Equal("running", next.Status); // unchanged
    }

    [Fact]
    public void Heartbeat_OutOfOrder_IsNoOp()
    {
        var current = State("running", updatedAt: T0, lastHeartbeatAt: T2);

        var next = RunFold.Apply(current, new HeartbeatEvent { At = T1, RunId = RunId });

        Assert.Null(next);
    }

    [Fact]
    public void Heartbeat_FirstHeartbeat_Works()
    {
        var current = State("running", updatedAt: T0, lastHeartbeatAt: null);

        var next = RunFold.Apply(current, new HeartbeatEvent { At = T1, RunId = RunId });

        Assert.NotNull(next);
        Assert.Equal(T1, next.LastHeartbeatAt);
    }

    [Fact]
    public void Heartbeat_CarriesCurrentPhase()
    {
        var current = State("running", updatedAt: T0, currentPhase: null);

        var next = RunFold.Apply(current, new HeartbeatEvent("feature-builder") { At = T1, RunId = RunId });

        Assert.NotNull(next);
        Assert.Equal(T1, next.LastHeartbeatAt);
        Assert.Equal("feature-builder", next.CurrentPhase);
        Assert.Equal(T0, next.UpdatedAt); // heartbeat never bumps UpdatedAt
        Assert.Equal("running", next.Status); // unchanged
    }

    [Fact]
    public void Heartbeat_AdvancesCurrentPhase()
    {
        var current = State("running", updatedAt: T0, lastHeartbeatAt: T0, currentPhase: "feature-builder");

        var next = RunFold.Apply(current, new HeartbeatEvent("test-runner") { At = T1, RunId = RunId });

        Assert.NotNull(next);
        Assert.Equal("test-runner", next.CurrentPhase);
        Assert.Equal(T1, next.LastHeartbeatAt);
    }

    [Fact]
    public void Heartbeat_WithoutCurrentPhase_KeepsExistingPhase()
    {
        var current = State("running", updatedAt: T0, lastHeartbeatAt: T0, currentPhase: "feature-builder");

        var next = RunFold.Apply(current, new HeartbeatEvent { At = T1, RunId = RunId });

        Assert.NotNull(next);
        Assert.Equal("feature-builder", next.CurrentPhase);
        Assert.Equal(T1, next.LastHeartbeatAt);
    }

    [Fact]
    public void Heartbeat_FirstHeartbeat_CarriesCurrentPhase()
    {
        var current = State("running", updatedAt: T0, lastHeartbeatAt: null, currentPhase: null);

        var next = RunFold.Apply(current, new HeartbeatEvent("feature-builder") { At = T1, RunId = RunId });

        Assert.NotNull(next);
        Assert.Equal("feature-builder", next.CurrentPhase);
        Assert.Equal(T1, next.LastHeartbeatAt);
    }

    [Fact]
    public void Heartbeat_CarriesCurrentCategory()
    {
        var current = State("running", updatedAt: T0, currentPhase: "static-analysis", currentCategory: null);

        var next = RunFold.Apply(current, new HeartbeatEvent("static-analysis", "quality-loop") { At = T1, RunId = RunId });

        Assert.NotNull(next);
        Assert.Equal(T1, next.LastHeartbeatAt);
        Assert.Equal("static-analysis", next.CurrentPhase);
        Assert.Equal("quality-loop", next.CurrentCategory);
        Assert.Equal(T0, next.UpdatedAt); // heartbeat never bumps UpdatedAt
    }

    [Fact]
    public void Heartbeat_AdvancesCurrentCategory()
    {
        var current = State("running", updatedAt: T0, lastHeartbeatAt: T0, currentPhase: "feature-builder", currentCategory: "feature-builder");

        var next = RunFold.Apply(current, new HeartbeatEvent("static-analysis", "quality-loop") { At = T1, RunId = RunId });

        Assert.NotNull(next);
        Assert.Equal("quality-loop", next.CurrentCategory);
        Assert.Equal("static-analysis", next.CurrentPhase);
    }

    [Fact]
    public void Heartbeat_WithoutCurrentCategory_KeepsExistingCategory()
    {
        var current = State("running", updatedAt: T0, lastHeartbeatAt: T0, currentPhase: "static-analysis", currentCategory: "quality-loop");

        var next = RunFold.Apply(current, new HeartbeatEvent("static-analysis") { At = T1, RunId = RunId });

        Assert.NotNull(next);
        Assert.Equal("quality-loop", next.CurrentCategory);
        Assert.Equal(T1, next.LastHeartbeatAt);
    }

    [Theory]
    [InlineData("done")]
    [InlineData("failed")]
    public void Heartbeat_AfterTerminal_StillApplies(string terminalStatus)
    {
        var current = State(terminalStatus, updatedAt: T0, lastHeartbeatAt: T0);

        var next = RunFold.Apply(current, new HeartbeatEvent { At = T1, RunId = RunId });

        Assert.NotNull(next);
        Assert.Equal(T1, next.LastHeartbeatAt);
    }

    [Theory]
    [InlineData("done")]
    [InlineData("failed")]
    public void FreezeCaptured_AfterTerminal_StillApplies(string terminalStatus)
    {
        var current = State(terminalStatus);

        var next = RunFold.Apply(current, new FreezeCapturedEvent("/freeze") { At = T1, RunId = RunId });

        Assert.NotNull(next);
        Assert.True(next.FreezeCaptured);
    }

    [Theory]
    [InlineData("done")]
    [InlineData("failed")]
    public void PrVerified_AfterTerminal_StillApplies(string terminalStatus)
    {
        var current = State(terminalStatus);

        var next = RunFold.Apply(current, new PrVerifiedEvent("https://pr") { At = T1, RunId = RunId });

        Assert.NotNull(next);
        Assert.Equal("https://pr", next.PrUrl);
    }

    [Theory]
    [InlineData("launching", true)]
    [InlineData("running", true)]
    [InlineData("stalled", true)]
    [InlineData("done", false)]
    [InlineData("failed", false)]
    public void ActiveAndTerminal_Predicates_Agree(string status, bool active)
    {
        Assert.Equal(active, RunFold.IsActiveStatus(status));
        Assert.Equal(!active, RunFold.IsTerminal(status));
    }

    [Fact]
    public void StallDetected_TransitionsToStalled()
    {
        var current = State("running");

        var next = RunFold.Apply(current, new StallDetectedEvent("stuck") { At = T1, RunId = RunId });

        Assert.NotNull(next);
        Assert.Equal("stalled", next.Status);
        Assert.Equal("stuck", next.FailureReason);
        Assert.Equal(T1, next.UpdatedAt);
    }

    [Theory]
    [InlineData("done")]
    [InlineData("failed")]
    public void StallDetected_AfterTerminal_IsNoOp(string terminalStatus)
    {
        var current = State(terminalStatus);

        var next = RunFold.Apply(current, new StallDetectedEvent("stuck") { At = T1, RunId = RunId });

        Assert.Null(next);
    }

    [Fact]
    public void StallDetected_OutOfOrder_IsNoOp()
    {
        var current = State("running", updatedAt: T2);

        var next = RunFold.Apply(current, new StallDetectedEvent { At = T1, RunId = RunId });

        Assert.Null(next);
    }

    [Fact]
    public void FreezeCaptured_SetsFreezeFlagAndPath()
    {
        var current = State("running");

        var next = RunFold.Apply(current, new FreezeCapturedEvent("/freeze") { At = T1, RunId = RunId });

        Assert.NotNull(next);
        Assert.True(next.FreezeCaptured);
        Assert.Equal("/freeze", next.FreezeLocalPath);
        Assert.Equal(T1, next.UpdatedAt);
    }

    [Fact]
    public void FreezeCaptured_OutOfOrder_IsNoOp()
    {
        var current = State("running", updatedAt: T2);

        var next = RunFold.Apply(current, new FreezeCapturedEvent("/freeze") { At = T1, RunId = RunId });

        Assert.Null(next);
    }

    [Fact]
    public void PrVerified_SetsPrUrl()
    {
        var current = State("running");

        var next = RunFold.Apply(current, new PrVerifiedEvent("https://pr") { At = T1, RunId = RunId });

        Assert.NotNull(next);
        Assert.Equal("https://pr", next.PrUrl);
        Assert.Equal(T1, next.UpdatedAt);
    }

    [Fact]
    public void PrVerified_OutOfOrder_IsNoOp()
    {
        var current = State("running", updatedAt: T2);

        var next = RunFold.Apply(current, new PrVerifiedEvent("https://pr") { At = T1, RunId = RunId });

        Assert.Null(next);
    }

    [Fact]
    public void RunFinished_TransitionsToDone()
    {
        var current = State("running");

        var next = RunFold.Apply(current, new RunFinishedEvent { At = T1, RunId = RunId });

        Assert.NotNull(next);
        Assert.Equal("done", next.Status);
        Assert.Equal(T1, next.FinishedAt);
        Assert.Equal(T1, next.UpdatedAt);
    }

    [Theory]
    [InlineData("done")]
    [InlineData("failed")]
    public void RunFinished_AfterTerminal_IsNoOp(string terminalStatus)
    {
        var current = State(terminalStatus);

        var next = RunFold.Apply(current, new RunFinishedEvent { At = T1, RunId = RunId });

        Assert.Null(next);
    }

    [Fact]
    public void RunFinished_OutOfOrder_IsNoOp()
    {
        var current = State("running", updatedAt: T2);

        var next = RunFold.Apply(current, new RunFinishedEvent { At = T1, RunId = RunId });

        Assert.Null(next);
    }

    [Fact]
    public void RunFailed_TransitionsToFailed()
    {
        var current = State("running");

        var next = RunFold.Apply(current, new RunFailedEvent("oops") { At = T1, RunId = RunId });

        Assert.NotNull(next);
        Assert.Equal("failed", next.Status);
        Assert.Equal(T1, next.FinishedAt);
        Assert.Equal("oops", next.FailureReason);
        Assert.Equal(T1, next.UpdatedAt);
    }

    [Theory]
    [InlineData("done")]
    [InlineData("failed")]
    public void RunFailed_AfterTerminal_IsNoOp(string terminalStatus)
    {
        var current = State(terminalStatus);

        var next = RunFold.Apply(current, new RunFailedEvent("oops") { At = T1, RunId = RunId });

        Assert.Null(next);
    }

    [Fact]
    public void RunFailed_OutOfOrder_IsNoOp()
    {
        var current = State("running", updatedAt: T2);

        var next = RunFold.Apply(current, new RunFailedEvent("oops") { At = T1, RunId = RunId });

        Assert.Null(next);
    }

    [Fact]
    public void UnknownEvent_IsNoOp()
    {
        // An unmapped wire type binds to the bare base event; the fold ignores it.
        var current = State("running");

        var next = RunFold.Apply(current, new RunEvent { At = T1, RunId = RunId });

        Assert.Null(next);
    }

    [Fact]
    public void PhaseFinished_StoresStep()
    {
        var current = State("running");

        var next = RunFold.Apply(current, new PhaseFinishedEvent(
            Agent: "feature-builder",
            Iteration: 0,
            DurationMs: 61000,
            InputTokens: 100,
            OutputTokens: 50,
            Cost: 0.0123m,
            Status: "done",
            Model: "opencode-go/glm-5.2")
        { At = T1, RunId = RunId });

        Assert.NotNull(next);
        Assert.Equal("running", next.Status); // steps never touch run Status
        Assert.Equal(T1, next.UpdatedAt);
        var step = Assert.Single(next.Steps!);
        Assert.Equal("feature-builder", step.Agent);
        Assert.Equal(0, step.Iteration);
        Assert.Equal(61000, step.DurationMs);
        Assert.Equal(100, step.InputTokens);
        Assert.Equal(50, step.OutputTokens);
        Assert.Equal(0.0123m, step.Cost);
        Assert.Equal("done", step.Status);
        Assert.Equal("opencode-go/glm-5.2", step.Model);
    }

    [Fact]
    public void PhaseFinished_StoresCategoryForPostRunGrouping()
    {
        var current = State("running");

        var next = RunFold.Apply(current, new PhaseFinishedEvent(
            Agent: "static-analysis",
            Iteration: 1,
            DurationMs: 2000,
            InputTokens: 10,
            OutputTokens: 5,
            Status: "done",
            Model: "m",
            Category: "quality-loop")
        { At = T1, RunId = RunId });

        Assert.NotNull(next);
        Assert.Equal("running", next.Status); // detail never feeds liveness
        Assert.Equal("quality-loop", Assert.Single(next.Steps!).Category);
    }

    [Fact]
    public void PhaseFinished_WithoutCategory_StoresNull()
    {
        // Steps relayed before categories carry none; the dashboard groups
        // them under the uncategorized bucket and the lights never notice.
        var current = State("running");

        var next = RunFold.Apply(current, new PhaseFinishedEvent(
            Agent: "feature-builder",
            Iteration: 0,
            DurationMs: 61000,
            InputTokens: 100,
            OutputTokens: 50,
            Status: "done",
            Model: "m")
        { At = T1, RunId = RunId });

        Assert.NotNull(next);
        Assert.Null(Assert.Single(next.Steps!).Category);
    }

    [Fact]
    public void PhaseFinished_ReEmit_ReplacesInPlace_LastWriteWins()
    {
        var current = State("running");
        var first = RunFold.Apply(current, new PhaseFinishedEvent(
            Agent: "test-runner", Iteration: 0, DurationMs: 1000,
            InputTokens: 10, OutputTokens: 5, Status: "done",
            Model: "m1")
        { At = T1, RunId = RunId });
        Assert.NotNull(first);
        var second = RunFold.Apply(first, new PhaseFinishedEvent(
            Agent: "pr-author", Iteration: 0, DurationMs: 2000,
            InputTokens: 20, OutputTokens: 10, Status: "done",
            Model: "m2")
        { At = T1.AddSeconds(1), RunId = RunId });
        Assert.NotNull(second);

        var next = RunFold.Apply(second, new PhaseFinishedEvent(
            Agent: "test-runner", Iteration: 0, DurationMs: 3000,
            InputTokens: 30, OutputTokens: 15, Status: "done",
            Model: "m1")
        { At = T1.AddSeconds(2), RunId = RunId });

        Assert.NotNull(next);
        Assert.Equal(2, next.Steps!.Count);
        Assert.Equal("test-runner", next.Steps[0].Agent); // position stable, values replaced
        Assert.Equal(3000, next.Steps[0].DurationMs);
        Assert.Equal(30, next.Steps[0].InputTokens);
        Assert.Equal("pr-author", next.Steps[1].Agent);
    }

    [Fact]
    public void PhaseFinished_LoopIterations_CoexistAsSeparateSteps()
    {
        var current = State("running");
        var t = T1;
        RunState? state = current;
        (string agent, int iteration)[] emissions =
        [
            ("static-analysis", 1),
            ("feature-builder", 1),
            ("static-analysis", 2),
            ("feature-builder", 2),
        ];
        foreach (var (agent, iteration) in emissions)
        {
            state = RunFold.Apply(state, new PhaseFinishedEvent(
                Agent: agent, Iteration: iteration, DurationMs: 1000,
                InputTokens: 10, OutputTokens: 5, Status: "done",
                Model: "m")
            { At = t, RunId = RunId });
            Assert.NotNull(state);
            t = t.AddSeconds(1);
        }

        Assert.Equal(4, state!.Steps!.Count);
        Assert.Equal(("static-analysis", 1), (state.Steps[0].Agent, state.Steps[0].Iteration));
        Assert.Equal(("feature-builder", 1), (state.Steps[1].Agent, state.Steps[1].Iteration));
        Assert.Equal(("static-analysis", 2), (state.Steps[2].Agent, state.Steps[2].Iteration));
        Assert.Equal(("feature-builder", 2), (state.Steps[3].Agent, state.Steps[3].Iteration));
    }

    [Fact]
    public void PhaseFinished_FailedStep_DoesNotTouchRunStatus()
    {
        var current = State("running");

        var next = RunFold.Apply(current, new PhaseFinishedEvent(
            Agent: "test-runner", Iteration: 0, DurationMs: 1000,
            InputTokens: 10, OutputTokens: 5, Status: "failed",
            Model: "m")
        { At = T1, RunId = RunId });

        Assert.NotNull(next);
        Assert.Equal("running", next.Status);
        Assert.Null(next.FinishedAt);
        Assert.Equal("failed", Assert.Single(next.Steps!).Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PhaseFinished_MissingModel_IsNoOp(string? model)
    {
        var current = State("running");

        var next = RunFold.Apply(current, new PhaseFinishedEvent(
            Agent: "feature-builder", Iteration: 0, DurationMs: 1000,
            InputTokens: 10, OutputTokens: 5, Status: "done",
            Model: model)
        { At = T1, RunId = RunId });

        Assert.Null(next);
    }

    [Fact]
    public void PhaseFinished_MissingIteration_IsNoOp()
    {
        var current = State("running");

        var next = RunFold.Apply(current, new PhaseFinishedEvent(
            Agent: "feature-builder", DurationMs: 1000,
            InputTokens: 10, OutputTokens: 5, Status: "done",
            Model: "m")
        { At = T1, RunId = RunId });

        Assert.Null(next);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void PhaseFinished_MissingAgent_IsNoOp(string? agent)
    {
        var current = State("running");

        var next = RunFold.Apply(current, new PhaseFinishedEvent(
            Agent: agent, Iteration: 0, DurationMs: 1000,
            InputTokens: 10, OutputTokens: 5, Status: "done",
            Model: "m")
        { At = T1, RunId = RunId });

        Assert.Null(next);
    }

    [Theory]
    [InlineData("done")]
    [InlineData("failed")]
    public void PhaseFinished_AfterTerminal_IsNoOp(string terminalStatus)
    {
        var current = State(terminalStatus);

        var next = RunFold.Apply(current, new PhaseFinishedEvent(
            Agent: "feature-builder", Iteration: 0, DurationMs: 1000,
            InputTokens: 10, OutputTokens: 5, Status: "done",
            Model: "m")
        { At = T1, RunId = RunId });

        Assert.Null(next);
    }

    [Theory]
    [InlineData(null, "done")]
    [InlineData("done", "done")]
    [InlineData("failed", "failed")]
    [InlineData("running", "done")]
    [InlineData("weird", "done")]
    public void PhaseFinished_UnknownStatus_CoercesToDone(string? status, string expected)
    {
        var current = State("running");

        var next = RunFold.Apply(current, new PhaseFinishedEvent(
            Agent: "feature-builder", Iteration: 0, DurationMs: 1000,
            InputTokens: 10, OutputTokens: 5, Status: status,
            Model: "m")
        { At = T1, RunId = RunId });

        Assert.Equal(expected, Assert.Single(next!.Steps!).Status);
    }

    [Fact]
    public void PhaseFinished_OutOfOrder_IsNoOp()
    {
        var current = State("running", updatedAt: T2);

        var next = RunFold.Apply(current, new PhaseFinishedEvent(
            Agent: "feature-builder", Iteration: 0, DurationMs: 1000,
            InputTokens: 10, OutputTokens: 5, Status: "done",
            Model: "m")
        { At = T1, RunId = RunId });

        Assert.Null(next);
    }
}
