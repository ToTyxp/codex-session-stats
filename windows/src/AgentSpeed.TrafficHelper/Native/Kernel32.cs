using System.Runtime.InteropServices;

namespace AgentSpeed.TrafficHelper.Native;

/// <summary>kernel32 entry points (handles, events, pipes, files).</summary>
internal static unsafe partial class Kernel32
{
    public const uint LoadLibrarySearchSystem32 = 0x800;
    public const uint WaitObject0 = 0;
    public const uint WaitTimeout = 0x102;
    public const uint Infinite = 0xFFFFFFFF;
    public const nint InvalidHandle = -1;

    public const uint GenericRead = 0x80000000;
    public const uint GenericWrite = 0x40000000;
    public const uint FileShareRead = 0x1;
    public const uint CreateNew = 1;
    public const uint OpenExisting = 3;
    public const uint FileAttributeNormal = 0x80;
    public const uint FileAttributeDirectory = 0x10;
    public const uint FileAttributeReparsePoint = 0x400;
    public const uint InvalidFileAttributes = 0xFFFFFFFF;

    public const uint MoveFileReplaceExisting = 0x1;
    public const uint MoveFileDelayUntilReboot = 0x4;
    public const uint MoveFileWriteThrough = 0x8;

    public const uint PipeAccessDuplex = 0x3;
    public const uint FileFlagFirstPipeInstance = 0x00080000;
    public const uint FileFlagOverlapped = 0x40000000;
    public const uint PipeTypeByte = 0;
    public const uint PipeReadModeByte = 0;
    public const uint PipeWait = 0;
    public const uint PipeRejectRemoteClients = 0x8;

    public const uint ProcessQueryLimitedInformation = 0x1000;
    public const uint Synchronize = 0x00100000;

    public const int StdOutput = -11;
    public const int StdError = -12;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial int SetDefaultDllDirectories(uint directoryFlags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial int CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint CreateEventW(nint attributes, int manualReset, int initialState, nint name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial int SetEvent(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial int ResetEvent(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint WaitForSingleObject(nint handle, uint milliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint WaitForMultipleObjects(uint count, nint* handles, int waitAll, uint milliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateNamedPipeW(
        string name, uint openMode, uint pipeMode, uint maxInstances, uint outBufferSize, uint inBufferSize,
        uint defaultTimeout, SecurityAttributes* attributes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial int ConnectNamedPipe(nint pipe, IoOverlapped* overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial int DisconnectNamedPipe(nint pipe);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial int GetNamedPipeClientSessionId(nint pipe, uint* sessionId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial int PeekNamedPipe(nint pipe, byte* buffer, uint size, uint* read, uint* totalAvailable, uint* leftThisMessage);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial int ReadFile(nint file, byte* buffer, uint toRead, uint* read, IoOverlapped* overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial int WriteFile(nint file, byte* buffer, uint toWrite, uint* written, IoOverlapped* overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial int GetOverlappedResult(nint file, IoOverlapped* overlapped, uint* transferred, int wait);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial int CancelIoEx(nint file, IoOverlapped* overlapped);

    [LibraryImport("kernel32.dll")]
    public static partial void GetSystemTimeAsFileTime(long* fileTime);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint GetModuleFileNameW(nint module, char* fileName, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateFileW(
        string name, uint access, uint share, SecurityAttributes* attributes, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial int GetFileSizeEx(nint file, long* size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial int GetFileInformationByHandle(nint file, FileInformation* information);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial int FlushFileBuffers(nint file);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MoveFileExW(string existing, string? replacement, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int DeleteFileW(string name);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int CreateDirectoryW(string name, SecurityAttributes* attributes);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int RemoveDirectoryW(string name);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint GetFileAttributesW(string name);

    [LibraryImport("kernel32.dll")]
    public static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentProcessId();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint OpenProcess(uint access, int inherit, uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial int GetProcessTimes(nint process, long* creation, long* exit, long* kernel, long* user);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint LocalFree(nint memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint GetStdHandle(int which);

    public static uint LastError => (uint)Marshal.GetLastPInvokeError();

    public static long FileTimeNow()
    {
        long now;
        GetSystemTimeAsFileTime(&now);
        return now;
    }

    public static void Close(ref nint handle)
    {
        if (handle != 0 && handle != InvalidHandle)
        {
            CloseHandle(handle);
        }

        handle = 0;
    }
}
