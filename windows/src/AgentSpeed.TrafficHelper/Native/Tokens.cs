using AgentSpeed.TrafficHelper.Text;

namespace AgentSpeed.TrafficHelper.Native;

/// <summary>Token and SID queries for the install verbs.</summary>
internal static unsafe class Tokens
{
    /// <summary>True when this process runs with an elevated administrator token (Administrators enabled, not deny-only).</summary>
    public static bool IsElevatedAdministrator()
    {
        void* sid = null;
        if (Advapi32.ConvertStringSidToSidW(SecurityText.AdministratorsSid, &sid) == 0)
        {
            return false;
        }

        try
        {
            int member;
            return Advapi32.CheckTokenMembership(0, sid, &member) != 0 && member != 0;
        }
        finally
        {
            Kernel32.LocalFree((nint)sid);
        }
    }

    /// <summary>The user SID of a token, as a string.</summary>
    public static string? UserOf(nint token)
    {
        var buffer = stackalloc byte[256];
        uint returned;
        if (Advapi32.GetTokenInformation(token, Advapi32.TokenUserClass, buffer, 256, &returned) == 0)
        {
            return null;
        }

        // TOKEN_USER: SID_AND_ATTRIBUTES { PSID Sid; DWORD Attributes; }
        var sid = *(byte**)buffer;
        return sid == null ? null : SecurityText.SidToString(new ReadOnlySpan<byte>(sid, Math.Min(68, 8 + sid[1] * 4)));
    }

    public static int ElevationType(nint token)
    {
        int type;
        uint returned;
        return Advapi32.GetTokenInformation(token, Advapi32.TokenElevationTypeClass, &type, sizeof(int), &returned) != 0 ? type : 0;
    }

    public static nint LinkedToken(nint token)
    {
        nint linked;
        uint returned;
        return Advapi32.GetTokenInformation(token, Advapi32.TokenLinkedTokenClass, &linked, (uint)sizeof(nint), &returned) != 0 ? linked : 0;
    }

    public static nint OpenProcessToken(nint process)
    {
        nint token;
        return Advapi32.OpenProcessToken(process, Advapi32.TokenQuery, &token) != 0 ? token : 0;
    }

    /// <summary>Owner SID and DACL bytes of a file or folder (no reparse-point check here).</summary>
    public static bool TryGetFileSecurity(string path, out string? owner, out byte[]? dacl, out uint error)
    {
        owner = null;
        dacl = null;
        void* ownerSid = null, daclPointer = null, descriptor = null;
        error = Advapi32.GetNamedSecurityInfoW(
            path, Advapi32.SeFileObject, Advapi32.OwnerSecurityInformation | Advapi32.DaclSecurityInformation,
            &ownerSid, null, &daclPointer, null, &descriptor);
        if (error != 0)
        {
            return false;
        }

        try
        {
            if (ownerSid != null)
            {
                var length = Advapi32.GetLengthSid(ownerSid);
                owner = SecurityText.SidToString(new ReadOnlySpan<byte>(ownerSid, length));
            }

            if (daclPointer != null)
            {
                var size = *(ushort*)((byte*)daclPointer + 2);
                dacl = new ReadOnlySpan<byte>(daclPointer, size).ToArray();
            }

            return true;
        }
        finally
        {
            Kernel32.LocalFree((nint)descriptor);
        }
    }
}
