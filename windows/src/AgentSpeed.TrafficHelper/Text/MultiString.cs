using System.Runtime.InteropServices;
using System.Text;

namespace AgentSpeed.TrafficHelper.Text;

/// <summary>REG_MULTI_SZ encoding and the allowed-user list kept in it.</summary>
internal static class MultiString
{
    public const int MaxAllowedUsers = 64;

    /// <summary>Splits UTF-16LE data at NULs; empty strings and a missing terminator are tolerated.</summary>
    public static List<string> Parse(ReadOnlySpan<byte> data)
    {
        var chars = MemoryMarshal.Cast<byte, char>(data[..(data.Length & ~1)]);
        var values = new List<string>();
        while (!chars.IsEmpty)
        {
            var end = chars.IndexOf('\0');
            var item = end < 0 ? chars : chars[..end];
            if (!item.IsEmpty)
            {
                values.Add(item.ToString());
            }

            if (end < 0)
            {
                break;
            }

            chars = chars[(end + 1)..];
        }

        return values;
    }

    public static byte[] Format(IEnumerable<string> values)
    {
        var builder = new StringBuilder();
        foreach (var value in values)
        {
            builder.Append(value).Append('\0');
        }

        builder.Append('\0');
        return Encoding.Unicode.GetBytes(builder.ToString());
    }

    /// <summary>Valid user SIDs only, normalized, without duplicates, at most <see cref="MaxAllowedUsers"/>.</summary>
    public static List<string> AllowedUsers(IEnumerable<string> values)
    {
        var users = new List<string>();
        foreach (var value in values)
        {
            if (SidText.TryNormalizeUserSid(value, out var sid) && !users.Contains(sid, StringComparer.OrdinalIgnoreCase))
            {
                users.Add(sid);
                if (users.Count == MaxAllowedUsers)
                {
                    break;
                }
            }
        }

        return users;
    }
}
