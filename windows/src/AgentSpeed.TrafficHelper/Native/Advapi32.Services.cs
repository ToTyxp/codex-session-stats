using System.Runtime.InteropServices;

namespace AgentSpeed.TrafficHelper.Native;

/// <summary>Service Control Manager entry points.</summary>
internal static unsafe partial class Advapi32
{
    public const uint ScManagerConnect = 0x0001;
    public const uint ScManagerCreateService = 0x0002;
    public const uint ServiceAllAccess = 0xF01FF;
    public const uint ServiceQueryStatus = 0x0004;
    public const uint ServiceStop = 0x0020;
    public const uint Delete = 0x00010000;

    public const uint ServiceWin32OwnProcess = 0x10;
    public const uint ServiceDemandStart = 3;
    public const uint ServiceErrorNormal = 1;
    public const uint ServiceNoChange = 0xFFFFFFFF;

    public const uint ServiceStopped = 1;
    public const uint ServiceStartPending = 2;
    public const uint ServiceStopPending = 3;
    public const uint ServiceRunning = 4;

    public const uint ServiceAcceptStop = 0x1;
    public const uint ServiceAcceptShutdown = 0x4;

    public const uint ServiceControlStop = 1;
    public const uint ServiceControlInterrogate = 4;
    public const uint ServiceControlShutdown = 5;

    public const uint ServiceConfigDescription = 1;
    public const uint ServiceConfigFailureActions = 2;
    public const uint ServiceConfigServiceSidInfo = 5;
    public const uint ServiceConfigRequiredPrivilegesInfo = 6;
    public const uint ServiceSidTypeUnrestricted = 1;

    public const uint ScStatusProcessInfo = 0;
    public const uint ScActionNone = 0;
    public const uint ScActionRestart = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct ServiceStatus
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ServiceStatusProcess
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
        public uint ProcessId;
        public uint ServiceFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ServiceTableEntry
    {
        public char* ServiceName;
        public delegate* unmanaged<uint, char**, void> ServiceProc;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ScAction
    {
        public uint Type;
        public uint Delay;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ServiceFailureActions
    {
        public uint ResetPeriod;
        public char* RebootMessage;
        public char* Command;
        public uint ActionCount;
        public ScAction* Actions;
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    public static partial int StartServiceCtrlDispatcherW(ServiceTableEntry* table);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    public static partial nint RegisterServiceCtrlHandlerExW(
        char* serviceName, delegate* unmanaged<uint, uint, nint, nint, uint> handler, nint context);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    public static partial int SetServiceStatus(nint statusHandle, ServiceStatus* status);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint OpenSCManagerW(string? machine, string? database, uint access);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint OpenServiceW(nint manager, string name, uint access);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateServiceW(
        nint manager, string name, string displayName, uint access, uint serviceType, uint startType, uint errorControl,
        string binaryPath, string? loadOrderGroup, nint tagId, string? dependencies, string? account, string? password);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int ChangeServiceConfigW(
        nint service, uint serviceType, uint startType, uint errorControl, string? binaryPath, string? loadOrderGroup,
        nint tagId, string? dependencies, string? account, string? password, string? displayName);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    public static partial int ChangeServiceConfig2W(nint service, uint infoLevel, void* info);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    public static partial int CloseServiceHandle(nint handle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    public static partial int ControlService(nint service, uint control, ServiceStatus* status);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    public static partial int QueryServiceStatusEx(nint service, uint infoLevel, void* buffer, uint size, uint* needed);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    public static partial int DeleteService(nint service);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    public static partial int SetServiceObjectSecurity(nint service, uint securityInformation, void* descriptor);

    public static void CloseService(ref nint handle)
    {
        if (handle != 0)
        {
            CloseServiceHandle(handle);
        }

        handle = 0;
    }
}
