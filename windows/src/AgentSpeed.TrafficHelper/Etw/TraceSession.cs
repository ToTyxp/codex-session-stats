using AgentSpeed.Core.Traffic.Helper;
using AgentSpeed.TrafficHelper.Native;

namespace AgentSpeed.TrafficHelper.Etw;

/// <summary>
/// The real-time session <c>AgentSpeed-Traffic</c> (WINDOWS-SERVICE 2, 3.2, 3.3): 64 KB buffers, 4–32 buffers, 1 s flush,
/// QPC clock, the fixed GUID whose descriptor <see cref="SessionSecurity"/> sets; Kernel-Network (level 4, keywords IPv4|IPv6) and Kernel-Process (level 4, keyword process), each with an
/// event-ID filter, enabled without it when the provider rejects the filter.
/// </summary>
internal sealed unsafe class TraceSession : IDisposable
{
    private const uint QpcClock = 1;
    private const byte LevelInformation = 4;
    private const ulong NetworkKeywords = 0x30;
    private const ulong ProcessKeywords = 0x10;

    private ulong _handle;

    public bool IsStarted => _handle != 0;

    /// <summary>Stops a session with the service's name left behind by a crashed instance (or found at removal).</summary>
    public static uint StopLeftover()
    {
        var properties = EventTraceProperties.Empty();
        return Advapi32.ControlTraceW(0, HelperProtocol.EtwSessionName, &properties, Advapi32.EventTraceControlStop);
    }

    /// <summary>Starts the session and enables both providers; returns a Win32 error (0 on success).</summary>
    public uint Start()
    {
        var properties = NewProperties();
        ulong handle;
        var status = Advapi32.StartTraceW(&handle, HelperProtocol.EtwSessionName, &properties);
        if (status == Win32Error.AlreadyExists)
        {
            StopLeftover();
            properties = NewProperties();
            status = Advapi32.StartTraceW(&handle, HelperProtocol.EtwSessionName, &properties);
        }

        if (status != 0)
        {
            return status;
        }

        _handle = handle;
        status = Enable(Providers.KernelNetwork, NetworkKeywords, TrafficEventIds.Network);
        if (status == 0)
        {
            status = Enable(Providers.KernelProcess, ProcessKeywords, TrafficEventIds.Process);
        }

        if (status != 0)
        {
            Stop();
        }

        return status;
    }

    /// <summary>Events and real-time buffers the session lost so far; false when the query failed.</summary>
    public bool TryQueryLost(out ulong lost)
    {
        lost = 0;
        if (_handle == 0)
        {
            return false;
        }

        var properties = EventTraceProperties.Empty();
        if (Advapi32.ControlTraceW(_handle, null, &properties, Advapi32.EventTraceControlQuery) != 0)
        {
            return false;
        }

        lost = (ulong)properties.EventsLost + properties.RealTimeBuffersLost;
        return true;
    }

    public void Stop()
    {
        if (_handle == 0)
        {
            return;
        }

        var properties = EventTraceProperties.Empty();
        Advapi32.ControlTraceW(_handle, null, &properties, Advapi32.EventTraceControlStop);
        _handle = 0;
    }

    public void Dispose() => Stop();

    private static EventTraceProperties NewProperties()
    {
        var properties = EventTraceProperties.Empty();
        properties.WnodeGuid = SessionSecurity.SessionGuid;
        properties.WnodeClientContext = QpcClock;
        properties.WnodeFlags = EventTraceProperties.WnodeFlagTracedGuid;
        properties.BufferSize = 64;
        properties.MinimumBuffers = 4;
        properties.MaximumBuffers = 32;
        properties.LogFileMode = EventTraceProperties.EventTraceRealTimeMode;
        properties.FlushTimer = 1;
        return properties;
    }

    private uint Enable(Guid provider, ulong keywords, ReadOnlySpan<ushort> ids)
    {
        // EVENT_FILTER_EVENT_ID: BOOLEAN FilterIn, UCHAR Reserved, USHORT Count, USHORT Events[Count].
        var filterSize = 4 + ids.Length * 2;
        var filter = stackalloc byte[filterSize];
        filter[0] = 1;
        filter[1] = 0;
        *(ushort*)(filter + 2) = (ushort)ids.Length;
        for (var i = 0; i < ids.Length; i++)
        {
            *(ushort*)(filter + 4 + i * 2) = ids[i];
        }

        var descriptor = new EventFilterDescriptor { Ptr = (ulong)filter, Size = (uint)filterSize, Type = EventFilterDescriptor.TypeEventId };
        var parameters = new EnableTraceParameters
        {
            Version = EnableTraceParameters.Version2,
            EnableFilterDesc = &descriptor,
            FilterDescCount = 1,
        };
        var status = Advapi32.EnableTraceEx2(
            _handle, &provider, Advapi32.EventControlCodeEnableProvider, LevelInformation, keywords, 0, 0, &parameters);
        if (status != 0)
        {
            // The callback drops the other IDs anyway (WINDOWS-SERVICE 3.3).
            status = Advapi32.EnableTraceEx2(
                _handle, &provider, Advapi32.EventControlCodeEnableProvider, LevelInformation, keywords, 0, 0, null);
        }

        return status;
    }
}
