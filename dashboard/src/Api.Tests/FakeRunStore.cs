using Api.Host;
using Api.Issues;
using Api.Runs;

namespace Api.Tests;

/// <summary>
/// In-memory <see cref="IRunStore"/>: the fold, plus the same run-completion
/// composition the real store performs — host stamping on every event, and on
/// run-finished the queue-slot release, linked-issue resolve and best-effort GitHub
/// close — via the injected fakes, so unit tests over the fake no longer silently skip
/// those side effects. Without injected fakes the composition is absent, matching the
/// fold-only behaviour the ingest tests exercise.
/// </summary>
public sealed class FakeRunStore : IRunStore
{
    private readonly Dictionary<Guid, RunState> _runs = new();
    private readonly IHostStore? _hostStore;
    private readonly IIssueResolver? _resolver;
    private readonly IGitHubIssuesClient? _gitHub;

    public FakeRunStore(IHostStore? hostStore = null, IIssueResolver? resolver = null, IGitHubIssuesClient? gitHub = null)
    {
        _hostStore = hostStore;
        _resolver = resolver;
        _gitHub = gitHub;
    }

    public Task<IReadOnlyList<RunState>> All()
    {
        var runs = _runs.Values.OrderByDescending(r => r.StartedAt).ToList();
        return Task.FromResult<IReadOnlyList<RunState>>(runs);
    }

    public Task<IReadOnlyList<RunState>> All(int skip, int take)
    {
        var runs = _runs.Values
            .OrderByDescending(r => r.StartedAt)
            .Skip(skip)
            .Take(take)
            .ToList();
        return Task.FromResult<IReadOnlyList<RunState>>(runs);
    }

    public Task<IReadOnlyList<RunState>> Active()
    {
        var runs = _runs.Values
            .Where(r => RunFold.IsActiveStatus(r.Status))
            .OrderByDescending(r => r.StartedAt)
            .ToList();
        return Task.FromResult<IReadOnlyList<RunState>>(runs);
    }

    public Task<RunState?> GetRun(Guid runId)
    {
        return Task.FromResult(_runs.TryGetValue(runId, out var state) ? state : null);
    }

    public async Task<RunState?> Apply(Guid runId, RunEvent ev)
    {
        // A run event only arrives via the host relay, so any event proves the host is
        // alive — stamped before folding, same as the real store, so even an event the
        // fold rejects still counts.
        if (_hostStore is not null)
        {
            await _hostStore.StampLastSeen();
        }

        var current = _runs.TryGetValue(runId, out var state) ? state : null;
        var next = RunFold.Apply(current, ev with { RunId = runId });
        if (next is null)
        {
            return current;
        }

        _runs[runId] = next;

        if (ev is RunFinishedEvent)
        {
            await CompleteRun(runId);
        }

        return next;
    }

    public async Task<bool> Delete(Guid runId)
    {
        // Same composition as the real delete: the queue slot (if any) is released with
        // the run's removal, so a deleted run leaves no orphaned queue row.
        if (_resolver is not null)
        {
            await _resolver.ReleaseQueueSlotAsync(runId, conn: null!, tx: null!);
        }

        return _runs.Remove(runId);
    }

    /// <summary>
    /// The run-finished composition, mirroring the real store's: resolve the linked
    /// issue, release the queue slot, then close the issue on GitHub best-effort — a
    /// failed close never fails the event.
    /// </summary>
    private async Task CompleteRun(Guid runId)
    {
        if (_resolver is null)
        {
            return;
        }

        var linkedIssue = await _resolver.ResolveLinkedIssueAsync(runId, conn: null!, tx: null!);
        await _resolver.ReleaseQueueSlotAsync(runId, conn: null!, tx: null!);

        if (linkedIssue is null || _gitHub is null)
        {
            return;
        }

        try
        {
            await _gitHub.CloseIssueAsync(linkedIssue.Repo, linkedIssue.Number);
        }
        catch
        {
            // Best-effort, same as the real store's post-commit close.
        }
    }
}
