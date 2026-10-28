namespace AgentSpeed.TrafficHelper.Etw;

/// <summary>Which provider an event comes from.</summary>
internal enum EventSource : byte
{
    Network = 1,
    Process = 2,
}

/// <summary>
/// The resolved user-data offsets of one (provider, event ID, version): for network events PID, size, daddr and saddr;
/// for process events ProcessID, CreateTime, ParentProcessID and SessionID. <see cref="Valid"/> false when the layout
/// lacks a field (such events are counted as unparsed).
/// </summary>
internal readonly record struct EventLayout(bool Valid, int First, int Second, int Third, int Fourth, int AddressLength, int MinimumLength)
{
    public static EventLayout Invalid { get; } = new(false, 0, 0, 0, 0, 0, 0);

    public static EventLayout Network(int pid, int size, int daddr, int saddr, int addressLength)
    {
        var minimum = Math.Max(Math.Max(pid + 4, size + 4), Math.Max(daddr, saddr) + addressLength);
        return new EventLayout(true, pid, size, daddr, saddr, addressLength, minimum);
    }

    public static EventLayout Process(int processId, int createTime, int parentProcessId, int sessionId)
    {
        var minimum = Math.Max(Math.Max(processId + 4, createTime + 8), Math.Max(parentProcessId, sessionId) + 4);
        return new EventLayout(true, processId, createTime, parentProcessId, sessionId, 0, minimum);
    }
}

/// <summary>The Kernel-Network and Kernel-Process event IDs the service uses (WINDOWS-SERVICE 3.3).</summary>
internal static class TrafficEventIds
{
    public static ReadOnlySpan<ushort> Network => [10, 11, 26, 27, 42, 43, 58, 59];

    public static ReadOnlySpan<ushort> Process => [1, 2];

    public const ushort ProcessStart = 1;
    public const ushort ProcessStop = 2;

    public static bool IsCountedNetwork(ushort id) => id is 10 or 11 or 26 or 27 or 42 or 43 or 58 or 59;

    public static bool IsReceive(ushort id) => id is 11 or 27 or 43 or 59;

    public static bool IsIPv6(ushort id) => id is 26 or 27 or 58 or 59;
}
