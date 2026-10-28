using System.Buffers.Binary;
using System.Text;
using AgentSpeed.TrafficHelper.Etw;

namespace AgentSpeed.TrafficHelper.Tests;

public sealed class EventLayoutTests
{
    private const ushort UInt16 = 6;
    private const ushort UInt32 = 8;
    private const ushort UInt64 = 10;
    private const ushort Binary = 14;
    private const ushort FileTime = 17;
    private const ushort UnicodeString = 1;
    private const ushort Sid = 19;
    private const uint ParamLength = 0x2;

    [Fact]
    public void TcpIPv4LayoutMatchesTheVerifiedBuild()
    {
        var layout = EventLayoutResolver.Network(NetworkProperties(4), 8, ipv6: false);

        Assert.Equal(EventLayout.Network(0, 4, 8, 12, 4), layout);
        Assert.Equal(16, layout.MinimumLength);
    }

    [Fact]
    public void TcpIPv6LayoutMatchesTheVerifiedBuild()
    {
        var layout = EventLayoutResolver.Network(NetworkProperties(16), 8, ipv6: true);

        Assert.Equal(EventLayout.Network(0, 4, 8, 24, 16), layout);
        Assert.Equal(40, layout.MinimumLength);
    }

    [Fact]
    public void AddressOfTheWrongFamilyIsInvalid()
    {
        Assert.False(EventLayoutResolver.Network(NetworkProperties(4), 8, ipv6: true).Valid);
        Assert.False(EventLayoutResolver.Network(NetworkProperties(16), 8, ipv6: false).Valid);
    }

    [Fact]
    public void FieldAfterAVariableSizePropertyIsInvalid()
    {
        EventProperty[] properties =
        [
            new("PID", UInt32, 0, 1, 4),
            new("name", UnicodeString, 0, 1, 0),
            new("size", UInt32, 0, 1, 4),
            new("daddr", UInt32, 0, 1, 4),
            new("saddr", UInt32, 0, 1, 4),
        ];

        Assert.False(EventLayoutResolver.Network(properties, 8, ipv6: false).Valid);
    }

    [Fact]
    public void ParameterSizedBinaryEndsTheResolvablePrefix()
    {
        Assert.Equal(-1, EventLayoutResolver.FixedSize(new EventProperty("blob", Binary, ParamLength, 1, 16), 8));
        Assert.Equal(16, EventLayoutResolver.FixedSize(new EventProperty("addr", Binary, 0, 1, 16), 8));
        Assert.Equal(12, EventLayoutResolver.FixedSize(new EventProperty("array", UInt32, 0, 3, 4), 8));
        Assert.Equal(4, EventLayoutResolver.FixedSize(new EventProperty("pointer", 16, 0, 1, 0), 4));
    }

    [Fact]
    public void MissingFieldIsInvalid()
    {
        EventProperty[] properties = [new("PID", UInt32, 0, 1, 4), new("size", UInt32, 0, 1, 4)];

        Assert.False(EventLayoutResolver.Network(properties, 8, ipv6: false).Valid);
    }

    [Fact]
    public void ProcessStartLayoutSkipsSequenceNumbers()
    {
        EventProperty[] properties =
        [
            new("ProcessID", UInt32, 0, 1, 4),
            new("ProcessSequenceNumber", UInt64, 0, 1, 8),
            new("CreateTime", FileTime, 0, 1, 8),
            new("ParentProcessID", UInt32, 0, 1, 4),
            new("ParentProcessSequenceNumber", UInt64, 0, 1, 8),
            new("SessionID", UInt32, 0, 1, 4),
            new("Flags", UInt32, 0, 1, 4),
            new("MandatoryLabel", Sid, 0, 1, 0),
            new("ImageName", UnicodeString, 0, 1, 0),
        ];

        var layout = EventLayoutResolver.ProcessStart(properties, 8);

        Assert.Equal(EventLayout.Process(0, 12, 20, 32), layout);
        Assert.Equal(36, layout.MinimumLength);
    }

    [Fact]
    public void ProcessStopNeedsOnlyIdAndCreateTime()
    {
        EventProperty[] properties =
        [
            new("ProcessID", UInt32, 0, 1, 4),
            new("CreateTime", FileTime, 0, 1, 8),
            new("ExitTime", FileTime, 0, 1, 8),
            new("ImageName", UnicodeString, 0, 1, 0),
        ];

        var layout = EventLayoutResolver.ProcessStop(properties, 8);

        Assert.True(layout.Valid);
        Assert.Equal(0, layout.First);
        Assert.Equal(4, layout.Second);
        Assert.False(EventLayoutResolver.ProcessStart(properties, 8).Valid);
    }

    [Fact]
    public void TraceEventInfoIsParsedWithBoundsChecks()
    {
        var info = TraceEventInfo(("PID", UInt32, 1, 4), ("size", UInt32, 1, 4), ("daddr", Binary, 1, 16));

        var properties = TdhLayoutReader.Parse(info);

        Assert.NotNull(properties);
        Assert.Equal(["PID", "size", "daddr"], properties!.Select(p => p.Name));
        Assert.Equal(Binary, properties[2].InType);
        Assert.Equal((ushort)16, properties[2].Length);
    }

    [Fact]
    public void TraceEventInfoWithTooManyPropertiesIsRejected()
    {
        var info = TraceEventInfo(("PID", UInt32, 1, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(104), 1_000);

        Assert.Null(TdhLayoutReader.Parse(info));
        Assert.Null(TdhLayoutReader.Parse(info.AsSpan(0, 50)));
    }

    [Fact]
    public void NameOffsetOutsideTheBufferGivesAnEmptyName()
    {
        var info = TraceEventInfo(("PID", UInt32, 1, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(112 + 4), 0xFFFF);

        Assert.Equal("", Assert.Single(TdhLayoutReader.Parse(info)!).Name);
    }

    private static EventProperty[] NetworkProperties(ushort addressLength) =>
    [
        new("PID", UInt32, 0, 1, 4),
        new("size", UInt32, 0, 1, 4),
        addressLength == 4 ? new("daddr", UInt32, 0, 1, 4) : new("daddr", Binary, 0, 1, 16),
        addressLength == 4 ? new("saddr", UInt32, 0, 1, 4) : new("saddr", Binary, 0, 1, 16),
        new("dport", UInt16, 0, 1, 2),
        new("sport", UInt16, 0, 1, 2),
    ];

    private static byte[] TraceEventInfo(params (string Name, ushort InType, ushort Count, ushort Length)[] properties)
    {
        var namesOffset = 112 + properties.Length * 24;
        var names = new List<byte>();
        var buffer = new byte[namesOffset];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(100), (uint)properties.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(104), (uint)properties.Length);
        for (var i = 0; i < properties.Length; i++)
        {
            var entry = buffer.AsSpan(112 + i * 24);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], (uint)(namesOffset + names.Count));
            BinaryPrimitives.WriteUInt16LittleEndian(entry[8..], properties[i].InType);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[16..], properties[i].Count);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[18..], properties[i].Length);
            names.AddRange(Encoding.Unicode.GetBytes(properties[i].Name + "\0"));
        }

        return [.. buffer, .. names];
    }
}
