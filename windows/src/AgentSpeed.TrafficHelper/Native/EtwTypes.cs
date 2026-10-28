using System.Runtime.InteropServices;

namespace AgentSpeed.TrafficHelper.Native;

/// <summary>The parts of EVENT_RECORD (64-bit layout, 112 bytes) the service reads.</summary>
[StructLayout(LayoutKind.Explicit, Size = 112)]
internal unsafe struct EventRecord
{
    public const ushort Flag32BitHeader = 0x20;

    [FieldOffset(4)] public ushort Flags;
    [FieldOffset(16)] public long TimeStamp;
    [FieldOffset(24)] public Guid ProviderId;
    [FieldOffset(40)] public ushort Id;
    [FieldOffset(42)] public byte Version;
    [FieldOffset(86)] public ushort UserDataLength;
    [FieldOffset(96)] public byte* UserData;
}

/// <summary>EVENT_TRACE_LOGFILEW (64-bit layout, 448 bytes): only the fields a real-time consumer sets.</summary>
[StructLayout(LayoutKind.Explicit, Size = 448)]
internal unsafe struct EventTraceLogfile
{
    public const uint ProcessTraceModeRealTime = 0x00000100;
    public const uint ProcessTraceModeEventRecord = 0x10000000;

    [FieldOffset(8)] public char* LoggerName;
    [FieldOffset(28)] public uint ProcessTraceMode;
    [FieldOffset(424)] public delegate* unmanaged<EventRecord*, void> EventRecordCallback;
}

/// <summary>EVENT_TRACE_PROPERTIES (120 bytes) followed by room for the session name.</summary>
[StructLayout(LayoutKind.Explicit, Size = TotalSize)]
internal struct EventTraceProperties
{
    public const int HeaderSize = 120;
    public const int TotalSize = HeaderSize + 512;
    public const uint WnodeFlagTracedGuid = 0x00020000;
    public const uint EventTraceRealTimeMode = 0x00000100;

    [FieldOffset(0)] public uint WnodeBufferSize;
    [FieldOffset(24)] public Guid WnodeGuid;
    [FieldOffset(40)] public uint WnodeClientContext;
    [FieldOffset(44)] public uint WnodeFlags;
    [FieldOffset(48)] public uint BufferSize;
    [FieldOffset(52)] public uint MinimumBuffers;
    [FieldOffset(56)] public uint MaximumBuffers;
    [FieldOffset(64)] public uint LogFileMode;
    [FieldOffset(68)] public uint FlushTimer;
    [FieldOffset(88)] public uint EventsLost;
    [FieldOffset(96)] public uint LogBuffersLost;
    [FieldOffset(100)] public uint RealTimeBuffersLost;
    [FieldOffset(112)] public uint LogFileNameOffset;
    [FieldOffset(116)] public uint LoggerNameOffset;

    /// <summary>An empty property block whose name area can receive the session name.</summary>
    public static EventTraceProperties Empty() => new() { WnodeBufferSize = TotalSize, LoggerNameOffset = HeaderSize };
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct EnableTraceParameters
{
    public const uint Version2 = 2;

    public uint Version;
    public uint EnableProperty;
    public uint ControlFlags;
    public Guid SourceId;
    public EventFilterDescriptor* EnableFilterDesc;
    public uint FilterDescCount;
}

[StructLayout(LayoutKind.Sequential)]
internal struct EventFilterDescriptor
{
    public const uint TypeEventId = 0x80000200;

    public ulong Ptr;
    public uint Size;
    public uint Type;
}

/// <summary>The provider GUIDs (WINDOWS-SERVICE 1).</summary>
internal static class Providers
{
    public static readonly Guid KernelNetwork = new(0x7DD42A49, 0x5329, 0x4832, 0x8D, 0xFD, 0x43, 0xD9, 0x79, 0x15, 0x3A, 0x88);
    public static readonly Guid KernelProcess = new(0x22FB2CD6, 0x0E7B, 0x422B, 0xA0, 0xC7, 0x2F, 0xAD, 0x1F, 0xD0, 0xE7, 0x16);
}
