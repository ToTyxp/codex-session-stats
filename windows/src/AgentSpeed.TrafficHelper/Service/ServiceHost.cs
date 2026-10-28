using System.Runtime.InteropServices;
using AgentSpeed.Core.Traffic.Helper;
using AgentSpeed.TrafficHelper.Native;

namespace AgentSpeed.TrafficHelper.Service;

/// <summary>
/// The SCM side (WINDOWS-SERVICE 3.1, 3.8): StartServiceCtrlDispatcher, ServiceMain and the control handler as
/// unmanaged entry points. Start arguments are ignored. STOP and SHUTDOWN report STOP_PENDING and end the run loop;
/// the session and pipe are closed within about 2 s.
/// </summary>
internal static unsafe class ServiceHost
{
    private const uint ErrorExceptionInService = 1064;

    private static readonly Lock StatusLock = new();
    private static char* s_name;
    private static nint s_statusHandle;
    private static uint s_checkPoint;
    private static volatile ServiceRuntime? s_runtime;

    /// <summary>Connects to the SCM; returns a Win32 error (1063 when not started as a service).</summary>
    public static uint Run()
    {
        s_name = (char*)Marshal.StringToHGlobalUni(HelperProtocol.ServiceName);
        var table = stackalloc Advapi32.ServiceTableEntry[2];
        table[0].ServiceName = s_name;
        table[0].ServiceProc = &ServiceMain;
        table[1] = default;
        return Advapi32.StartServiceCtrlDispatcherW(table) != 0 ? 0 : Kernel32.LastError;
    }

    [UnmanagedCallersOnly]
    private static void ServiceMain(uint argc, char** argv)
    {
        s_statusHandle = Advapi32.RegisterServiceCtrlHandlerExW(s_name, &Handler, 0);
        if (s_statusHandle == 0)
        {
            return;
        }

        uint exitCode;
        try
        {
            Report(Advapi32.ServiceStartPending, 0, 5_000);
            using var runtime = new ServiceRuntime();
            s_runtime = runtime;
            exitCode = runtime.Run(static () => Report(Advapi32.ServiceRunning, 0, 0));
            s_runtime = null;
        }
        catch (Exception)
        {
            exitCode = ErrorExceptionInService;
        }

        Report(Advapi32.ServiceStopped, exitCode, 0);
    }

    [UnmanagedCallersOnly]
    private static uint Handler(uint control, uint eventType, nint eventData, nint context)
    {
        switch (control)
        {
            case Advapi32.ServiceControlStop:
            case Advapi32.ServiceControlShutdown:
                Report(Advapi32.ServiceStopPending, 0, 2_000);
                s_runtime?.RequestStop();
                return 0;
            case Advapi32.ServiceControlInterrogate:
                return 0;
            default:
                return Win32Error.CallNotImplemented;
        }
    }

    private static void Report(uint state, uint exitCode, uint waitHint)
    {
        lock (StatusLock)
        {
            var pending = state is Advapi32.ServiceStartPending or Advapi32.ServiceStopPending;
            var status = new Advapi32.ServiceStatus
            {
                ServiceType = Advapi32.ServiceWin32OwnProcess,
                CurrentState = state,
                ControlsAccepted = state == Advapi32.ServiceRunning ? Advapi32.ServiceAcceptStop | Advapi32.ServiceAcceptShutdown : 0,
                Win32ExitCode = exitCode,
                CheckPoint = pending ? ++s_checkPoint : 0,
                WaitHint = waitHint,
            };
            Advapi32.SetServiceStatus(s_statusHandle, &status);
        }
    }
}
