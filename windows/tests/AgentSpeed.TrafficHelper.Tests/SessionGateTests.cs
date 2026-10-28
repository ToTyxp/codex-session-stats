using AgentSpeed.TrafficHelper.Pipe;

namespace AgentSpeed.TrafficHelper.Tests;

public sealed class SessionGateTests
{
    [Fact]
    public void SessionZeroIsNeverServed()
    {
        var gate = new SessionGate();

        Assert.False(gate.TryEnter(0));
        Assert.Equal(0, gate.Count(0));
    }

    [Fact]
    public void EachSessionGetsAtMostTwoInstances()
    {
        var gate = new SessionGate();

        Assert.True(gate.TryEnter(1));
        Assert.True(gate.TryEnter(1));
        Assert.False(gate.TryEnter(1));
        Assert.True(gate.TryEnter(2));
        Assert.True(gate.TryEnter(2));
        Assert.False(gate.TryEnter(2));
        Assert.Equal(SessionGate.PerSessionLimit, gate.Count(1));
    }

    [Fact]
    public void LeavingFreesASlot()
    {
        var gate = new SessionGate();
        gate.TryEnter(3);
        gate.TryEnter(3);

        gate.Leave(3);

        Assert.True(gate.TryEnter(3));
        gate.Leave(3);
        gate.Leave(3);
        gate.Leave(3);
        Assert.Equal(0, gate.Count(3));
        Assert.True(gate.TryEnter(3));
    }

    [Fact]
    public void ConcurrentUseStaysWithinTheLimit()
    {
        var gate = new SessionGate();
        var admitted = 0;

        Parallel.For(0, 64, _ =>
        {
            if (gate.TryEnter(7))
            {
                Interlocked.Increment(ref admitted);
            }
        });

        Assert.Equal(SessionGate.PerSessionLimit, admitted);
    }
}
