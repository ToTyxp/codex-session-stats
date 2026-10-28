using System.Runtime.InteropServices;

namespace AgentSpeed.TrafficHelper.Native;

/// <summary>Event Tracing for Windows controller and consumer entry points.</summary>
internal static unsafe partial class Advapi32
{
    public const uint EventTraceControlQuery = 0;
    public const uint EventTraceControlStop = 1;
    public const uint EventControlCodeEnableProvider = 1;
    public const ulong InvalidProcessTraceHandle = ulong.MaxValue;

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint StartTraceW(ulong* sessionHandle, string sessionName, void* properties);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint ControlTraceW(ulong sessionHandle, string? sessionName, void* properties, uint controlCode);

    [LibraryImport("advapi32.dll")]
    public static partial uint EnableTraceEx2(
        ulong sessionHandle, Guid* providerId, uint controlCode, byte level, ulong matchAnyKeyword, ulong matchAllKeyword,
        uint timeout, void* enableParameters);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    public static partial ulong OpenTraceW(void* logfile);

    [LibraryImport("advapi32.dll")]
    public static partial uint ProcessTrace(ulong* handles, uint handleCount, long* startTime, long* endTime);

    [LibraryImport("advapi32.dll")]
    public static partial uint CloseTrace(ulong traceHandle);

    [LibraryImport("advapi32.dll")]
    public static partial uint EventAccessControl(Guid* guid, uint operation, void* sid, uint rights, byte allowOrDeny);

    [LibraryImport("advapi32.dll")]
    public static partial uint EventAccessRemove(Guid* guid);
}
