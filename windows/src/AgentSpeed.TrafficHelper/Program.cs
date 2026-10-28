using System.Runtime.InteropServices;
using AgentSpeed.Core.Traffic.Helper;
using AgentSpeed.TrafficHelper.Install;
using AgentSpeed.TrafficHelper.Native;
using AgentSpeed.TrafficHelper.Service;
using AgentSpeed.TrafficHelper.Text;

[assembly: System.Runtime.CompilerServices.DisableRuntimeMarshalling]
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace AgentSpeed.TrafficHelper;

/// <summary>
/// Entry point (WINDOWS-SERVICE 3, 5): no arguments → the service; <c>install</c> / <c>remove</c> → one elevated
/// action with exit code 0 ok, 1 refused, 2 failed; anything else → usage, exit 1.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (OperatingSystem.IsWindows())
        {
            Kernel32.SetDefaultDllDirectories(Kernel32.LoadLibrarySearchSystem32);
        }

        var command = CommandLine.Parse(args);
        if (!OperatingSystem.IsWindows() && command.Verb != HelperVerb.Usage)
        {
            Output.Error("The traffic helper runs on Windows only.");
            return (int)HelperExitCode.Failed;
        }

        switch (command.Verb)
        {
            case HelperVerb.Service:
                var error = ServiceHost.Run();
                if (error == Win32Error.FailedServiceControllerConnect)
                {
                    Output.Error(CommandLine.Usage);
                    return (int)HelperExitCode.Refused;
                }

                return error == 0 ? 0 : (int)HelperExitCode.Failed;
            case HelperVerb.Install:
                return (int)Installer.Install(command.UserSid!, command.Sha256!);
            case HelperVerb.Remove:
                return (int)Installer.Remove(command.UserSid!);
            default:
                Output.Error(CommandLine.Usage);
                return (int)HelperExitCode.Refused;
        }
    }
}
