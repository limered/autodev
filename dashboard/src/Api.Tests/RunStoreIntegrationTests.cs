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

    // Postgres timestamptz keeps microsecond precision while DateTimeOffset keeps 100ns
    // ticks, so a UtcNow with a sub-microsecond tail never round-trips exactly. Seed the
    // clock reading with the tail trimmed so exact-equality assertions are deterministic.
    private static DateTimeOffset UtcNowAtMicrosecondPrecision()
    {
        var now = DateTimeOffset.UtcNow;
        return now.AddTicks(-(now.Ticks % 10));
    }

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
    public async Task Delete_RemovesRow_AndReportsWhetherItExisted()
    {
        Skip.IfNot(_fixture.IsDockerAvailable, "Docker is not available; skipping RunStore integration tests.");
        var runId = Guid.NewGuid();
        await _store.Apply(runId, Started(DateTimeOffset.UtcNow));

        var deleted = await _store.Delete(runId);

        Assert.True(deleted);
        Assert.Null(await _fixture.GetRunAsync(runId));
        Assert.False(await _store.Delete(runId)); // second delete finds nothing
    }

    [SkippableFact]
    public async Task Heartbeat_WritesHeartbeatAndPhase_WithoutBumpingUpdatedAt()
    {
        Skip.IfNot(_fixture.IsDockerAvailable, "Docker is not available; skipping RunStore integration tests.");
        var runId = Guid.NewGuid();
        var started = UtcNowAtMicrosecondPrecision();
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
        var started = UtcNowAtMicrosecondPrecision();
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

    [SkippableFact]
    public async Task All_SkipTake_ReturnsWindowInStartedAtDescOrder_AndShortFinalPage()
    {
        Skip.IfNot(_fixture.IsDockerAvailable, "Docker is not available; skipping RunStore integration tests.");

        // Seed 25 runs, each started 1s apart so started_at DESC ordering is deterministic
        // (no ties). runIds[0] is oldest, runIds[24] is newest.
        var baseAt = DateTimeOffset.UtcNow;
        var runIds = new List<Guid>();
        for (var i = 0; i < 25; i++)
        {
            var id = Guid.NewGuid();
            runIds.Add(id);
            await _store.Apply(id, Started(baseAt.AddSeconds(i)));
        }

        var first = await _store.All(0, 10);
        var second = await _store.All(10, 10);
        var third = await _store.All(20, 10);

        // Full pages hold exactly `take` runs; the last page is short.
        Assert.Equal(10, first.Count);
        Assert.Equal(10, second.Count);
        Assert.Equal(5, third.Count);

        // Newest first: page 1 holds the 10 most recent, page 2 the next 10, page 3 the tail.
        Assert.Equal(runIds[24], first[0].RunId);
        Assert.Equal(runIds[15], first[9].RunId);
        Assert.Equal(runIds[14], second[0].RunId);
        Assert.Equal(runIds[5], second[9].RunId);
        Assert.Equal(runIds[4], third[0].RunId);
        Assert.Equal(runIds[0], third[4].RunId);

        // Within each page the runs are strictly ordered by started_at DESC.
        Assert.True(first[0].StartedAt > first[9].StartedAt);
        Assert.True(second[0].StartedAt > second[9].StartedAt);
        Assert.True(third[0].StartedAt > third[4].StartedAt);
    }
}
