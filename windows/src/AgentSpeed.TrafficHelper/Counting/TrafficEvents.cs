using System.Buffers.Binary;
using AgentSpeed.TrafficHelper.Etw;

namespace AgentSpeed.TrafficHelper.Counting;

/// <summary>
/// Applies decoded events to the table (WINDOWS-SERVICE 3.5, 3.6): network events with loopback either side, size 0 or
/// PID ≤ 4 are dropped; events whose layout is unusable or whose data is too short are counted as unparsed.
/// Addresses are read only for the loopback decision and never stored.
/// </summary>
internal sealed class TrafficEvents(TrafficTable table, HelperStats stats)
{
    public void Network(ushort id, in EventLayout layout, ReadOnlySpan<byte> data, ulong eventTime, long nowMs)
    {
        if (!TrafficEventIds.IsCountedNetwork(id))
        {
            return;
        }

        if (!layout.Valid || data.Length < layout.MinimumLength)
        {
            stats.CountUnparsed();
            return;
        }

        var pid = BinaryPrimitives.ReadUInt32LittleEndian(data[layout.First..]);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(data[layout.Second..]);
        if (pid <= 4 || size == 0)
        {
            return;
        }

        var destination = data.Slice(layout.Third, layout.AddressLength);
        var source = data.Slice(layout.Fourth, layout.AddressLength);
        var loopback = layout.AddressLength == 4
            ? Loopback.IsIPv4(destination) || Loopback.IsIPv4(source)
            : Loopback.IsIPv6(destination) || Loopback.IsIPv6(source);
        if (loopback)
        {
            return;
        }

        table.AddBytes(pid, TrafficEventIds.IsReceive(id), size, eventTime, nowMs);
    }

    public void ProcessStart(in EventLayout layout, ReadOnlySpan<byte> data, long nowMs)
    {
        if (!layout.Valid || data.Length < layout.MinimumLength)
        {
            stats.CountUnparsed();
            return;
        }

        table.ProcessStarted(
            new ProcessInfo(
                BinaryPrimitives.ReadUInt32LittleEndian(data[layout.First..]),
                BinaryPrimitives.ReadUInt64LittleEndian(data[layout.Second..]),
                BinaryPrimitives.ReadUInt32LittleEndian(data[layout.Third..]),
                BinaryPrimitives.ReadUInt32LittleEndian(data[layout.Fourth..])),
            nowMs);
    }

    public void ProcessStop(in EventLayout layout, ReadOnlySpan<byte> data, ulong eventTime, long nowMs)
    {
        if (!layout.Valid || data.Length < layout.MinimumLength)
        {
            stats.CountUnparsed();
            return;
        }

        table.ProcessStopped(
            BinaryPrimitives.ReadUInt32LittleEndian(data[layout.First..]),
            BinaryPrimitives.ReadUInt64LittleEndian(data[layout.Second..]),
            eventTime,
            nowMs);
    }
}
