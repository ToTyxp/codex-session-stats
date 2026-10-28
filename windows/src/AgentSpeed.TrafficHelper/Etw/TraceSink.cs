using AgentSpeed.TrafficHelper.Counting;
using AgentSpeed.TrafficHelper.Native;

namespace AgentSpeed.TrafficHelper.Etw;

/// <summary>
/// Routes each delivered event (on the ProcessTrace thread only) to <see cref="TrafficEvents"/>, resolving the field
/// layout once per (provider, ID, version, pointer size) and dropping IDs the service does not use.
/// </summary>
internal sealed unsafe class TraceSink(TrafficEvents events)
{
    private readonly Dictionary<int, EventLayout> _layouts = new();

    public void Handle(EventRecord* record)
    {
        var id = record->Id;
        EventSource source;
        if (record->ProviderId == Providers.KernelNetwork)
        {
            if (!TrafficEventIds.IsCountedNetwork(id))
            {
                return;
            }

            source = EventSource.Network;
        }
        else if (record->ProviderId == Providers.KernelProcess)
        {
            if (id is not (TrafficEventIds.ProcessStart or TrafficEventIds.ProcessStop))
            {
                return;
            }

            source = EventSource.Process;
        }
        else
        {
            return;
        }

        var pointerSize = (record->Flags & EventRecord.Flag32BitHeader) != 0 ? 4 : 8;
        var key = (int)source << 25 | id << 9 | record->Version << 1 | (pointerSize == 4 ? 1 : 0);
        if (!_layouts.TryGetValue(key, out var layout))
        {
            layout = Resolve(record, source, id, pointerSize);
            if (_layouts.Count < 256)
            {
                _layouts[key] = layout;
            }
        }

        var data = record->UserData == null ? ReadOnlySpan<byte>.Empty : new ReadOnlySpan<byte>(record->UserData, record->UserDataLength);
        var time = (ulong)Math.Max(record->TimeStamp, 0);
        var now = Environment.TickCount64;
        if (source == EventSource.Network)
        {
            events.Network(id, layout, data, time, now);
        }
        else if (id == TrafficEventIds.ProcessStart)
        {
            events.ProcessStart(layout, data, now);
        }
        else
        {
            events.ProcessStop(layout, data, time, now);
        }
    }

    private static EventLayout Resolve(EventRecord* record, EventSource source, ushort id, int pointerSize)
    {
        var properties = TdhLayoutReader.Read(record);
        if (properties is null)
        {
            return EventLayout.Invalid;
        }

        return source == EventSource.Network
            ? EventLayoutResolver.Network(properties, pointerSize, TrafficEventIds.IsIPv6(id))
            : id == TrafficEventIds.ProcessStart
                ? EventLayoutResolver.ProcessStart(properties, pointerSize)
                : EventLayoutResolver.ProcessStop(properties, pointerSize);
    }
}
