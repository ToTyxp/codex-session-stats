using System.Runtime.InteropServices;

namespace AgentSpeed.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct WINDOWPLACEMENT
{
    public uint length;
    public uint flags;
    public uint showCmd;
    public POINT ptMinPosition;
    public POINT ptMaxPosition;
    public RECT rcNormalPosition;
}

/// <summary>
/// Saves and restores a window's normal frame in physical pixels (workspace coordinates), which survives per-monitor
/// DPI differences; the system moves a frame that no longer fits any display back onto one.
/// </summary>
internal static partial class WindowPlacement
{
    private const uint SW_HIDE = 0;
    private const uint SW_SHOWMAXIMIZED = 3;

    public static bool TryGet(nint hwnd, out RECT normal, out bool maximized)
    {
        normal = default;
        maximized = false;
        if (hwnd == 0)
        {
            return false;
        }

        var placement = new WINDOWPLACEMENT { length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(hwnd, ref placement))
        {
            return false;
        }

        normal = placement.rcNormalPosition;
        maximized = placement.showCmd == SW_SHOWMAXIMIZED;
        return !normal.IsEmpty;
    }

    /// <summary>Moves a window that is not shown yet to <paramref name="normal"/> without showing it.</summary>
    public static bool TrySetHidden(nint hwnd, RECT normal)
    {
        if (hwnd == 0 || normal.IsEmpty)
        {
            return false;
        }

        var placement = new WINDOWPLACEMENT
        {
            length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>(),
            showCmd = SW_HIDE,
            rcNormalPosition = normal,
        };
        return SetWindowPlacement(hwnd, ref placement);
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowPlacement(nint hWnd, ref WINDOWPLACEMENT lpwndpl);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPlacement(nint hWnd, ref WINDOWPLACEMENT lpwndpl);
}
