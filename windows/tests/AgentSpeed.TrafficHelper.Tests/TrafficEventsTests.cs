using System.Buffers.Binary;
using AgentSpeed.TrafficHelper.Counting;
using AgentSpeed.TrafficHelper.Etw;

namespace AgentSpeed.TrafficHelper.Tests;

public sealed class TrafficEventsTests
{
    private const uint Pid = 4242;

    /// <summary>The layouts verified on build 28000, version 0 (WINDOWS-SERVICE 3.5).</summary>
    private static readonly EventLayout V4 = EventLayout.Network(0, 4, 8, 12, 4);
    private static readonly EventLayout V6 = EventLayout.Network(0, 4, 8, 24, 16);

    private readonly TrafficTable _table = new();
    private readonly HelperStats _stats = new();
    private readonly TrafficEvents _events;

    public TrafficEventsTests()
    {
        _events = new TrafficEvents(_table, _stats);
        _table.ProcessStarted(new ProcessInfo(Pid, 100, 1, 1), 0);
    }

    public static TheoryData<string, string, bool> V4Cases() => new()
    {
        { "203.0.113.5", "192.168.1.10", true },
        { "127.0.0.1", "127.0.0.1", false },
        { "127.0.0.1", "192.168.1.10", false },
        { "192.168.1.10", "127.5.6.7", false },
        { "128.0.0.1", "10.0.0.1", true },
    };

    [Theory]
    [MemberData(nameof(V4Cases))]
    public void IPv4LoopbackOnEitherSideIsDropped(string destination, string source, bool counted)
    {
        _events.Network(11, V4, Event(1000, Address(destination), Address(source), 4), 200, 0);

        Assert.Equal(counted ? 1000ul : 0ul, Rx());
    }

    public static TheoryData<string, string, bool> V6Cases() => new()
    {
        { "2001:db8::1", "2001:db8::2", true },
        { "::1", "::1", false },
        { "::1", "2001:db8::2", false },
        { "2001:db8::1", "::ffff:127.0.0.1", false },
        { "::ffff:127.200.0.9", "2001:db8::2", false },
        { "::ffff:10.0.0.1", "2001:db8::2", true },
        { "::2", "2001:db8::2", true },
        { "fe80::1", "fe80::2", true },
    };

    [Theory]
    [MemberData(nameof(V6Cases))]
    public void IPv6LoopbackAndMappedLoopbackAreDropped(string destination, string source, bool counted)
    {
        _events.Network(27, V6, Event(1000, Address(destination), Address(source), 16), 200, 0);

        Assert.Equal(counted ? 1000ul : 0ul, Rx());
    }

    [Theory]
    [InlineData(10, false)]
    [InlineData(11, true)]
    [InlineData(42, false)]
    [InlineData(43, true)]
    public void SendAndReceiveIdsGoToTxAndRx(ushort id, bool receive)
    {
        _events.Network(id, V4, Event(64, Address("8.8.8.8"), Address("10.0.0.2"), 4), 200, 0);

        Assert.True(_table.TryGet(Pid, 100, out var entry));
        Assert.Equal(receive ? 64ul : 0ul, entry.Rx);
        Assert.Equal(receive ? 0ul : 64ul, entry.Tx);
    }

    [Theory]
    [InlineData(26, false)]
    [InlineData(27, true)]
    [InlineData(58, false)]
    [InlineData(59, true)]
    public void IPv6SendAndReceiveIds(ushort id, bool receive)
    {
        _events.Network(id, V6, Event(64, Address("2001:db8::1"), Address("2001:db8::2"), 16), 200, 0);

        Assert.True(_table.TryGet(Pid, 100, out var entry));
        Assert.Equal(receive ? 64ul : 0ul, entry.Rx);
        Assert.Equal(receive ? 0ul : 64ul, entry.Tx);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(4u)]
    public void IdleAndSystemPidsAreDropped(uint pid)
    {
        var data = Event(1000, Address("8.8.8.8"), Address("10.0.0.2"), 4, pid);
        _events.Network(11, V4, data, 200, 0);

        Assert.False(_table.HasOrphans);
        Assert.Equal(0ul, Rx());
    }

    [Fact]
    public void SizeZeroIsDropped()
    {
        _events.Network(11, V4, Event(0, Address("8.8.8.8"), Address("10.0.0.2"), 4), 200, 0);

        Assert.Equal(0ul, Rx());
        Assert.Equal(0ul, _stats.UnparsedEvents);
    }

    [Fact]
    public void ShortDataIsUnparsed()
    {
        var data = Event(1000, Address("8.8.8.8"), Address("10.0.0.2"), 4);
        _events.Network(11, V4, data.AsSpan(0, 15), 200, 0);

        Assert.Equal(1ul, _stats.UnparsedEvents);
        Assert.Equal(0ul, Rx());
    }

    [Fact]
    public void InvalidLayoutIsUnparsed()
    {
        _events.Network(11, EventLayout.Invalid, Event(1000, Address("8.8.8.8"), Address("10.0.0.2"), 4), 200, 0);
        _events.ProcessStart(EventLayout.Invalid, new byte[64], 0);
        _events.ProcessStop(EventLayout.Invalid, new byte[64], 0, 0);

        Assert.Equal(3ul, _stats.UnparsedEvents);
    }

    [Fact]
    public void UncountedNetworkIdsAreIgnoredSilently()
    {
        // Connect (12), disconnect (13), retransmit (14) arrive when the provider rejected the event-ID filter.
        _events.Network(12, V4, Event(1000, Address("8.8.8.8"), Address("10.0.0.2"), 4), 200, 0);
        _events.Network(14, EventLayout.Invalid, [], 200, 0);

        Assert.Equal(0ul, _stats.UnparsedEvents);
        Assert.Equal(0ul, Rx());
    }

    [Fact]
    public void ProcessStartAndStopEventsDriveTheTable()
    {
        var start = EventLayout.Process(0, 12, 20, 32);
        var data = new byte[48];
        BinaryPrimitives.WriteUInt32LittleEndian(data, 777);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(12), 5_000);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20), 600);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(32), 3);

        _events.ProcessStart(start, data, 0);
        Assert.True(_table.TryGet(777, 5_000, out var started));
        Assert.Equal(3u, started.SessionId);

        _events.ProcessStop(EventLayout.Process(0, 12, 0, 0), data, 6_000, 10);
        Assert.True(_table.TryGet(777, 5_000, out var stopped));
        Assert.True(stopped.Exited);
    }

    private ulong Rx() => _table.TryGet(Pid, 100, out var entry) ? entry.Rx : 0;

    private static byte[] Event(uint size, byte[] destination, byte[] source, int addressLength, uint pid = Pid)
    {
        var data = new byte[8 + addressLength * 2 + 16];
        BinaryPrimitives.WriteUInt32LittleEndian(data, pid);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), size);
        destination.CopyTo(data, 8);
        source.CopyTo(data, 8 + addressLength);
        return data;
    }

    private static byte[] Address(string text) => System.Net.IPAddress.Parse(text).GetAddressBytes();
}
