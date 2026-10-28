using System.Globalization;
using System.Text;
using AgentSpeed.Core.Traffic.Helper;
using AgentSpeed.TrafficHelper.Native;
using AgentSpeed.TrafficHelper.Text;

namespace AgentSpeed.TrafficHelper.Install;

/// <summary>
/// <c>HKLM\SOFTWARE\AgentSpeed\TrafficHelper</c> (64-bit view; default HKLM ACL: administrators write, users read):
/// Version, Protocol, AllowedUsers, InstalledAt (WINDOWS-SERVICE 2).
/// </summary>
internal static unsafe class HelperRegistry
{
    private const string ParentKey = @"SOFTWARE\AgentSpeed";
    private const string ChildName = "TrafficHelper";
    private const uint MaxValueSize = 64 * 1024;

    /// <summary>The allowed users (validated, deduplicated); empty when the key or value is missing or unreadable.</summary>
    public static List<string> ReadAllowedUsers()
    {
        nint key;
        if (Advapi32.RegOpenKeyExW(Advapi32.HkeyLocalMachine, HelperProtocol.RegistryKey, 0, Advapi32.KeyRead | Advapi32.KeyWow64_64Key, &key) != 0)
        {
            return [];
        }

        try
        {
            uint type, size = 0;
            if (Advapi32.RegQueryValueExW(key, "AllowedUsers", 0, &type, null, &size) != 0 || type != Advapi32.RegMultiSz || size > MaxValueSize)
            {
                return [];
            }

            var data = new byte[size];
            fixed (byte* buffer = data)
            {
                if (Advapi32.RegQueryValueExW(key, "AllowedUsers", 0, &type, buffer, &size) != 0 || type != Advapi32.RegMultiSz)
                {
                    return [];
                }
            }

            return MultiString.AllowedUsers(MultiString.Parse(data.AsSpan(0, (int)Math.Min(size, (uint)data.Length))));
        }
        finally
        {
            Advapi32.RegCloseKey(key);
        }
    }

    /// <summary>Writes every value of an install; returns a Win32 error (0 on success).</summary>
    public static uint WriteInstall(IReadOnlyList<string> allowedUsers)
    {
        nint key;
        uint disposition;
        var status = Advapi32.RegCreateKeyExW(
            Advapi32.HkeyLocalMachine, HelperProtocol.RegistryKey, 0, 0, Advapi32.RegOptionNonVolatile,
            Advapi32.KeySetValue | Advapi32.KeyQueryValue | Advapi32.KeyWow64_64Key, 0, &key, &disposition);
        if (status != 0)
        {
            return status;
        }

        try
        {
            status = SetString(key, "Version", HelperProtocol.BundledVersion.ToString());
            if (status == 0)
            {
                uint protocol = HelperProtocol.MaxVersion;
                status = Advapi32.RegSetValueExW(key, "Protocol", 0, Advapi32.RegDword, (byte*)&protocol, sizeof(uint));
            }

            if (status == 0)
            {
                status = SetMultiString(key, "AllowedUsers", allowedUsers);
            }

            if (status == 0)
            {
                status = SetString(key, "InstalledAt", DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            }

            return status;
        }
        finally
        {
            Advapi32.RegCloseKey(key);
        }
    }

    /// <summary>Replaces only AllowedUsers (a remove that leaves other users); returns a Win32 error.</summary>
    public static uint WriteAllowedUsers(IReadOnlyList<string> allowedUsers)
    {
        nint key;
        var status = Advapi32.RegOpenKeyExW(
            Advapi32.HkeyLocalMachine, HelperProtocol.RegistryKey, 0, Advapi32.KeySetValue | Advapi32.KeyWow64_64Key, &key);
        if (status != 0)
        {
            return status;
        }

        try
        {
            return SetMultiString(key, "AllowedUsers", allowedUsers);
        }
        finally
        {
            Advapi32.RegCloseKey(key);
        }
    }

    /// <summary>Deletes the key, and <c>SOFTWARE\AgentSpeed</c> when nothing else is left in it.</summary>
    public static uint Delete()
    {
        var status = Advapi32.RegDeleteKeyExW(Advapi32.HkeyLocalMachine, HelperProtocol.RegistryKey, Advapi32.KeyWow64_64Key, 0);
        if (status is not (0 or Win32Error.FileNotFound))
        {
            return status;
        }

        nint parent;
        if (Advapi32.RegOpenKeyExW(Advapi32.HkeyLocalMachine, ParentKey, 0, Advapi32.KeyQueryValue | Advapi32.KeyEnumerateSubKeys | Advapi32.KeyWow64_64Key, &parent) == 0)
        {
            uint subKeys = 1, values = 1;
            var empty = Advapi32.RegQueryInfoKeyW(parent, null, null, 0, &subKeys, null, null, &values, null, null, null, null) == 0
                && subKeys == 0 && values == 0;
            Advapi32.RegCloseKey(parent);
            if (empty)
            {
                Advapi32.RegDeleteKeyExW(Advapi32.HkeyLocalMachine, ParentKey, Advapi32.KeyWow64_64Key, 0);
            }
        }

        return 0;
    }

    /// <summary><c>ProgramFilesDir</c> (64-bit view), what FOLDERID_ProgramFiles resolves to; never an environment variable.</summary>
    public static string? ProgramFilesDirectory()
    {
        nint key;
        if (Advapi32.RegOpenKeyExW(Advapi32.HkeyLocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion", 0, Advapi32.KeyQueryValue | Advapi32.KeyWow64_64Key, &key) != 0)
        {
            return null;
        }

        try
        {
            var buffer = stackalloc char[300];
            uint type, size = 299 * sizeof(char);
            if (Advapi32.RegQueryValueExW(key, "ProgramFilesDir", 0, &type, (byte*)buffer, &size) != 0 || type != Advapi32.RegSz)
            {
                return null;
            }

            var text = new string(buffer, 0, (int)(size / sizeof(char))).TrimEnd('\0');
            return IsLocalAbsolute(text) ? text.TrimEnd('\\') : null;
        }
        finally
        {
            Advapi32.RegCloseKey(key);
        }
    }

    /// <summary><c>X:\…</c> with no relative segments, device or UNC prefixes.</summary>
    public static bool IsLocalAbsolute(string path) =>
        path.Length is >= 3 and < 200 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\'
        && !path.Contains("\\.", StringComparison.Ordinal) && !path.Contains('/') && path.IndexOfAny(['*', '?', '"', '<', '>', '|']) < 0;

    private static uint SetString(nint key, string name, string value)
    {
        var bytes = Encoding.Unicode.GetBytes(value + "\0");
        fixed (byte* data = bytes)
        {
            return Advapi32.RegSetValueExW(key, name, 0, Advapi32.RegSz, data, (uint)bytes.Length);
        }
    }

    private static uint SetMultiString(nint key, string name, IReadOnlyList<string> values)
    {
        var bytes = MultiString.Format(values);
        fixed (byte* data = bytes)
        {
            return Advapi32.RegSetValueExW(key, name, 0, Advapi32.RegMultiSz, data, (uint)bytes.Length);
        }
    }
}
