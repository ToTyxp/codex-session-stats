using System.Runtime.InteropServices;

namespace AgentSpeed.TrafficHelper.Native;

/// <summary>Win32 error codes used by the helper.</summary>
internal static class Win32Error
{
    public const uint Success = 0;
    public const uint FileNotFound = 2;
    public const uint PathNotFound = 3;
    public const uint AccessDenied = 5;
    public const uint SharingViolation = 32;
    public const uint InvalidParameter = 87;
    public const uint InsufficientBuffer = 122;
    public const uint AlreadyExists = 183;
    public const uint MoreData = 234;
    public const uint PipeBusy = 231;
    public const uint NoData = 232;
    public const uint PipeNotConnected = 233;
    public const uint BrokenPipe = 109;
    public const uint PipeConnected = 535;
    public const uint IoPending = 997;
    public const uint OperationAborted = 995;
    public const uint NotFound = 1168;
    public const uint CallNotImplemented = 120;
    public const uint ServiceCannotAcceptControl = 1061;
    public const uint ServiceNotActive = 1062;
    public const uint FailedServiceControllerConnect = 1063;
    public const uint ServiceDoesNotExist = 1060;
    public const uint ServiceExists = 1073;
    public const uint ServiceMarkedForDelete = 1072;
    public const uint Timeout = 1460;
    public const uint WmiInstanceNotFound = 4201;
    public const uint NotSupported = 50;
}

/// <summary>OVERLAPPED (named apart from System.Threading.Overlapped).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct IoOverlapped
{
    public nuint Internal;
    public nuint InternalHigh;
    public uint Offset;
    public uint OffsetHigh;
    public nint EventHandle;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct SecurityAttributes
{
    public uint Length;
    public void* SecurityDescriptor;
    public int InheritHandle;
}

/// <summary>BY_HANDLE_FILE_INFORMATION (FILETIMEs are 4-byte aligned).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct FileInformation
{
    public uint FileAttributes;
    public long CreationTime;
    public long LastAccessTime;
    public long LastWriteTime;
    public uint VolumeSerialNumber;
    public uint FileSizeHigh;
    public uint FileSizeLow;
    public uint NumberOfLinks;
    public uint FileIndexHigh;
    public uint FileIndexLow;
}
