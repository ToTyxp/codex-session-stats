using AgentSpeed.TrafficHelper.Pipe;

namespace AgentSpeed.TrafficHelper.Tests;

public sealed class IdleTrackerTests
{
    [Fact]
    public void StopsTwoMinutesAfterStartWhenNoClientEverConnects()
    {
        var idle = new IdleTracker(1_000);

        Assert.False(idle.ShouldStop(1_000 + IdleTracker.IdleStopMs - 1));
        Assert.True(idle.ShouldStop(1_000 + IdleTracker.IdleStopMs));
    }

    [Fact]
    public void NeverStopsWhileAClientIsConnected()
    {
        var idle = new IdleTracker(0);
        idle.Connected();

        Assert.False(idle.ShouldStop(10 * IdleTracker.IdleStopMs));
    }

    [Fact]
    public void CountsFromTheLastDisconnect()
    {
        var idle = new IdleTracker(0);
        idle.Connected();
        idle.Connected();
        idle.Disconnected(50_000);
        Assert.False(idle.ShouldStop(50_000 + IdleTracker.IdleStopMs));

        idle.Disconnected(90_000);
        Assert.False(idle.ShouldStop(90_000 + IdleTracker.IdleStopMs - 1));
        Assert.True(idle.ShouldStop(90_000 + IdleTracker.IdleStopMs));
    }

    [Fact]
    public void ExtraDisconnectDoesNotGoNegative()
    {
        var idle = new IdleTracker(0);
        idle.Disconnected(10);
        idle.Connected();

        Assert.Equal(1, idle.Clients);
        Assert.False(idle.ShouldStop(IdleTracker.IdleStopMs * 2));
    }
}
