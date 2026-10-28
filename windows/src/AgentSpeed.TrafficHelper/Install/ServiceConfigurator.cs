using System.Runtime.InteropServices;
using AgentSpeed.Core.Traffic.Helper;
using AgentSpeed.TrafficHelper.Native;
using AgentSpeed.TrafficHelper.Text;

namespace AgentSpeed.TrafficHelper.Install;

/// <summary>
/// Creates, reconfigures, stops and deletes the <c>AgentSpeedTraffic</c> service (WINDOWS-SERVICE 2): own process,
/// demand start, LocalService, quoted ImagePath, unrestricted service SID, SeChangeNotifyPrivilege only, restart once
/// after 5 s (reset after a day), and the service DACL for the allowed users.
/// </summary>
internal sealed unsafe class ServiceConfigurator : IDisposable
{
    private const string Account = @"NT AUTHORITY\LocalService";
    private const string RequiredPrivileges = "SeChangeNotifyPrivilege\0\0";
    private const int StopWaitMs = 5_000;

    private nint _manager;
    private nint _service;

    private ServiceConfigurator(nint manager)
    {
        _manager = manager;
    }

    public bool Exists => _service != 0;

    public static ServiceConfigurator? Open(bool create, out uint error)
    {
        var access = Advapi32.ScManagerConnect | (create ? Advapi32.ScManagerCreateService : 0);
        var manager = Advapi32.OpenSCManagerW(null, null, access);
        if (manager == 0)
        {
            error = Kernel32.LastError;
            return null;
        }

        var configurator = new ServiceConfigurator(manager);
        configurator._service = Advapi32.OpenServiceW(manager, HelperProtocol.ServiceName, Advapi32.ServiceAllAccess);
        error = configurator._service == 0 ? Kernel32.LastError : 0;
        if (configurator._service == 0 && error != Win32Error.ServiceDoesNotExist)
        {
            configurator.Dispose();
            return null;
        }

        error = 0;
        return configurator;
    }

    /// <summary>
    /// Stops a running service and waits up to 5 s in total until the SCM reports it stopped and its process has exited
    /// (so its image is no longer mapped); true when it is stopped (or does not exist).
    /// </summary>
    public bool StopAndWait()
    {
        if (_service == 0)
        {
            return true;
        }

        var deadline = Environment.TickCount64 + StopWaitMs;
        var process = OpenServiceProcess();
        try
        {
            var asked = false;
            while (true)
            {
                var state = QueryState(out _);
                if (state is 0 or Advapi32.ServiceStopped)
                {
                    return state == Advapi32.ServiceStopped && WaitForExit(process, deadline);
                }

                if (!asked && state != Advapi32.ServiceStopPending)
                {
                    Advapi32.ServiceStatus status;
                    asked = Advapi32.ControlService(_service, Advapi32.ServiceControlStop, &status) != 0
                        || Kernel32.LastError == Win32Error.ServiceNotActive;
                }

                if (Environment.TickCount64 >= deadline)
                {
                    return false;
                }

                Thread.Sleep(100);
            }
        }
        finally
        {
            Kernel32.Close(ref process);
        }
    }

    /// <summary>Creates the service or brings an existing one to the specified configuration; returns a Win32 error.</summary>
    public uint Configure(string binaryPath, IReadOnlyList<string> allowedUsers)
    {
        var quoted = "\"" + binaryPath + "\"";
        if (_service == 0)
        {
            _service = Advapi32.CreateServiceW(
                _manager, HelperProtocol.ServiceName, HelperProtocol.ServiceDisplayName, Advapi32.ServiceAllAccess,
                Advapi32.ServiceWin32OwnProcess, Advapi32.ServiceDemandStart, Advapi32.ServiceErrorNormal, quoted,
                null, 0, null, Account, "");
            if (_service == 0)
            {
                return Kernel32.LastError;
            }
        }
        else if (Advapi32.ChangeServiceConfigW(
                _service, Advapi32.ServiceWin32OwnProcess, Advapi32.ServiceDemandStart, Advapi32.ServiceErrorNormal, quoted,
                null, 0, "\0", Account, "", HelperProtocol.ServiceDisplayName) == 0)
        {
            return Kernel32.LastError;
        }

        var error = SetDescription();
        if (error == 0)
        {
            var sidInfo = Advapi32.ServiceSidTypeUnrestricted;
            error = Change(Advapi32.ServiceConfigServiceSidInfo, &sidInfo);
        }

        if (error == 0)
        {
            fixed (char* privileges = RequiredPrivileges)
            {
                var info = privileges;
                error = Change(Advapi32.ServiceConfigRequiredPrivilegesInfo, &info);
            }
        }

        if (error == 0)
        {
            error = SetFailureActions();
        }

        return error == 0 ? SetAccess(allowedUsers) : error;
    }

