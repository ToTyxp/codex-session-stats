using System.Buffers.Binary;
using AgentSpeed.Core.Traffic.Helper;
using AgentSpeed.TrafficHelper.Counting;
using AgentSpeed.TrafficHelper.Pipe;

namespace AgentSpeed.TrafficHelper.Tests;

public sealed class RequestHandlerTests
{
    private const ulong Epoch = 133_000_000_000_000_000;
    private const ulong SampleTime = 133_000_000_010_000_000;
    private const uint Session = 1;

    private readonly TrafficTable _table = new();
    private readonly HelperStats _stats = new();
    private readonly RequestRateLimiter _limiter = new();
    private byte[] _buffer = new byte[64];

    private RequestHandler Handler => new(_table, _stats, Epoch, () => SampleTime);

    public static TheoryData<string, byte[]> Malformed()
    {
        var valid = Request(1, HelperRequestType.Hello, 7);
        var data = new TheoryData<string, byte[]>
        {
            { "empty", [] },
            { "one byte", [0x41] },
            { "fifteen bytes", valid[..15] },
            { "seventeen bytes", [.. valid, 0] },
            { "thirty-two bytes", [.. valid, .. valid] },
            { "wrong magic", With(valid, 0, 0x42) },
            { "magic byte order", With(valid, 3, 0x41) },
            { "type 0", Request(1, (HelperRequestType)0, 7) },
            { "type 3", Request(1, (HelperRequestType)3, 7) },
            { "type with error flag", Request(1, (HelperRequestType)0x8001, 7) },
            { "reserved low byte", With(valid, 12, 1) },
            { "reserved high byte", With(valid, 15, 0x80) },
        };
        return data;
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public void MalformedRequestsGetBadRequestAndClose(string name, byte[] request)
    {
        var after = Handler.Handle(request, Session, 0, _limiter, ref _buffer, out var length);

        Assert.True(after == AfterResponse.Close, name);
        Assert.Equal(HelperProtocol.HeaderSize, length);
        var header = ReadHeader();
        Assert.Equal(HelperStatusCode.BadRequest, header.Status);
        Assert.True(header.IsError);
        Assert.Equal(0u, header.PayloadLength);
        Assert.Equal(Epoch, header.Epoch);
    }

    [Fact]
    public void BadRequestEchoesTheSequenceWhenTheMagicMatched()
    {
        Handler.Handle(With(Request(1, HelperRequestType.Snapshot, 99), 12, 1), Session, 0, _limiter, ref _buffer, out _);

        var header = ReadHeader();
        Assert.Equal(99u, header.Sequence);
        Assert.Equal((ushort)(2 | HelperProtocol.ErrorFlag), header.Type);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(0xFFFF)]
    public void UnsupportedVersionGetsTheHelloPayloadAndKeepsTheConnection(ushort version)
    {
        var after = Handler.Handle(Request(version, HelperRequestType.Snapshot, 5), Session, 0, _limiter, ref _buffer, out var length);

        Assert.Equal(AfterResponse.Continue, after);
        Assert.Equal(HelperProtocol.HeaderSize + HelperProtocol.HelloSize, length);
        Assert.True(HelperResponseHeader.TryParse(_buffer.AsSpan(0, 32), HelperRequestType.Snapshot, 5, out var header));
        Assert.Equal(HelperStatusCode.UnsupportedVersion, header.Status);
        Assert.True(HelperHello.TryParse(_buffer.AsSpan(32, 40), out var hello));
        Assert.Equal(HelperProtocol.MinVersion, hello.MinProtocol);
        Assert.Equal(HelperProtocol.MaxVersion, hello.MaxProtocol);
        Assert.Equal(HelperProtocol.BundledVersion.Packed, hello.HelperVersion);
    }

    [Fact]
    public void HelloReportsHealthAndCounters()
    {
        _stats.SetHealthy(true);
        _stats.CountUnparsed();
        _stats.SetCurrentSessionLost(3);

        var after = Handler.Handle(Request(1, HelperRequestType.Hello, 1), Session, 0, _limiter, ref _buffer, out var length);

        Assert.Equal(AfterResponse.Continue, after);
        Assert.Equal(72, length);
        Assert.True(HelperResponseHeader.TryParse(_buffer.AsSpan(0, 32), HelperRequestType.Hello, 1, out var header));
        Assert.Equal(HelperStatusCode.Ok, header.Status);
        Assert.Equal(Epoch, header.Epoch);
        Assert.True(HelperHello.TryParse(_buffer.AsSpan(32, 40), out var hello));
        Assert.True(hello.IsSessionHealthy);
        Assert.Equal(3ul, hello.EventsLost);
        Assert.Equal(1ul, hello.UnparsedEvents);
        Assert.Equal(0ul, hello.EvictedEntries);
    }

    [Fact]
    public void MoreThanFourRequestsInOneSecondAreRateLimited()
    {
        var handler = Handler;
        for (var i = 0; i < 4; i++)
        {
            handler.Handle(Request(1, HelperRequestType.Hello, (uint)i), Session, 100 + i * 200, _limiter, ref _buffer, out _);
            Assert.Equal(HelperStatusCode.Ok, ReadHeader().Status);
        }

        var after = handler.Handle(Request(1, HelperRequestType.Snapshot, 9), Session, 999, _limiter, ref _buffer, out var length);
        Assert.Equal(AfterResponse.Continue, after);
        Assert.Equal(HelperProtocol.HeaderSize, length);
        Assert.True(HelperResponseHeader.TryParse(_buffer.AsSpan(0, 32), HelperRequestType.Snapshot, 9, out var limited));
        Assert.Equal(HelperStatusCode.RateLimited, limited.Status);
        Assert.Equal(0u, limited.PayloadLength);

        // One second after the oldest of the four, one more request fits.
        handler.Handle(Request(1, HelperRequestType.Hello, 10), Session, 1_100, _limiter, ref _buffer, out _);
        Assert.Equal(HelperStatusCode.Ok, ReadHeader().Status);
        handler.Handle(Request(1, HelperRequestType.Hello, 11), Session, 1_101, _limiter, ref _buffer, out _);
        Assert.Equal(HelperStatusCode.RateLimited, ReadHeader().Status);
    }

    [Fact]
    public void AClientThatKeepsHittingTheLimitIsDisconnected()
    {
        var handler = Handler;
        for (var i = 0; i < 4; i++)
        {
            handler.Handle(Request(1, HelperRequestType.Hello, (uint)i), Session, 0, _limiter, ref _buffer, out _);
        }

        for (var i = 1; i < RequestRateLimiter.MaxConsecutiveRejections; i++)
        {
            Assert.Equal(AfterResponse.Continue, handler.Handle(Request(1, HelperRequestType.Hello, 100), Session, 10, _limiter, ref _buffer, out _));
            Assert.Equal(HelperStatusCode.RateLimited, ReadHeader().Status);
        }

        Assert.Equal(AfterResponse.Close, handler.Handle(Request(1, HelperRequestType.Hello, 100), Session, 10, _limiter, ref _buffer, out _));
        Assert.Equal(HelperStatusCode.RateLimited, ReadHeader().Status);
    }

    [Fact]
    public void AServedRequestResetsTheRejectionCount()
    {
        var limiter = new RequestRateLimiter();
        for (var i = 0; i < 4; i++)
        {
            limiter.TryAcquire(i);
        }

        for (var i = 0; i < RequestRateLimiter.MaxConsecutiveRejections - 1; i++)
        {
            Assert.False(limiter.TryAcquire(10));
        }

        Assert.True(limiter.TryAcquire(1_000));
        Assert.Equal(0, limiter.ConsecutiveRejections);
        Assert.False(limiter.TryAcquire(1_000));
        Assert.Equal(1, limiter.ConsecutiveRejections);
    }

    [Fact]
    public void RateLimiterAllowsASteadyFourPerSecond()
    {
        var limiter = new RequestRateLimiter();
        for (var i = 0; i < 40; i++)
        {
            Assert.True(limiter.TryAcquire(i * 250));
        }

        Assert.False(limiter.TryAcquire(39 * 250 + 1));
    }

    [Fact]
    public void SnapshotListsOnlyTheClientSessionsProcessesWithTraffic()
    {
        _table.ProcessStarted(new ProcessInfo(100, 1_000, 1, Session), 0);
        _table.ProcessStarted(new ProcessInfo(200, 2_000, 1, Session), 0);
        _table.ProcessStarted(new ProcessInfo(300, 3_000, 1, 2), 0);
        _table.AddBytes(100, receive: true, 1_500, 5_000, 0);
        _table.AddBytes(100, receive: false, 40, 5_000, 0);
        _table.AddBytes(300, receive: true, 9_999, 5_000, 0);
        _table.ProcessStopped(100, 1_000, 6_000, 0);

        Handler.Handle(Request(1, HelperRequestType.Snapshot, 3), Session, 0, _limiter, ref _buffer, out var length);

        Assert.True(HelperResponseHeader.TryParse(_buffer.AsSpan(0, 32), HelperRequestType.Snapshot, 3, out var header));
        Assert.Equal(length - HelperProtocol.HeaderSize, (int)header.PayloadLength);
        var entries = new List<HelperEntry>();
        Assert.True(HelperSnapshot.TryParse(_buffer.AsSpan(32, (int)header.PayloadLength), entries, out var sampleTime));
        Assert.Equal(SampleTime, sampleTime);
        var entry = Assert.Single(entries);
        Assert.Equal(new HelperEntry(100, HelperEntry.ExitedFlag, 1_000, 1_500, 40), entry);
    }

    [Fact]
    public void EmptySnapshotHasOnlyTheHeader()
    {
        Handler.Handle(Request(1, HelperRequestType.Snapshot, 1), Session, 0, _limiter, ref _buffer, out var length);

        Assert.Equal(HelperProtocol.HeaderSize + HelperProtocol.SnapshotHeaderSize, length);
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(_buffer.AsSpan(32 + 8)));
    }

