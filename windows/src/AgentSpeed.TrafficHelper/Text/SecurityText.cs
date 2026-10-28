using System.Buffers.Binary;
using System.Text;
using AgentSpeed.Core.Traffic.Helper;

namespace AgentSpeed.TrafficHelper.Text;

/// <summary>The SDDL strings of the pipe and the service object, and SID helpers (WINDOWS-SERVICE 2, 4).</summary>
internal static class SecurityText
{
    public const string SystemSid = "S-1-5-18";
    public const string AdministratorsSid = "S-1-5-32-544";
    public const string TrustedInstallerSid = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";
    public const string CreatorOwnerSid = "S-1-3-0";

    /// <summary><c>FILE_GENERIC_READ | FILE_WRITE_DATA</c>: read responses, write requests, no create-instance right.</summary>
    public const uint PipeClientAccess = 0x0012008B;

    /// <summary><c>SERVICE_QUERY_STATUS | SERVICE_START | SERVICE_STOP | SERVICE_INTERROGATE | READ_CONTROL</c>.</summary>
    public const uint ServiceUserAccess = 0x000200B4;

    public const uint ServiceAllAccess = 0x000F01FF;
    public const uint ServiceQueryStatus = 0x00000004;

    /// <summary>Medium integrity, no write-up and no read-up: low-integrity and AppContainer processes cannot open the pipe.</summary>
    public const string MediumLabel = "S:(ML;;NWNR;;;ME)";

    /// <summary>
    /// Owner = the service SID; protected DACL: deny NETWORK, SYSTEM and the service full, each allowed user read + write
    /// data; an explicit medium mandatory label with no read-up.
    /// </summary>
    public static string PipeSddl(string serviceSid, IEnumerable<string> allowedUsers)
    {
        var builder = new StringBuilder();
        builder.Append("O:").Append(serviceSid).Append("D:P(D;;GA;;;NU)(A;;GA;;;SY)(A;;GA;;;").Append(serviceSid).Append(')');
        foreach (var user in allowedUsers)
        {
            builder.Append("(A;;0x").Append(PipeClientAccess.ToString("x", null)).Append(";;;").Append(user).Append(')');
        }

        return builder.Append(MediumLabel).ToString();
    }

    /// <summary>SYSTEM and Administrators full, each allowed user query/start/stop/interrogate/read-control, Authenticated Users query.</summary>
    public static string ServiceSddl(IEnumerable<string> allowedUsers)
    {
        var builder = new StringBuilder();
        builder.Append("D:(A;;0x").Append(ServiceAllAccess.ToString("x", null)).Append(";;;SY)");
        builder.Append("(A;;0x").Append(ServiceAllAccess.ToString("x", null)).Append(";;;BA)");
        foreach (var user in allowedUsers)
        {
            builder.Append("(A;;0x").Append(ServiceUserAccess.ToString("x", null)).Append(";;;").Append(user).Append(')');
        }

        builder.Append("(A;;0x").Append(ServiceQueryStatus.ToString("x", null)).Append(";;;AU)");
        return builder.ToString();
    }

    /// <summary><c>PROCESS_QUERY_LIMITED_INFORMATION</c>: what the client needs for <c>QueryFullProcessImageName</c>.</summary>
    public const uint ProcessQueryLimited = 0x1000;

    /// <summary>The process DACL <paramref name="currentDacl"/> (SDDL, <c>D:…</c>) plus query-limited access for each allowed user.</summary>
    public static string ProcessQuerySddl(string currentDacl, IEnumerable<string> allowedUsers)
    {
        var builder = new StringBuilder(currentDacl);
        foreach (var user in allowedUsers)
        {
            builder.Append("(A;;0x").Append(ProcessQueryLimited.ToString("x", null)).Append(";;;").Append(user).Append(')');
        }

        return builder.ToString();
    }

    /// <summary>The service SID <c>S-1-5-80-…</c> from SHA-1 of the upper-case service name in UTF-16LE (what <c>sc showsid</c> prints).</summary>
    public static string ServiceSidFromHash(ReadOnlySpan<byte> sha1)
    {
        var builder = new StringBuilder("S-1-5-80");
        for (var i = 0; i < 5; i++)
        {
            builder.Append('-').Append(BinaryPrimitives.ReadUInt32LittleEndian(sha1[(i * 4)..]));
        }

        return builder.ToString();
    }

    public static byte[] ServiceNameBytes(string serviceName = HelperProtocol.ServiceName) =>
        Encoding.Unicode.GetBytes(serviceName.ToUpperInvariant());

    /// <summary>A binary SID (as in ACEs and tokens) to its string form; null when malformed.</summary>
    public static string? SidToString(ReadOnlySpan<byte> sid)
    {
        if (sid.Length < 8 || sid[0] != 1)
        {
            return null;
        }

        int count = sid[1];
        if (count > 15 || sid.Length < 8 + count * 4)
        {
            return null;
        }

        ulong authority = 0;
        for (var i = 2; i < 8; i++)
        {
            authority = authority << 8 | sid[i];
        }

        var builder = new StringBuilder("S-1-");
        builder.Append(authority);
        for (var i = 0; i < count; i++)
        {
            builder.Append('-').Append(BinaryPrimitives.ReadUInt32LittleEndian(sid[(8 + i * 4)..]));
        }

        return builder.ToString();
    }

    public static int SidLength(ReadOnlySpan<byte> sid) => sid.Length < 2 ? 0 : 8 + sid[1] * 4;

    /// <summary>Parses exactly 64 hex digits (either case) into 32 bytes.</summary>
    public static bool TryParseSha256(string? text, out byte[] hash)
    {
        hash = [];
        if (text is null || text.Length != 64)
        {
            return false;
        }

        var bytes = new byte[32];
        for (var i = 0; i < 32; i++)
        {
            var high = HexValue(text[i * 2]);
            var low = HexValue(text[i * 2 + 1]);
            if (high < 0 || low < 0)
            {
                return false;
            }

            bytes[i] = (byte)(high << 4 | low);
        }

        hash = bytes;
        return true;
    }

    public static string ToHex(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(bytes);

    private static int HexValue(char ch) => ch switch
    {
        >= '0' and <= '9' => ch - '0',
        >= 'a' and <= 'f' => ch - 'a' + 10,
        >= 'A' and <= 'F' => ch - 'A' + 10,
        _ => -1,
    };
}
