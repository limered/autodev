using Api.Host;
using Xunit;

namespace Api.Tests;

public sealed class HostLivenessTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Null_IsOffline() => Assert.False(HostLiveness.IsOnline(null, Now));

    [Fact]
    public void Fresh_IsOnline() => Assert.True(HostLiveness.IsOnline(Now.AddSeconds(-5), Now));

    [Fact]
    public void Stale_IsOffline() => Assert.False(HostLiveness.IsOnline(Now.AddSeconds(-21), Now));

    [Fact]
    public void Future_IsOnline() => Assert.True(HostLiveness.IsOnline(Now.AddSeconds(5), Now));
}
