using System.Runtime.InteropServices;

namespace AgentSpeed.Reporting;

/// <summary>
/// The computer's name for the default username (DESIGN 11, Default username): <c>GetComputerNameExW(ComputerNamePhysicalDnsHostname)</c>,
/// which keeps the letter case the user typed, else <see cref="Environment.MachineName"/>. Read at most once a minute.
/// </summary>
internal static partial class ReportComputerName
{
    private const int ComputerNamePhysicalDnsHostname = 5;
    private static (string? Name, long At)? _cached;

    public static string? Current
    {
        get
        {
            var now = Environment.TickCount64;
            if (_cached is { } cached && now - cached.At < 60_000)
            {
                return cached.Name;
            }

            var name = Read();
            _cached = (name, now);
            return name;
        }
    }

    private static unsafe string? Read()
    {
        if (OperatingSystem.IsWindows())
        {
            var buffer = stackalloc char[256];
            var size = 256u;
            if (GetComputerNameExW(ComputerNamePhysicalDnsHostname, buffer, ref size) && size is > 0 and < 256)
            {
                return new string(buffer, 0, (int)size);
            }
        }

        var fallback = Environment.MachineName;
        return string.IsNullOrWhiteSpace(fallback) ? null : fallback;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetComputerNameExW(int nameType, char* buffer, ref uint size);
}
