using Api.Runs;

namespace Api.Tests;

public sealed class FakeRunStore : IRunStore
{
    private readonly Dictionary<Guid, RunState> _runs = new();

    public Task<IReadOnlyList<RunState>> All()
    {
        var runs = _runs.Values.OrderByDescending(r => r.StartedAt).ToList();
        return Task.FromResult<IReadOnlyList<RunState>>(runs);
    }

    public Task<IReadOnlyList<RunState>> Active()
    {
        var runs = _runs.Values
            .Where(r => r.Status is "launching" or "running" or "stalled")
            .OrderByDescending(r => r.StartedAt)
            .ToList();
        return Task.FromResult<IReadOnlyList<RunState>>(runs);
    }

    public Task<RunState?> Get(Guid runId)
    {
        return Task.FromResult(_runs.TryGetValue(runId, out var state) ? state : null);
    }

    public Task<RunState?> Apply(Guid runId, RunEvent ev)
    {
        var current = _runs.TryGetValue(runId, out var state) ? state : null;
        var next = RunFold.Apply(current, ev with { RunId = runId });
        if (next is not null)
        {
            _runs[runId] = next;
            return Task.FromResult<RunState?>(next);
        }

        return Task.FromResult(current);
    }
}
