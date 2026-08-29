using Api.Runs;
using Xunit;

namespace Api.Tests;

/// <summary>
/// Integration tests over the <em>real</em> <see cref="RunStore"/> against a real Postgres
/// container. They pin the write-path branches <see cref="FakeRunStore"/> cannot: the
/// insert-on-first-event path, the cheap heartbeat-only update, the changed-columns-only
/// diff update, the out-of-order <c>@updatedAt</c> guard, and the run-finished orchestration
/// (delete the queue row + close the linked GitHub issue) — none of which live in the fold.
/// </summary>
public sealed class RunStoreIntegrationTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private RunStore _store = null!;
    private RecordingGitHubIssuesClient _gitHub = null!;

    public RunStoreIntegrationTests(PostgresFixture fixture) => _fixture = fixture;

    public Task InitializeAsync()
    {
        if (!_fixture.IsDockerAvailable) return Task.CompletedTask;
        (_store, _gitHub) = _fixture.CreateRunStore();
        return _fixture.ResetAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static RunEvent Started(DateTimeOffset at) => new(
        "run-started", at, "owner/repo", "feat/x", "do the thing", "gpt-x",
        null, null, null, null,
        Stages: new[] { new RunStage("build", "gpt-x") });

    [SkippableFact]
    public async Task RunStarted_InsertsRow()
    {
        Skip.IfNot(_fixture.IsDockerAvailable, "Docker is not available; skipping RunStore integration tests.");
        var runId = Guid.NewGuid();
        var at = DateTimeOffset.UtcNow;

        await _store.Apply(runId, Started(at));

        var persisted = await _fixture.GetRunAsync(runId);
        Assert.NotNull(persisted);
        Assert.Equal("launching", persisted!.Status);
        Assert.Equal("owner/repo", persisted.Repo);
        Assert.NotNull(persisted.Stages);
        Assert.Equal("build", persisted.Stages!.Single().Agent);
    }

    [SkippableFact]
    public async Task Heartbeat_WritesHeartbeatAndPhase_WithoutBumpingUpdatedAt()
    {
        Skip.IfNot(_fixture.IsDockerAvailable, "Docker is not available; skipping RunStore integration tests.");
        var runId = Guid.NewGuid();
        var started = DateTimeOffset.UtcNow;
        await _store.Apply(runId, Started(started));
        var before = await _fixture.GetRunAsync(runId);

        var beat = started.AddSeconds(5);
        await _store.Apply(runId, new RunEvent(
            "heartbeat", beat, null, null, null, null, null, null, null, null, CurrentPhase: "compiling"));

        var after = await _fixture.GetRunAsync(runId);
        Assert.Equal(beat, after!.LastHeartbeatAt);
        Assert.Equal("compiling", after.CurrentPhase);
        // The heartbeat-only path must NOT bump updated_at.
        Assert.Equal(before!.UpdatedAt, after.UpdatedAt);
    }

    [SkippableFact]
    public async Task AgentStarted_DiffUpdate_WritesOnlyChangedColumns()
    {
        Skip.IfNot(_fixture.IsDockerAvailable, "Docker is not available; skipping RunStore integration tests.");
        var runId = Guid.NewGuid();
        var started = DateTimeOffset.UtcNow;
        await _store.Apply(runId, Started(started));

        var at = started.AddSeconds(3);
        await _store.Apply(runId, new RunEvent(
            "agent-started", at, null, null, null, null, VmName: "vm-42",
            null, null, null));

        var after = await _fixture.GetRunAsync(runId);
        Assert.Equal("running", after!.Status);
        Assert.Equal("vm-42", after.VmName);
        Assert.Equal(at, after.UpdatedAt);
        Assert.Equal("do the thing", after.Spec); // untouched column preserved
    }

    [SkippableFact]
    public async Task OutOfOrderEvent_IsRejectedByUpdatedAtGuard()
    {
        Skip.IfNot(_fixture.IsDockerAvailable, "Docker is not available; skipping RunStore integration tests.");
        var runId = Guid.NewGuid();
        var started = DateTimeOffset.UtcNow;
        await _store.Apply(runId, Started(started));
        await _store.Apply(runId, new RunEvent(
            "agent-started", started.AddSeconds(10), null, null, null, null, "vm-new", null, null, null));

        // A stale agent-started (earlier timestamp, different vm) must not overwrite.
        await _store.Apply(runId, new RunEvent(
            "agent-started", started.AddSeconds(1), null, null, null, null, "vm-stale", null, null, null));

        var after = await _fixture.GetRunAsync(runId);
        Assert.Equal("vm-new", after!.VmName);
    }

    [SkippableFact]
    public async Task RunFinished_DeletesQueueRow_AndClosesLinkedIssue()
    {
        Skip.IfNot(_fixture.IsDockerAvailable, "Docker is not available; skipping RunStore integration tests.");
        var runId = Guid.NewGuid();
        var started = DateTimeOffset.UtcNow;

        await _fixture.SeedIssueAsync(555, "owner/repo", 7, "The issue");
        await _fixture.SeedQueueRowAsync(555, runId);
        await _store.Apply(runId, Started(started));
        await _store.Apply(runId, new RunEvent(
            "agent-started", started.AddSeconds(1), null, null, null, null, "vm-1", null, null, null));

        await _store.Apply(runId, new RunEvent(
            "run-finished", started.AddSeconds(9), null, null, null, null, null, null, null, null));

        var after = await _fixture.GetRunAsync(runId);
        Assert.Equal("done", after!.Status);
        Assert.Equal(0, await _fixture.CountQueueRowsAsync(runId));
        Assert.Equal(("owner/repo", 7), Assert.Single(_gitHub.Closed));
    }

    [SkippableFact]
    public async Task RunFinished_NoLinkedIssue_DoesNotClose()
    {
        Skip.IfNot(_fixture.IsDockerAvailable, "Docker is not available; skipping RunStore integration tests.");
        var runId = Guid.NewGuid();
        var started = DateTimeOffset.UtcNow;
        await _store.Apply(runId, Started(started));

        await _store.Apply(runId, new RunEvent(
            "run-finished", started.AddSeconds(9), null, null, null, null, null, null, null, null));

        Assert.Empty(_gitHub.Closed);
    }
}
