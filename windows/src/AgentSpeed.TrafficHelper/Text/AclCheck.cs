using System.Buffers.Binary;

namespace AgentSpeed.TrafficHelper.Text;

/// <summary>
/// Decides whether an install folder is safe (WINDOWS-SERVICE 5.2): its owner is Administrators, TrustedInstaller or
/// SYSTEM, and no ACE outside those (plus CREATOR OWNER, which inherits as the admin owner of what the installer creates)
/// grants any right that could change, replace or re-permission it — including inherit-only ACEs, which would apply to
/// the helper binary. Unknown allow-type ACEs with such rights fail closed.
/// </summary>
internal static class AclCheck
{
    /// <summary>Write data/append/EA/attributes, delete child, DELETE, WRITE_DAC, WRITE_OWNER, MAXIMUM_ALLOWED, GENERIC_ALL/WRITE.</summary>
    public const uint FileWriteRights = 0x2 | 0x4 | 0x10 | 0x40 | 0x100 | 0x10000 | 0x40000 | 0x80000 | 0x02000000 | 0x10000000 | 0x40000000;

    private const byte AccessAllowed = 0;
    private const byte AccessDenied = 1;
    private const byte AccessDeniedObject = 6;
    private const byte AccessAllowedCallback = 9;
    private const byte AccessDeniedCallback = 10;
    private const byte AccessDeniedCallbackObject = 12;

    private static readonly HashSet<string> TrustedOwners = new(StringComparer.OrdinalIgnoreCase)
    {
        SecurityText.AdministratorsSid,
        SecurityText.TrustedInstallerSid,
        SecurityText.SystemSid,
    };

    private static readonly HashSet<string> TrustedWriters = new(StringComparer.OrdinalIgnoreCase)
    {
        SecurityText.AdministratorsSid,
        SecurityText.TrustedInstallerSid,
        SecurityText.SystemSid,
        SecurityText.CreatorOwnerSid,
    };

    public static bool IsTrustedOwner(string? sid) => sid is not null && TrustedOwners.Contains(sid);

    /// <summary>False when the ACL is malformed or grants write-type rights to anyone untrusted; a null DACL is never safe.</summary>
    public static bool IsAdminOnlyWritable(ReadOnlySpan<byte> acl, uint writeRights = FileWriteRights)
    {
        if (acl.Length < 8)
        {
            return false;
        }

        var size = BinaryPrimitives.ReadUInt16LittleEndian(acl[2..]);
        var count = BinaryPrimitives.ReadUInt16LittleEndian(acl[4..]);
        if (size < 8 || size > acl.Length)
        {
            return false;
        }

        var offset = 8;
        for (var i = 0; i < count; i++)
        {
            if (size - offset < 8)
            {
                return false;
            }

            var ace = acl.Slice(offset, size - offset);
            var type = ace[0];
            var aceSize = BinaryPrimitives.ReadUInt16LittleEndian(ace[2..]);
            if (aceSize < 8 || aceSize > ace.Length)
            {
                return false;
            }

            ace = ace[..aceSize];
            offset += aceSize;
            if (type is AccessDenied or AccessDeniedObject or AccessDeniedCallback or AccessDeniedCallbackObject)
            {
                continue;
            }

            var mask = BinaryPrimitives.ReadUInt32LittleEndian(ace[4..]);
            if ((mask & writeRights) == 0)
            {
                continue;
            }

            if (type is not (AccessAllowed or AccessAllowedCallback))
            {
                return false;
            }

            var sid = ace[8..];
            var length = SecurityText.SidLength(sid);
            if (length == 0 || length > sid.Length || SecurityText.SidToString(sid[..length]) is not { } text
                || !TrustedWriters.Contains(text))
            {
                return false;
            }
        }

        return true;
    }
}
