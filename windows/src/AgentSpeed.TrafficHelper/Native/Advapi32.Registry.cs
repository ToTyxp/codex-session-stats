using System.Runtime.InteropServices;

namespace AgentSpeed.TrafficHelper.Native;

/// <summary>Registry entry points (always the 64-bit view of HKLM).</summary>
internal static unsafe partial class Advapi32
{
    public static readonly nint HkeyLocalMachine = unchecked((nint)(int)0x80000002);

    public const uint KeyQueryValue = 0x0001;
    public const uint KeySetValue = 0x0002;
    public const uint KeyEnumerateSubKeys = 0x0008;
    public const uint KeyRead = 0x20019;
    public const uint KeyWow64_64Key = 0x0100;
    public const uint RegOptionNonVolatile = 0;

    public const uint RegSz = 1;
    public const uint RegDword = 4;
    public const uint RegMultiSz = 7;

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegOpenKeyExW(nint key, string subKey, uint options, uint access, nint* result);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegCreateKeyExW(
        nint key, string subKey, uint reserved, nint className, uint options, uint access, nint attributes, nint* result,
        uint* disposition);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegQueryValueExW(nint key, string? valueName, nint reserved, uint* type, byte* data, uint* size);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegSetValueExW(nint key, string? valueName, uint reserved, uint type, byte* data, uint size);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegDeleteKeyExW(nint key, string subKey, uint access, uint reserved);

    [LibraryImport("advapi32.dll")]
    public static partial uint RegQueryInfoKeyW(
        nint key, char* className, uint* classLength, nint reserved, uint* subKeys, uint* maxSubKeyLength, uint* maxClassLength,
        uint* values, uint* maxValueNameLength, uint* maxValueLength, uint* securityDescriptorLength, long* lastWriteTime);

    [LibraryImport("advapi32.dll")]
    public static partial uint RegCloseKey(nint key);

    public static void CloseKey(ref nint key)
    {
        if (key != 0)
        {
            RegCloseKey(key);
        }

        key = 0;
    }
}
