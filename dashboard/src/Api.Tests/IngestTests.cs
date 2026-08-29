using Api.Runs;
using Xunit;

namespace Api.Tests;

public class IngestTests
{
    private static readonly Guid RunId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset T0 = new(2024, 2, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddMinutes(1);
    private static readonly DateTimeOffset T2 = T0.AddMinutes(2);

    private static RunEvent E(string type, DateTimeOffset? at = null, Func<RunEvent, RunEvent>? configure = null)
    {
        var ev = new RunEvent(type, at, null, null, null, null, null, null, null, null) { RunId = RunId };
        return configure?.Invoke(ev) ?? ev;
    }

    [Fact]
    public async Task Apply_Start_CreatesRun()
    {
        var store = new FakeRunStore();

        var state = await store.Apply(RunId, E("run-started", T1, e => e with { Repo = "r", Branch = "b", Spec = "s", Model = "m" }));

        Assert.NotNull(state);
        Assert.Equal(RunId, state.RunId);
        Assert.Equal("r", state.Repo);
        Assert.Equal("b", state.Branch);
        Assert.Equal("s", state.Spec);
        Assert.Equal("m", state.Model);
        Assert.Equal("launching", state.Status);
        Assert.Equal(T1, state.StartedAt);
        Assert.Equal(T1, state.UpdatedAt);

        var fromStore = await store.Get(RunId);
        Assert.NotNull(fromStore);
        Assert.Equal(state, fromStore);
    }

    [Fact]
    public async Task Apply_HeartbeatOnly_UpdatesHeartbeatButNotUpdatedAt()
    {
        var store = new FakeRunStore();
        await store.Apply(RunId, E("run-started", T0, e => e with { Repo = "r" }));

        var state = await store.Apply(RunId, E("heartbeat", T1));

        Assert.NotNull(state);
        Assert.Equal(T1, state.LastHeartbeatAt);
        Assert.Equal(T0, state.UpdatedAt);
        Assert.Equal("launching", state.Status);
    }

    [Fact]
    public async Task Apply_OutOfOrderEvent_IsNoOp()
    {
        var store = new FakeRunStore();
        await store.Apply(RunId, E("run-started", T0, e => e with { Repo = "r" }));
        await store.Apply(RunId, E("agent-started", T2, e => e with { VmName = "vm-1" }));

        var state = await store.Apply(RunId, E("agent-started", T1, e => e with { VmName = "vm-2" }));

        Assert.NotNull(state);
        Assert.Equal("vm-1", state.VmName);
        Assert.Equal(T2, state.UpdatedAt);
        Assert.Equal("running", state.Status);
    }

    [Fact]
    public async Task Apply_NormalUpdate_TransitionsRun()
    {
        var store = new FakeRunStore();
        await store.Apply(RunId, E("run-started", T0, e => e with { Repo = "r" }));

        var state = await store.Apply(RunId, E("agent-started", T1, e => e with { VmName = "vm-1" }));

        Assert.NotNull(state);
        Assert.Equal("running", state.Status);
        Assert.Equal("vm-1", state.VmName);
        Assert.Equal(T1, state.UpdatedAt);
    }

    [Fact]
    public async Task Apply_Start_PersistsStages()
    {
        var store = new FakeRunStore();
        var stages = new[]
        {
            new RunStage("feature-builder", "m1"),
            new RunStage("test-runner", "m2"),
            new RunStage("pr-author", "m3"),
        };

        var state = await store.Apply(RunId, E("run-started", T1, e => e with { Repo = "r", Stages = stages }));

        Assert.NotNull(state);
        Assert.NotNull(state.Stages);
        Assert.Equal(3, state.Stages.Count);
        Assert.Equal("feature-builder", state.Stages[0].Agent);
        Assert.Equal("m1", state.Stages[0].Model);

        var fromStore = await store.Get(RunId);
        Assert.NotNull(fromStore);
        Assert.NotNull(fromStore.Stages);
        Assert.Equal(3, fromStore.Stages.Count);
        Assert.Equal("pr-author", fromStore.Stages[2].Agent);
        Assert.Equal("m3", fromStore.Stages[2].Model);
    }

    [Fact]
    public async Task Apply_Heartbeat_PersistsCurrentPhase()
    {
        var store = new FakeRunStore();
        await store.Apply(RunId, E("run-started", T0, e => e with { Repo = "r" }));

        var state = await store.Apply(RunId, E("heartbeat", T1, e => e with { CurrentPhase = "feature-builder" }));

        Assert.NotNull(state);
        Assert.Equal("feature-builder", state.CurrentPhase);
        Assert.Equal(T1, state.LastHeartbeatAt);

        var fromStore = await store.Get(RunId);
        Assert.NotNull(fromStore);
        Assert.Equal("feature-builder", fromStore.CurrentPhase);
    }

    [Fact]
    public async Task Apply_Heartbeat_AdvancesCurrentPhaseThroughStore()
    {
        var store = new FakeRunStore();
        await store.Apply(RunId, E("run-started", T0, e => e with { Repo = "r" }));
        await store.Apply(RunId, E("heartbeat", T1, e => e with { CurrentPhase = "feature-builder" }));

        var state = await store.Apply(RunId, E("heartbeat", T2, e => e with { CurrentPhase = "test-runner" }));

        Assert.NotNull(state);
        Assert.Equal("test-runner", state.CurrentPhase);

        var fromStore = await store.Get(RunId);
        Assert.NotNull(fromStore);
        Assert.Equal("test-runner", fromStore.CurrentPhase);
    }
}
