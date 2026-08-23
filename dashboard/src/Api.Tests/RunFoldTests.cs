using Api.Folding;
using Api.Models;
using Xunit;

namespace Api.Tests;

public class RunFoldTests
{
    private static readonly Guid RunId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddMinutes(1);
    private static readonly DateTimeOffset T2 = T0.AddMinutes(2);

    private static RunEvent E(string type, DateTimeOffset? at = null, Func<RunEvent, RunEvent>? configure = null)
    {
        var ev = new RunEvent(type, at, null, null, null, null, null, null, null, null) { RunId = RunId };
        return configure?.Invoke(ev) ?? ev;
    }

    private static RunState State(string status = "launching", DateTimeOffset? updatedAt = null, DateTimeOffset? lastHeartbeatAt = null) =>
        new(RunId, "repo", "branch", "spec", "model", null, status, T0, null, lastHeartbeatAt, null, null, false, null, updatedAt ?? T0);

    [Fact]
    public void RunStarted_CreatesNewRun()
    {
        var next = RunFold.Apply(null, E("run-started", T1, e => e with { Repo = "r", Branch = "b", Spec = "s", Model = "m" }));

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
    }

    [Fact]
    public void RunStarted_OnExistingRun_IsNoOp()
    {
        var current = State();

        var next = RunFold.Apply(current, E("run-started", T1, e => e with { Repo = "r" }));

        Assert.Null(next);
    }

    [Fact]
    public void AgentStarted_TransitionsLaunchingToRunning()
    {
        var current = State("launching");

        var next = RunFold.Apply(current, E("agent-started", T1, e => e with { VmName = "vm-1" }));

        Assert.NotNull(next);
        Assert.Equal("running", next.Status);
        Assert.Equal("vm-1", next.VmName);
        Assert.Equal(T1, next.UpdatedAt);
    }

    [Fact]
    public void AgentStarted_OutOfOrder_IsNoOp()
    {
        var current = State("running", updatedAt: T2);

        var next = RunFold.Apply(current, E("agent-started", T1, e => e with { VmName = "vm-1" }));

        Assert.Null(next);
    }

    [Theory]
    [InlineData("done")]
    [InlineData("failed")]
    public void AgentStarted_AfterTerminal_IsNoOp(string terminalStatus)
    {
        var current = State(terminalStatus);

        var next = RunFold.Apply(current, E("agent-started", T1, e => e with { VmName = "vm-1" }));

        Assert.Null(next);
    }

    [Fact]
    public void Heartbeat_UpdatesLastHeartbeatAt()
    {
        var current = State("running", updatedAt: T0);

        var next = RunFold.Apply(current, E("heartbeat", T1));

        Assert.NotNull(next);
        Assert.Equal(T1, next.LastHeartbeatAt);
        Assert.Equal(T0, next.UpdatedAt); // unchanged
        Assert.Equal("running", next.Status); // unchanged
    }

    [Fact]
    public void Heartbeat_OutOfOrder_IsNoOp()
    {
        var current = State("running", updatedAt: T0, lastHeartbeatAt: T2);

        var next = RunFold.Apply(current, E("heartbeat", T1));

        Assert.Null(next);
    }

    [Fact]
    public void Heartbeat_FirstHeartbeat_Works()
    {
        var current = State("running", updatedAt: T0, lastHeartbeatAt: null);

        var next = RunFold.Apply(current, E("heartbeat", T1));

        Assert.NotNull(next);
        Assert.Equal(T1, next.LastHeartbeatAt);
    }

    [Fact]
    public void StallDetected_TransitionsToStalled()
    {
        var current = State("running");

        var next = RunFold.Apply(current, E("stall-detected", T1, e => e with { FailureReason = "stuck" }));

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

        var next = RunFold.Apply(current, E("stall-detected", T1, e => e with { FailureReason = "stuck" }));

        Assert.Null(next);
    }

    [Fact]
    public void StallDetected_OutOfOrder_IsNoOp()
    {
        var current = State("running", updatedAt: T2);

        var next = RunFold.Apply(current, E("stall-detected", T1));

        Assert.Null(next);
    }

    [Fact]
    public void FreezeCaptured_SetsFreezeFlagAndPath()
    {
        var current = State("running");

        var next = RunFold.Apply(current, E("freeze-captured", T1, e => e with { FreezeLocalPath = "/freeze" }));

        Assert.NotNull(next);
        Assert.True(next.FreezeCaptured);
        Assert.Equal("/freeze", next.FreezeLocalPath);
        Assert.Equal(T1, next.UpdatedAt);
    }

    [Fact]
    public void FreezeCaptured_OutOfOrder_IsNoOp()
    {
        var current = State("running", updatedAt: T2);

        var next = RunFold.Apply(current, E("freeze-captured", T1, e => e with { FreezeLocalPath = "/freeze" }));

        Assert.Null(next);
    }

    [Fact]
    public void PrVerified_SetsPrUrl()
    {
        var current = State("running");

        var next = RunFold.Apply(current, E("pr-verified", T1, e => e with { PrUrl = "https://pr" }));

        Assert.NotNull(next);
        Assert.Equal("https://pr", next.PrUrl);
        Assert.Equal(T1, next.UpdatedAt);
    }

    [Fact]
    public void PrVerified_OutOfOrder_IsNoOp()
    {
        var current = State("running", updatedAt: T2);

        var next = RunFold.Apply(current, E("pr-verified", T1, e => e with { PrUrl = "https://pr" }));

        Assert.Null(next);
    }

    [Fact]
    public void RunFinished_TransitionsToDone()
    {
        var current = State("running");

        var next = RunFold.Apply(current, E("run-finished", T1));

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

        var next = RunFold.Apply(current, E("run-finished", T1));

        Assert.Null(next);
    }

    [Fact]
    public void RunFinished_OutOfOrder_IsNoOp()
    {
        var current = State("running", updatedAt: T2);

        var next = RunFold.Apply(current, E("run-finished", T1));

        Assert.Null(next);
    }

    [Fact]
    public void RunFailed_TransitionsToFailed()
    {
        var current = State("running");

        var next = RunFold.Apply(current, E("run-failed", T1, e => e with { FailureReason = "oops" }));

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

        var next = RunFold.Apply(current, E("run-failed", T1, e => e with { FailureReason = "oops" }));

        Assert.Null(next);
    }

    [Fact]
    public void RunFailed_OutOfOrder_IsNoOp()
    {
        var current = State("running", updatedAt: T2);

        var next = RunFold.Apply(current, E("run-failed", T1, e => e with { FailureReason = "oops" }));

        Assert.Null(next);
    }

    [Fact]
    public void UnknownEvent_IsNoOp()
    {
        var current = State("running");

        var next = RunFold.Apply(current, E("unknown-event", T1));

        Assert.Null(next);
    }
}
