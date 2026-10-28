using AgentSpeed.TrafficHelper.Native;
using AgentSpeed.TrafficHelper.Text;

namespace AgentSpeed.TrafficHelper.Etw;

/// <summary>
/// The security of the <c>AgentSpeed-Traffic</c> session. A session without its own GUID falls under the default session
/// descriptor, which lets Performance Log Users consume it in real time and stop it. The session therefore uses a fixed
/// GUID whose descriptor (<c>HKLM\SYSTEM\CurrentControlSet\Control\WMI\Security\{guid}</c>, set by the elevated install
/// with EventAccessControl and removed on uninstall) grants SYSTEM and Administrators full access and the service SID
/// only what the service does: create, query and stop the real-time session, enable its two providers into it, and
/// consume it.
/// </summary>
internal static unsafe class SessionSecurity
{
    public static readonly Guid SessionGuid = new(0x6ddf6feb, 0x235b, 0x49fc, 0x88, 0xb8, 0x96, 0x90, 0x7f, 0x83, 0xd9, 0xe0);

    /// <summary>WMIGUID_ALL_ACCESS (Windows XP and later), as the provider descriptors grant SYSTEM.</summary>
    public const uint FullAccess = 0x00120FFF;

    /// <summary>
    /// WMIGUID_QUERY | TRACELOG_CREATE_REALTIME | TRACELOG_GUID_ENABLE | TRACELOG_ACCESS_REALTIME: measured on build 28000,
    /// without GUID_ENABLE enabling the providers into the session fails, without QUERY or ACCESS_REALTIME its consumer
    /// receives nothing.
    /// </summary>
    public const uint ServiceAccess = 0x0001 | 0x0020 | 0x0080 | 0x0400;

    private const uint EventSecuritySetDacl = 0;
    private const uint EventSecurityAddDacl = 2;

    /// <summary>Replaces the session GUID's descriptor; returns a Win32 error (0 on success).</summary>
    public static uint Apply(string serviceSid)
    {
        var error = Grant(EventSecuritySetDacl, SecurityText.SystemSid, FullAccess);
        if (error == 0)
        {
            error = Grant(EventSecurityAddDacl, SecurityText.AdministratorsSid, FullAccess);
        }

        return error == 0 ? Grant(EventSecurityAddDacl, serviceSid, ServiceAccess) : error;
    }

    /// <summary>Deletes the session GUID's descriptor (absent counts as done); returns a Win32 error.</summary>
    public static uint Remove()
    {
        var guid = SessionGuid;
        var error = Advapi32.EventAccessRemove(&guid);
        return error is Win32Error.FileNotFound ? 0 : error;
    }

    private static uint Grant(uint operation, string sidText, uint rights)
    {
        void* sid = null;
        if (Advapi32.ConvertStringSidToSidW(sidText, &sid) == 0)
        {
            return Kernel32.LastError;
        }

        try
        {
            var guid = SessionGuid;
            return Advapi32.EventAccessControl(&guid, operation, sid, rights, 1);
        }
        finally
        {
            Kernel32.LocalFree((nint)sid);
        }
    }
}