    [Fact]
    public void FullTableFitsTheProtocolMaximum()
    {
        for (uint pid = 8; pid < 8 + TrafficTable.Capacity; pid++)
        {
            _table.ProcessStarted(new ProcessInfo(pid, 10, 1, Session), 0);
            _table.AddBytes(pid, receive: true, 1, 20, 0);
        }

        _table.ProcessStarted(new ProcessInfo(99_999, 10, 1, Session), 1);
        _table.AddBytes(99_999, receive: true, 1, 20, 1);

        Handler.Handle(Request(1, HelperRequestType.Snapshot, 1), Session, 0, _limiter, ref _buffer, out var length);

        Assert.Equal(HelperProtocol.HeaderSize + HelperProtocol.MaxSnapshotPayload, length);
        Assert.True(HelperResponseHeader.TryParse(_buffer.AsSpan(0, 32), HelperRequestType.Snapshot, 1, out var header));
        var entries = new List<HelperEntry>();
        Assert.True(HelperSnapshot.TryParse(_buffer.AsSpan(32, (int)header.PayloadLength), entries, out _));
        Assert.Equal(HelperProtocol.MaxEntries, entries.Count);
        Assert.Equal(1ul, _table.EvictedEntries);
    }

    [Fact]
    public void ResponseBufferGrowsOnlyFromTableSize()
    {
        var tiny = new byte[1];
        Handler.Handle(Request(1, HelperRequestType.Hello, 1), Session, 0, _limiter, ref tiny, out var length);

        Assert.True(tiny.Length >= length);
    }

    private HelperResponseHeader ReadHeader() => new(
        BinaryPrimitives.ReadUInt16LittleEndian(_buffer.AsSpan(4)),
        BinaryPrimitives.ReadUInt16LittleEndian(_buffer.AsSpan(6)),
        (HelperStatusCode)BinaryPrimitives.ReadUInt32LittleEndian(_buffer.AsSpan(8)),
        BinaryPrimitives.ReadUInt32LittleEndian(_buffer.AsSpan(12)),
        BinaryPrimitives.ReadUInt64LittleEndian(_buffer.AsSpan(16)),
        BinaryPrimitives.ReadUInt32LittleEndian(_buffer.AsSpan(24)));

    private static byte[] Request(ushort version, HelperRequestType type, uint sequence)
    {
        var bytes = new byte[16];
        new HelperRequest(version, type, sequence).Write(bytes);
        return bytes;
    }

    private static byte[] With(byte[] source, int index, byte value)
    {
        var copy = (byte[])source.Clone();
        copy[index] = value;
        return copy;
    }
}