    /// <summary>Replaces the service DACL with the one for <paramref name="allowedUsers"/>.</summary>
    public uint SetAccess(IReadOnlyList<string> allowedUsers)
    {
        using var descriptor = SecurityDescriptor.FromSddl(SecurityText.ServiceSddl(allowedUsers), out var error);
        if (descriptor is null)
        {
            return error;
        }

        return Advapi32.SetServiceObjectSecurity(_service, Advapi32.DaclSecurityInformation, descriptor.Pointer) != 0 ? 0 : Kernel32.LastError;
    }

    /// <summary>Deletes the service (it disappears once stopped and no handles remain); returns a Win32 error.</summary>
    public uint Delete()
    {
        if (_service == 0)
        {
            return 0;
        }

        if (Advapi32.DeleteService(_service) != 0)
        {
            return 0;
        }

        var error = Kernel32.LastError;
        return error == Win32Error.ServiceMarkedForDelete ? 0 : error;
    }

    public void Dispose()
    {
        Advapi32.CloseService(ref _service);
        Advapi32.CloseService(ref _manager);
    }

    private uint QueryState(out uint processId)
    {
        Advapi32.ServiceStatusProcess status;
        uint needed;
        if (Advapi32.QueryServiceStatusEx(_service, Advapi32.ScStatusProcessInfo, &status, (uint)sizeof(Advapi32.ServiceStatusProcess), &needed) == 0)
        {
            processId = 0;
            return 0;
        }

        processId = status.ProcessId;
        return status.CurrentState;
    }

    /// <summary>A SYNCHRONIZE handle on the running service's process, or 0 when it is not running.</summary>
    private nint OpenServiceProcess()
    {
        var state = QueryState(out var processId);
        return state != Advapi32.ServiceStopped && processId > 4 ? Kernel32.OpenProcess(Kernel32.Synchronize, 0, processId) : 0;
    }

    private static bool WaitForExit(nint process, long deadline)
    {
        if (process == 0)
        {
            return true;
        }

        var remaining = Math.Max(0, deadline - Environment.TickCount64);
        return Kernel32.WaitForSingleObject(process, (uint)remaining) == Kernel32.WaitObject0;
    }

    private uint SetDescription()
    {
        var text = Marshal.StringToHGlobalUni(HelperProtocol.ServiceDescription);
        try
        {
            var info = (char*)text;
            return Change(Advapi32.ServiceConfigDescription, &info);
        }
        finally
        {
            Marshal.FreeHGlobal(text);
        }
    }

    private uint SetFailureActions()
    {
        var actions = stackalloc Advapi32.ScAction[2];
        actions[0] = new Advapi32.ScAction { Type = Advapi32.ScActionRestart, Delay = 5_000 };
        actions[1] = new Advapi32.ScAction { Type = Advapi32.ScActionNone, Delay = 0 };
        var info = new Advapi32.ServiceFailureActions { ResetPeriod = 86_400, ActionCount = 2, Actions = actions };
        return Change(Advapi32.ServiceConfigFailureActions, &info);
    }

    private uint Change(uint level, void* info) =>
        Advapi32.ChangeServiceConfig2W(_service, level, info) != 0 ? 0 : Kernel32.LastError;
}
