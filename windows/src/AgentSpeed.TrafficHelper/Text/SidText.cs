using System.Text;

namespace AgentSpeed.TrafficHelper.Text;

/// <summary>
/// Validates SID strings that may become allowed users: canonical <c>S-1-5-21-…</c> (local and domain accounts, including
/// Microsoft accounts) or <c>S-1-12-1-…</c> (Microsoft Entra ID) only, so a group such as Everyone or Users can never be
/// granted pipe or service access. Only ASCII digits and dashes pass, which also keeps SDDL built from them well formed.
/// </summary>
internal static class SidText
{
    public const int MaxLength = 184;
    private const int MaxSubAuthorities = 15;

    public static bool TryNormalizeUserSid(string? text, out string sid)
    {
        sid = "";
        if (string.IsNullOrEmpty(text) || text.Length > MaxLength)
        {
            return false;
        }

        var upper = text.Length > 0 && text[0] == 's' ? "S" + text[1..] : text;
        var isAccount = upper.StartsWith("S-1-5-21-", StringComparison.Ordinal);
        var isEntra = upper.StartsWith("S-1-12-1-", StringComparison.Ordinal);
        if (!isAccount && !isEntra)
        {
            return false;
        }

        var parts = upper[2..].Split('-');
        // parts[0] is the revision, parts[1] the authority, the rest sub-authorities.
        var subAuthorities = parts.Length - 2;
        if (subAuthorities < (isAccount ? 4 : 2) || subAuthorities > MaxSubAuthorities)
        {
            return false;
        }

        var builder = new StringBuilder("S", upper.Length);
        foreach (var part in parts)
        {
            if (!TryParseDecimal(part, out var value))
            {
                return false;
            }

            builder.Append('-').Append(value);
        }

        var normalized = builder.ToString();
        if (!string.Equals(normalized, upper, StringComparison.Ordinal))
        {
            // Leading zeros or other non-canonical spellings are refused rather than rewritten.
            return false;
        }

        sid = normalized;
        return true;
    }

    private static bool TryParseDecimal(string part, out uint value)
    {
        value = 0;
        if (part.Length is 0 or > 10)
        {
            return false;
        }

        ulong accumulator = 0;
        foreach (var ch in part)
        {
            if (ch is < '0' or > '9')
            {
                return false;
            }

            accumulator = accumulator * 10 + (uint)(ch - '0');
        }

        if (accumulator > uint.MaxValue)
        {
            return false;
        }

        value = (uint)accumulator;
        return true;
    }
}
