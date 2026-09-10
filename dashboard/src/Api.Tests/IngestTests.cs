using System.Text.Json;
using Api.Runs;
using Xunit;

namespace Api.Tests;

/// <summary>
/// The ingest pipeline end to end: wire JSON shaped exactly like Send-FactoryEvent
/// writes it (flat camelCase keyed on "type", unordered keys — a PowerShell hashtable
/// pipes to ConvertTo-Json) → typed <see cref="RunEvent"/> → fold → store.
/// </summary>
public class IngestTests
{
    private static readonly Guid RunId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset T0 = new(2024, 2, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddMinutes(1);
    private static readonly DateTimeOffset T2 = T0.AddMinutes(2);

    // Mirrors the endpoint's ConfigureHttpJsonOptions (web defaults + camelCase). The
    // RunEvent converter attaches itself via [JsonConverter] on the base record.
    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static RunEvent? FromWire(string json) => JsonSerializer.Deserialize<RunEvent>(json, WireOptions);

    // ---------------------------------------------------------------- wire → event → store

    [Fact]
    public async Task FromWire_RunStarted_IngestsNewRun()
    {
        var store = new FakeRunStore();

        var ev = FromWire(
            """{"type":"run-started","at":"2024-02-01T00:01:00Z","repo":"owner/repo","branch":"feat/x","spec":"do the thing","model":"gpt-x","stages":[{"agent":"build","model":"gpt-x"}]}""");

        var started = Assert.IsType<RunStartedEvent>(ev);
        Assert.Equal("owner/repo", started.Repo);
        Assert.Equal("feat/x", started.Branch);
        Assert.Equal("do the thing", started.Spec);
        Assert.Equal("gpt-x", started.Model);
        Assert.NotNull(started.Stages);
        var stage = Assert.Single(started.Stages);
        Assert.Equal("build", stage.Agent);
        Assert.Equal("gpt-x", stage.Model);
        Assert.Equal(T1, started.At);

        var state = await store.Apply(RunId, started);
        Assert.NotNull(state);
        Assert.Equal(RunId, state.RunId);
        Assert.Equal("launching", state.Status);
        Assert.Equal("owner/repo", state.Repo);
        Assert.Equal("feat/x", state.Branch);
        Assert.Equal("do the thing", state.Spec);
        Assert.Equal("gpt-x", state.Model);
        Assert.Equal(T1, state.StartedAt);
        Assert.NotNull(state.Stages);
        Assert.Equal("build", Assert.Single(state.Stages).Agent);
    }

    [Fact]
    public async Task FromWire_AgentStarted_IngestsVmName_TypeNotFirst()
    {
        var store = new FakeRunStore();
        await store.Apply(RunId, new RunStartedEvent("r", "b", "s", "m") { At = T0 });

        // "type" mid-object and a non-UTC "at" (PowerShell hashtable key order is arbitrary).
        var ev = FromWire(
            """{"at":"2024-02-01T02:01:00+02:00","vmName":"vm-1","type":"agent-started"}""");

        var agentStarted = Assert.IsType<AgentStartedEvent>(ev);
        Assert.Equal("vm-1", agentStarted.VmName);
        Assert.Equal(T1, agentStarted.At);

        var state = await store.Apply(RunId, agentStarted);
        Assert.NotNull(state);
        Assert.Equal("running", state.Status);
        Assert.Equal("vm-1", state.VmName);
        Assert.Equal(T1, state.UpdatedAt);
    }

    [Fact]
    public async Task FromWire_Heartbeat_IngestsCurrentPhase_TypeLast()
    {
        var store = new FakeRunStore();
        await store.Apply(RunId, new RunStartedEvent("r") { At = T0 });

        var ev = FromWire(
            """{"currentPhase":"feature-builder","at":"2024-02-01T00:01:00Z","type":"heartbeat"}""");

        var heartbeat = Assert.IsType<HeartbeatEvent>(ev);
        Assert.Equal("feature-builder", heartbeat.CurrentPhase);
        Assert.Null(heartbeat.CurrentCategory);
        Assert.Equal(T1, heartbeat.At);

        var state = await store.Apply(RunId, heartbeat);
        Assert.NotNull(state);
        Assert.Equal(T1, state.LastHeartbeatAt);
        Assert.Equal("feature-builder", state.CurrentPhase);
        Assert.Null(state.CurrentCategory);
        Assert.Equal(T0, state.UpdatedAt); // heartbeat never bumps UpdatedAt
    }

    [Fact]
    public async Task FromWire_Heartbeat_IngestsCurrentCategory()
    {
        var store = new FakeRunStore();
        await store.Apply(RunId, new RunStartedEvent("r") { At = T0 });

        var ev = FromWire(
            """{"currentPhase":"static-analysis","currentCategory":"quality-loop","at":"2024-02-01T00:01:00Z","type":"heartbeat"}""");

        var heartbeat = Assert.IsType<HeartbeatEvent>(ev);
        Assert.Equal("static-analysis", heartbeat.CurrentPhase);
        Assert.Equal("quality-loop", heartbeat.CurrentCategory);

        var state = await store.Apply(RunId, heartbeat);
        Assert.NotNull(state);
        Assert.Equal("static-analysis", state.CurrentPhase);
        Assert.Equal("quality-loop", state.CurrentCategory);
        Assert.Equal(T1, state.LastHeartbeatAt);
    }

    [Fact]
    public async Task FromWire_RunStarted_IngestsStageCategories()
    {
        var store = new FakeRunStore();

        var ev = FromWire(
            """{"type":"run-started","at":"2024-02-01T00:01:00Z","repo":"owner/repo","branch":"feat/x","spec":"do the thing","model":"gpt-x","stages":[{"agent":"quality-loop","model":"gpt-x","category":"quality-loop"}]}""");

        var started = Assert.IsType<RunStartedEvent>(ev);
        Assert.NotNull(started.Stages);
        Assert.Equal("quality-loop", Assert.Single(started.Stages).Category);

        var state = await store.Apply(RunId, started);
        Assert.NotNull(state);
        Assert.Equal("quality-loop", Assert.Single(state.Stages!).Category);
    }

    [Fact]
    public async Task FromWire_RunStarted_LegacyStagesWithoutCategory_IngestNullCategory()
    {
        var store = new FakeRunStore();

        var ev = FromWire(
            """{"type":"run-started","at":"2024-02-01T00:01:00Z","repo":"owner/repo","branch":"feat/x","spec":"do the thing","model":"gpt-x","stages":[{"agent":"build","model":"gpt-x"}]}""");

        var started = Assert.IsType<RunStartedEvent>(ev);
        Assert.Null(Assert.Single(started.Stages!).Category);

        var state = await store.Apply(RunId, started);
        Assert.NotNull(state);
        Assert.Null(Assert.Single(state.Stages!).Category);
    }

    [Fact]
    public async Task FromWire_Heartbeat_WithoutPhase_IngestsNullPhase()
    {
        var store = new FakeRunStore();
        await store.Apply(RunId, new RunStartedEvent("r") { At = T0 });

        var ev = FromWire("""{"type":"heartbeat","at":"2024-02-01T00:01:00Z"}""");

        var heartbeat = Assert.IsType<HeartbeatEvent>(ev);
        Assert.Null(heartbeat.CurrentPhase);

        var state = await store.Apply(RunId, heartbeat);
        Assert.NotNull(state);
        Assert.Equal(T1, state.LastHeartbeatAt);
        Assert.Null(state.CurrentPhase);
    }

    [Fact]
    public async Task FromWire_Heartbeat_IgnoresFieldsNotOnTheRecord()
    {
        var store = new FakeRunStore();
        await store.Apply(RunId, new RunStartedEvent("r") { At = T0 });

        var ev = FromWire(
            """{"type":"heartbeat","at":"2024-02-01T00:01:00Z","vmName":"not-a-heartbeat-field","junk":1}""");

        var heartbeat = Assert.IsType<HeartbeatEvent>(ev);
        Assert.Null(heartbeat.CurrentPhase);

        var state = await store.Apply(RunId, heartbeat);
        Assert.NotNull(state);
        Assert.Null(state.VmName); // the stray vmName never reached the run
        Assert.Equal(T1, state.LastHeartbeatAt);
    }

    [Fact]
    public async Task FromWire_StallDetected_IngestsStall()
    {
        var store = new FakeRunStore();
        await store.Apply(RunId, new RunStartedEvent("r") { At = T0 });
        await store.Apply(RunId, new AgentStartedEvent("vm-1") { At = T0.AddSeconds(10) });

        var ev = FromWire(
            """{"type":"stall-detected","at":"2024-02-01T00:01:00Z","failureReason":"Heartbeat stale for 600s; job appears stalled"}""");

        var stall = Assert.IsType<StallDetectedEvent>(ev);
        Assert.Equal("Heartbeat stale for 600s; job appears stalled", stall.FailureReason);

        var state = await store.Apply(RunId, stall);
        Assert.NotNull(state);
        Assert.Equal("stalled", state.Status);
        Assert.Equal("Heartbeat stale for 600s; job appears stalled", state.FailureReason);
        Assert.Equal(T1, state.UpdatedAt);
    }

    [Fact]
    public async Task FromWire_FreezeCaptured_IngestsFreezePath_TypeMiddle()
    {
        var store = new FakeRunStore();
        await store.Apply(RunId, new RunStartedEvent("r") { At = T0 });

        var ev = FromWire(
            """{"freezeLocalPath":"C:\\freeze\\job-42","type":"freeze-captured","at":"2024-02-01T00:01:00Z"}""");

        var freeze = Assert.IsType<FreezeCapturedEvent>(ev);
        Assert.Equal("C:\\freeze\\job-42", freeze.FreezeLocalPath);

        var state = await store.Apply(RunId, freeze);
        Assert.NotNull(state);
        Assert.True(state.FreezeCaptured);
        Assert.Equal("C:\\freeze\\job-42", state.FreezeLocalPath);
    }

    [Fact]
    public async Task FromWire_PrVerified_IngestsPrUrl()
    {
        var store = new FakeRunStore();
        await store.Apply(RunId, new RunStartedEvent("r") { At = T0 });

        var ev = FromWire(
            """{"prUrl":"https://github.com/owner/repo/pull/7","at":"2024-02-01T00:01:00Z","type":"pr-verified"}""");

        var verified = Assert.IsType<PrVerifiedEvent>(ev);
        Assert.Equal("https://github.com/owner/repo/pull/7", verified.PrUrl);

        var state = await store.Apply(RunId, verified);
        Assert.NotNull(state);
        Assert.Equal("https://github.com/owner/repo/pull/7", state.PrUrl);
    }

    [Fact]
    public async Task FromWire_RunFinished_IngestsDone_TypeLast()
    {
        var store = new FakeRunStore();
        await store.Apply(RunId, new RunStartedEvent("r") { At = T0 });
        await store.Apply(RunId, new AgentStartedEvent("vm-1") { At = T0.AddSeconds(10) });

        var ev = FromWire("""{"at":"2024-02-01T00:01:00Z","type":"run-finished"}""");

        var finished = Assert.IsType<RunFinishedEvent>(ev);
        Assert.Equal(T1, finished.At);

        var state = await store.Apply(RunId, finished);
        Assert.NotNull(state);
        Assert.Equal("done", state.Status);
        Assert.Equal(T1, state.FinishedAt);
    }

    [Fact]
    public async Task FromWire_RunFailed_IngestsFailure()
    {
        var store = new FakeRunStore();
        await store.Apply(RunId, new RunStartedEvent("r") { At = T0 });

        var ev = FromWire(
            """{"failureReason":"agent crashed","type":"run-failed","at":"2024-02-01T00:01:00Z"}""");

        var failed = Assert.IsType<RunFailedEvent>(ev);
        Assert.Equal("agent crashed", failed.FailureReason);

        var state = await store.Apply(RunId, failed);
        Assert.NotNull(state);
        Assert.Equal("failed", state.Status);
        Assert.Equal("agent crashed", state.FailureReason);
        Assert.Equal(T1, state.FinishedAt);
    }

    [Fact]
    public async Task FromWire_PhaseFinished_IngestsStep()
    {
        var store = new FakeRunStore();
        await store.Apply(RunId, new RunStartedEvent("r") { At = T0 });

        var ev = FromWire(
            """{"type":"phase-finished","at":"2024-02-01T00:01:00Z","agent":"feature-builder","iteration":0,"durationMs":61000,"inputTokens":100,"outputTokens":50,"cost":0.0123,"status":"done","model":"opencode-go/glm-5.2"}""");

        var finished = Assert.IsType<PhaseFinishedEvent>(ev);
        Assert.Equal("feature-builder", finished.Agent);
        Assert.Equal(0, finished.Iteration);
        Assert.Equal(61000, finished.DurationMs);
        Assert.Equal(100, finished.InputTokens);
        Assert.Equal(50, finished.OutputTokens);
        Assert.Equal(0.0123m, finished.Cost);
        Assert.Equal("done", finished.Status);
        Assert.Equal("opencode-go/glm-5.2", finished.Model);

        var state = await store.Apply(RunId, finished);
        Assert.NotNull(state);
        Assert.Equal("launching", state.Status); // steps never touch run Status
        var step = Assert.Single(state.Steps!);
        Assert.Equal("feature-builder", step.Agent);
        Assert.Equal(0, step.Iteration);
        Assert.Equal(61000, step.DurationMs);
        Assert.Equal(100, step.InputTokens);
        Assert.Equal(50, step.OutputTokens);
        Assert.Equal("opencode-go/glm-5.2", step.Model);
    }

    [Fact]
    public async Task FromWire_PhaseFinished_WithCategory_IngestsCategory()
    {
        var store = new FakeRunStore();
        await store.Apply(RunId, new RunStartedEvent("r") { At = T0 });

        var ev = FromWire(
            """{"type":"phase-finished","at":"2024-02-01T00:01:00Z","agent":"static-analysis","iteration":1,"durationMs":2000,"inputTokens":10,"outputTokens":5,"status":"done","model":"m","category":"quality-loop"}""");

        var finished = Assert.IsType<PhaseFinishedEvent>(ev);
        Assert.Equal("quality-loop", finished.Category);

        var state = await store.Apply(RunId, finished);
        Assert.NotNull(state);
        Assert.Equal("launching", state.Status); // steps never touch run Status
        Assert.Equal("quality-loop", Assert.Single(state.Steps!).Category);
    }

    [Fact]
    public async Task FromWire_PhaseFinished_WithoutModel_IsDropped()
    {
        var store = new FakeRunStore();
        await store.Apply(RunId, new RunStartedEvent("r") { At = T0 });

        var ev = FromWire(
            """{"type":"phase-finished","at":"2024-02-01T00:01:00Z","agent":"feature-builder","iteration":0,"durationMs":1000,"inputTokens":1,"outputTokens":1,"status":"done"}""");

        var finished = Assert.IsType<PhaseFinishedEvent>(ev);
        Assert.Null(finished.Model);

        var before = await store.GetRun(RunId);
        var state = await store.Apply(RunId, finished);
        Assert.NotNull(state);
        Assert.Equal(before, state); // nothing folded, nothing persisted
    }

    [Fact]
    public async Task FromWire_UnknownType_IngestsNoOp()
    {
        var store = new FakeRunStore();
        await store.Apply(RunId, new RunStartedEvent("r") { At = T0 });
        var before = await store.GetRun(RunId);

        var ev = FromWire("""{"type":"unknown-event","at":"2024-02-01T00:01:00Z"}""");

        // An unmapped discriminator binds to the bare base event, not an error.
        Assert.IsType<RunEvent>(ev);

        var state = await store.Apply(RunId, ev!);
        Assert.NotNull(state);
        Assert.Equal(before, state); // nothing folded, nothing persisted
    }

    [Fact]
    public async Task FromWire_MissingType_IngestsNoOp()
    {
        var store = new FakeRunStore();
        await store.Apply(RunId, new RunStartedEvent("r") { At = T0 });
        var before = await store.GetRun(RunId);

        var ev = FromWire("""{"at":"2024-02-01T00:01:00Z","repo":"no-type-key"}""");

        Assert.IsType<RunEvent>(ev);

        var state = await store.Apply(RunId, ev!);
        Assert.NotNull(state);
        Assert.Equal(before, state);
    }

    // ---------------------------------------------------------------- event → wire (Write keeps the contract round-trippable)

    [Fact]
    public void ToWire_WritesDiscriminatorFirst_AndRoundTrips()
    {
        (RunEvent Event, string Discriminator)[] cases =
        [
            (new RunStartedEvent("r", "b", "s", "m", new[] { new RunStage("build", "gpt-x") }) { At = T1 }, "run-started"),
            (new AgentStartedEvent("vm-1") { At = T1 }, "agent-started"),
            (new HeartbeatEvent("feature-builder") { At = T1 }, "heartbeat"),
            (new HeartbeatEvent("static-analysis", "quality-loop") { At = T1 }, "heartbeat"),
            (new HeartbeatEvent { At = T1 }, "heartbeat"),
            (new StallDetectedEvent("stuck") { At = T1 }, "stall-detected"),
            (new FreezeCapturedEvent("/freeze") { At = T1 }, "freeze-captured"),
            (new PrVerifiedEvent("https://pr") { At = T1 }, "pr-verified"),
            (new PhaseFinishedEvent("feature-builder", 0, 61000, 100, 50, 0.0123m, "done", "m1") { At = T1 }, "phase-finished"),
            (new PhaseFinishedEvent("static-analysis", 1, 2000, 10, 5, null, "done", "m2", "quality-loop") { At = T1 }, "phase-finished"),
            (new RunFinishedEvent { At = T1 }, "run-finished"),
            (new RunFailedEvent("oops") { At = T1 }, "run-failed"),
        ];

        foreach (var (ev, discriminator) in cases)
        {
            var json = JsonSerializer.Serialize(ev, WireOptions);
            Assert.StartsWith($"{{\"type\":\"{discriminator}\",", json);

            var back = JsonSerializer.Deserialize<RunEvent>(json, WireOptions);
            Assert.IsType(ev.GetType(), back);
        }
    }

    [Fact]
    public void ToWire_BaseEvent_HasNoWireForm()
    {
        // The bare base is the unknown-type fallback; it has nothing to serialize.
        Assert.Throws<NotSupportedException>(
            () => JsonSerializer.Serialize<RunEvent>(new RunEvent { At = T1 }, WireOptions));
    }

    // ---------------------------------------------------------------- store-level fold behavior

    [Fact]
    public async Task Apply_Start_CreatesRun()
    {
        var store = new FakeRunStore();

        var state = await store.Apply(RunId, new RunStartedEvent("r", "b", "s", "m") { At = T1 });

        Assert.NotNull(state);
        Assert.Equal(RunId, state.RunId);
        Assert.Equal("r", state.Repo);
        Assert.Equal("b", state.Branch);
        Assert.Equal("s", state.Spec);
        Assert.Equal("m", state.Model);
        Assert.Equal("launching", state.Status);
        Assert.Equal(T1, state.StartedAt);
        Assert.Equal(T1, state.UpdatedAt);

        var fromStore = await store.GetRun(RunId);
        Assert.NotNull(fromStore);
        Assert.Equal(state, fromStore);
    }

    [Fact]
    public async Task Apply_HeartbeatOnly_UpdatesHeartbeatButNotUpdatedAt()
    {
        var store = new FakeRunStore();
        await store.Apply(RunId, new RunStartedEvent("r") { At = T0 });

        var state = await store.Apply(RunId, new HeartbeatEvent { At = T1 });

        Assert.NotNull(state);
        Assert.Equal(T1, state.LastHeartbeatAt);
        Assert.Equal(T0, state.UpdatedAt);
        Assert.Equal("launching", state.Status);
    }

    [Fact]
    public async Task Apply_OutOfOrderEvent_IsNoOp()
    {
        var store = new FakeRunStore();
        await store.Apply(RunId, new RunStartedEvent("r") { At = T0 });
        await store.Apply(RunId, new AgentStartedEvent("vm-1") { At = T2 });

        var state = await store.Apply(RunId, new AgentStartedEvent("vm-2") { At = T1 });

        Assert.NotNull(state);
        Assert.Equal("vm-1", state.VmName);
        Assert.Equal(T2, state.UpdatedAt);
        Assert.Equal("running", state.Status);
    }

    [Fact]
    public async Task Apply_NormalUpdate_TransitionsRun()
    {
        var store = new FakeRunStore();
        await store.Apply(RunId, new RunStartedEvent("r") { At = T0 });

        var state = await store.Apply(RunId, new AgentStartedEvent("vm-1") { At = T1 });

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

        var state = await store.Apply(RunId, new RunStartedEvent("r", Stages: stages) { At = T1 });

        Assert.NotNull(state);
        Assert.NotNull(state.Stages);
        Assert.Equal(3, state.Stages.Count);
        Assert.Equal("feature-builder", state.Stages[0].Agent);
        Assert.Equal("m1", state.Stages[0].Model);

        var fromStore = await store.GetRun(RunId);
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
        await store.Apply(RunId, new RunStartedEvent("r") { At = T0 });

        var state = await store.Apply(RunId, new HeartbeatEvent("feature-builder") { At = T1 });

        Assert.NotNull(state);
        Assert.Equal("feature-builder", state.CurrentPhase);
        Assert.Equal(T1, state.LastHeartbeatAt);

        var fromStore = await store.GetRun(RunId);
        Assert.NotNull(fromStore);
        Assert.Equal("feature-builder", fromStore.CurrentPhase);
    }

    [Fact]
    public async Task Apply_Heartbeat_AdvancesCurrentPhaseThroughStore()
    {
        var store = new FakeRunStore();
        await store.Apply(RunId, new RunStartedEvent("r") { At = T0 });
        await store.Apply(RunId, new HeartbeatEvent("feature-builder") { At = T1 });

        var state = await store.Apply(RunId, new HeartbeatEvent("test-runner") { At = T2 });

        Assert.NotNull(state);
        Assert.Equal("test-runner", state.CurrentPhase);

        var fromStore = await store.GetRun(RunId);
        Assert.NotNull(fromStore);
        Assert.Equal("test-runner", fromStore.CurrentPhase);
    }
}
