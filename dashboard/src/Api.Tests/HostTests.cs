using Api.Host;
using Xunit;

namespace Api.Tests;

public class HostTests
{
    [Fact]
    public async Task GetState_WhenNeverStamped_ReturnsNull()
    {
        var store = new FakeHostStore();

        var state = await store.GetState();

        Assert.Null(state);
    }

    [Fact]
    public async Task GetState_AfterStamp_IsOnline()
    {
        var store = new FakeHostStore();

        await store.StampLastSeen();
        var state = await store.GetState();

        Assert.NotNull(state);
        Assert.True(state.Online);
    }
}
