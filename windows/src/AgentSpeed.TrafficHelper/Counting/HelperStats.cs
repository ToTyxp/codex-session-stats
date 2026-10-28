using AgentSpeed.Core.Traffic.Helper;

namespace AgentSpeed.TrafficHelper.Counting;

/// <summary>The health counters the hello payload reports (WINDOWS-SERVICE 4). Thread-safe.</summary>
internal sealed class HelperStats
{
    private long _unparsed;
    private long _lostEarlier;
    private long _lostCurrent;
    private int _healthy;

    public ulong UnparsedEvents => (ulong)Interlocked.Read(ref _unparsed);

    /// <summary>Events and real-time buffers lost by every session of this service instance.</summary>
    public ulong EventsLost => (ulong)(Interlocked.Read(ref _lostEarlier) + Interlocked.Read(ref _lostCurrent));

    public bool SessionHealthy => Volatile.Read(ref _healthy) != 0;

    public void CountUnparsed() => Interlocked.Increment(ref _unparsed);

    public void SetHealthy(bool healthy) => Volatile.Write(ref _healthy, healthy ? 1 : 0);

    /// <summary>The current session's own lost count, as its last query reported it.</summary>
    public void SetCurrentSessionLost(ulong lost) => Interlocked.Exchange(ref _lostCurrent, (long)Math.Min(lost, long.MaxValue / 2));

    /// <summary>The current session ended: keep its losses and start counting the next one from zero.</summary>
    public void SessionEnded() => Interlocked.Add(ref _lostEarlier, Interlocked.Exchange(ref _lostCurrent, 0));

    public HelperHello Hello(ulong evictedEntries) => new(
        HelperProtocol.MinVersion,
        HelperProtocol.MaxVersion,
        HelperProtocol.BundledVersion.Packed,
        SessionHealthy ? HelperHello.SessionHealthy : 0,
        EventsLost,
        UnparsedEvents,
        evictedEntries);
}
