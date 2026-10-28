using AgentSpeed.TrafficHelper.Native;

namespace AgentSpeed.TrafficHelper.Install;

/// <summary>
/// Confirms that <c>--user</c> names a user account (WINDOWS-SERVICE 2, allowed users): the SID form check alone also
/// passes domain groups such as Domain Users (<c>S-1-5-21-…-513</c>), so the install asks LSA for the account type.
/// </summary>
internal static unsafe class AccountLookup
{
    private const int SidTypeUser = 1;

    /// <summary>True only for SidTypeUser; an unresolvable SID (or any lookup error) is refused.</summary>
    public static bool IsUserAccount(string sidText, out uint error)
    {
        void* sid = null;
        if (Advapi32.ConvertStringSidToSidW(sidText, &sid) == 0)
        {
            error = Kernel32.LastError;
            return false;
        }

        try
        {
            var name = stackalloc char[256];
            var domain = stackalloc char[256];
            uint nameLength = 256, domainLength = 256;
            int use;
            if (Advapi32.LookupAccountSidW(null, sid, name, &nameLength, domain, &domainLength, &use) == 0)
            {
                error = Kernel32.LastError;
                return false;
            }

            error = use == SidTypeUser ? 0 : Win32Error.InvalidParameter;
            return use == SidTypeUser;
        }
        finally
        {
            Kernel32.LocalFree((nint)sid);
        }
    }
}
