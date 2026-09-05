using Api.Host;

namespace Api.Tests;

public sealed class FakeHostStore : IHostStore
{
    private DateTimeOffset? _lastSeen;

    public Task StampLastSeen()
    {
        _lastSeen = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    }

    public Task<HostState?> GetState()
    {
        if (!_lastSeen.HasValue)
        {
            return Task.FromResult<HostState?>(null);
        }

        var online = HostLiveness.IsOnline(_lastSeen, DateTimeOffset.UtcNow);
        return Task.FromResult<HostState?>(new HostState(_lastSeen.Value, online));
    }
}
