using AgentSpeed.TrafficHelper.Native;
using AgentSpeed.TrafficHelper.Text;

namespace AgentSpeed.TrafficHelper.Service;

/// <summary>
/// Lets the allowed users verify the pipe server (WINDOWS-SERVICE 4, client step 2): a LocalService process's default
/// DACL grants only its logon SID and Administrators, so the client could not open it for
/// <c>QueryFullProcessImageName</c>. The service adds <c>PROCESS_QUERY_LIMITED_INFORMATION</c> (image name, times, exit
/// code — nothing that reads or changes the process) for each allowed user to its own process DACL.
/// </summary>
internal static unsafe class ProcessAccess
{
    private const uint SeKernelObject = 6;

    public static uint GrantQueryLimited(IReadOnlyList<string> allowedUsers)
    {
        if (allowedUsers.Count == 0)
        {
            return 0;
        }

        var process = Kernel32.GetCurrentProcess();
        void* dacl = null, descriptor = null;
        var error = Advapi32.GetSecurityInfo(process, SeKernelObject, Advapi32.DaclSecurityInformation, null, null, &dacl, null, &descriptor);
        if (error != 0)
        {
            return error;
        }

        string? current;
        try
        {
            current = Advapi32.DescriptorToSddl(descriptor, Advapi32.DaclSecurityInformation);
        }
        finally
        {
            Kernel32.LocalFree((nint)descriptor);
        }

        if (current is null || !current.StartsWith("D:", StringComparison.Ordinal))
        {
            return Kernel32.LastError is 0 ? Win32Error.InvalidParameter : Kernel32.LastError;
        }

        using var updated = SecurityDescriptor.FromSddl(SecurityText.ProcessQuerySddl(current, allowedUsers), out error);
        if (updated is null)
        {
            return error;
        }

        void* newDacl;
        int present, defaulted;
        if (Advapi32.GetSecurityDescriptorDacl(updated.Pointer, &present, &newDacl, &defaulted) == 0 || present == 0)
        {
            return Kernel32.LastError;
        }

        return Advapi32.SetSecurityInfo(process, SeKernelObject, Advapi32.DaclSecurityInformation, null, null, newDacl, null);
    }
}
