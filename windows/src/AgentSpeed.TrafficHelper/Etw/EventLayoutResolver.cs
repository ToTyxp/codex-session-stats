namespace AgentSpeed.TrafficHelper.Etw;

/// <summary>One top-level property of an event template as TDH describes it (EVENT_PROPERTY_INFO).</summary>
internal readonly record struct EventProperty(string Name, ushort InType, uint Flags, ushort Count, ushort Length);

/// <summary>
/// Computes field offsets from an event's property list (WINDOWS-SERVICE 3.5): offsets are the sums of the preceding
/// fixed-size properties; the first variable-size property (string, SID, struct, parameter-sized array) ends the
/// resolvable prefix, so fields after it are treated as missing.
/// </summary>
internal static class EventLayoutResolver
{
    private const uint PropertyStruct = 0x1;
    private const uint PropertyParamLength = 0x2;
    private const uint PropertyParamCount = 0x4;

    private const ushort InTypeInt8 = 3;
    private const ushort InTypeUInt8 = 4;
    private const ushort InTypeInt16 = 5;
    private const ushort InTypeUInt16 = 6;
    private const ushort InTypeInt32 = 7;
    private const ushort InTypeUInt32 = 8;
    private const ushort InTypeInt64 = 9;
    private const ushort InTypeUInt64 = 10;
    private const ushort InTypeFloat = 11;
    private const ushort InTypeDouble = 12;
    private const ushort InTypeBoolean = 13;
    private const ushort InTypeBinary = 14;
    private const ushort InTypeGuid = 15;
    private const ushort InTypePointer = 16;
    private const ushort InTypeFileTime = 17;
    private const ushort InTypeSystemTime = 18;
    private const ushort InTypeHexInt32 = 20;
    private const ushort InTypeHexInt64 = 21;
    private const ushort InTypeSizeT = 308;

    /// <summary>The network layout (PID u32, size u32, daddr, saddr of 4 or 16 bytes) or <see cref="EventLayout.Invalid"/>.</summary>
    public static EventLayout Network(IReadOnlyList<EventProperty> properties, int pointerSize, bool ipv6)
    {
        var addressLength = ipv6 ? 16 : 4;
        return Find(properties, pointerSize, "PID", 4, out var pid)
            && Find(properties, pointerSize, "size", 4, out var size)
            && Find(properties, pointerSize, "daddr", addressLength, out var daddr)
            && Find(properties, pointerSize, "saddr", addressLength, out var saddr)
            ? EventLayout.Network(pid, size, daddr, saddr, addressLength)
            : EventLayout.Invalid;
    }

    /// <summary>The process-start layout (ProcessID, CreateTime, ParentProcessID, SessionID).</summary>
    public static EventLayout ProcessStart(IReadOnlyList<EventProperty> properties, int pointerSize) =>
        Find(properties, pointerSize, "ProcessID", 4, out var pid)
        && Find(properties, pointerSize, "CreateTime", 8, out var create)
        && Find(properties, pointerSize, "ParentProcessID", 4, out var parent)
        && Find(properties, pointerSize, "SessionID", 4, out var session)
            ? EventLayout.Process(pid, create, parent, session)
            : EventLayout.Invalid;

    /// <summary>The process-stop layout (ProcessID, CreateTime); the parent and session slots repeat the PID offset.</summary>
    public static EventLayout ProcessStop(IReadOnlyList<EventProperty> properties, int pointerSize) =>
        Find(properties, pointerSize, "ProcessID", 4, out var pid) && Find(properties, pointerSize, "CreateTime", 8, out var create)
            ? EventLayout.Process(pid, create, pid, pid)
            : EventLayout.Invalid;

    /// <summary>The offset of <paramref name="name"/> when it lies in the fixed-size prefix and has <paramref name="size"/> bytes.</summary>
    public static bool Find(IReadOnlyList<EventProperty> properties, int pointerSize, string name, int size, out int offset)
    {
        offset = 0;
        var position = 0;
        foreach (var property in properties)
        {
            var length = FixedSize(property, pointerSize);
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                if (length != size)
                {
                    return false;
                }

                offset = position;
                return true;
            }

            if (length < 0)
            {
                return false;
            }

            position += length;
            if (position > 0xFFFF)
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>The byte size of a fixed-size property, or -1 when its size depends on the data.</summary>
    public static int FixedSize(in EventProperty property, int pointerSize)
    {
        if ((property.Flags & (PropertyStruct | PropertyParamLength | PropertyParamCount)) != 0)
        {
            return -1;
        }

        var element = property.InType switch
        {
            InTypeInt8 or InTypeUInt8 => 1,
            InTypeInt16 or InTypeUInt16 => 2,
            InTypeInt32 or InTypeUInt32 or InTypeHexInt32 or InTypeFloat or InTypeBoolean => 4,
            InTypeInt64 or InTypeUInt64 or InTypeHexInt64 or InTypeDouble or InTypeFileTime => 8,
            InTypeGuid or InTypeSystemTime => 16,
            InTypePointer or InTypeSizeT => pointerSize,
            InTypeBinary => property.Length,
            _ => -1,
        };
        if (element <= 0)
        {
            return -1;
        }

        var count = property.Count == 0 ? 1 : property.Count;
        return element * count;
    }
}
