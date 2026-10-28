using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AgentSpeed.Presentation.Layout;

namespace AgentSpeed.Interop;

/// <summary>A display in physical pixels (virtual-screen coordinates).</summary>
internal sealed record MonitorInfo(nint Handle, string DeviceName, RECT Bounds, RECT WorkArea, bool IsPrimary, uint Dpi)
{
    public double Scale => Dpi / 96.0;

    /// <summary>Stable-enough identity for remembering per-display positions.</summary>
    public string Key => $"{DeviceName}|{Bounds.Width}x{Bounds.Height}";

    public RectD WorkRect => new(WorkArea.Left, WorkArea.Top, WorkArea.Width, WorkArea.Height);

    public RectD BoundsRect => new(Bounds.Left, Bounds.Top, Bounds.Width, Bounds.Height);
}

internal static unsafe class Monitors
{
    public static IReadOnlyList<MonitorInfo> All()
    {
        var list = new List<MonitorInfo>(4);
        var handle = GCHandle.Alloc(list);
        try
        {
            User32.EnumDisplayMonitors(0, 0, &EnumProc, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        if (list.Count == 0 && FromPoint(new POINT(0, 0)) is { } primary)
        {
            list.Add(primary);
        }

        return list;
    }

    public static MonitorInfo? FromPoint(POINT point) => Describe(User32.MonitorFromPoint(point, MONITOR.DEFAULTTONEAREST));

    public static MonitorInfo? FromWindow(nint hwnd) => Describe(User32.MonitorFromWindow(hwnd, MONITOR.DEFAULTTONEAREST));

    public static MonitorInfo? FromRect(RECT rect) => Describe(User32.MonitorFromRect(rect, MONITOR.DEFAULTTONEAREST));

    /// <summary>The display's refresh rate in Hz, or null when Windows does not report one.</summary>
    public static int? RefreshRate(MonitorInfo monitor)
    {
        var mode = new DEVMODEW { dmSize = (ushort)sizeof(DEVMODEW) };
        const int CurrentSettings = -1;
        return User32.EnumDisplaySettings(monitor.DeviceName, CurrentSettings, ref mode) && mode.dmDisplayFrequency > 1
            ? (int)mode.dmDisplayFrequency
            : null;
    }

    public static MonitorInfo? Primary() => All().FirstOrDefault(m => m.IsPrimary) ?? FromPoint(new POINT(0, 0));

    public static MonitorInfo? Describe(nint monitor)
    {
        if (monitor == 0)
        {
            return null;
        }

        var info = new MONITORINFOEX { cbSize = (uint)sizeof(MONITORINFOEX) };
        if (!User32.GetMonitorInfo(monitor, ref info))
        {
            return null;
        }

        var device = new string(info.szDevice);
        var terminator = device.IndexOf('\0');
        if (terminator >= 0)
        {
            device = device[..terminator];
        }

        uint dpi = 96;
        try
        {
            if (Shcore.GetDpiForMonitor(monitor, Shcore.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0)
            {
                dpi = dpiX;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }

        return new MonitorInfo(monitor, device, info.rcMonitor, info.rcWork,
            (info.dwFlags & MONITOR.MONITORINFOF_PRIMARY) != 0, dpi);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int EnumProc(nint monitor, nint hdc, RECT* clip, nint data)
    {
        try
        {
            if (GCHandle.FromIntPtr(data).Target is List<MonitorInfo> list && Describe(monitor) is { } info)
            {
                list.Add(info);
            }
        }
        catch
        {
            // Never let an exception cross the native boundary.
        }

        return 1;
    }
}
