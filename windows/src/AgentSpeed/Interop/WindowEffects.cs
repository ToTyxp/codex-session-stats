using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace AgentSpeed.Interop;

/// <summary>DWM attributes and extended styles for WPF windows.</summary>
internal static unsafe class WindowEffects
{
    public static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

    public static bool SupportsSystemBackdrop => Environment.OSVersion.Version.Build >= 22621;

    public static void SetDarkMode(nint hwnd, bool dark)
    {
        if (hwnd == 0)
        {
            return;
        }

        var value = dark ? 1 : 0;
        _ = Dwmapi.DwmSetWindowAttribute(hwnd, DWMWA.USE_IMMERSIVE_DARK_MODE, &value, sizeof(int));
    }

    public static void SetCornerPreference(nint hwnd, int preference)
    {
        if (hwnd == 0 || !IsWindows11)
        {
            return;
        }

        _ = Dwmapi.DwmSetWindowAttribute(hwnd, DWMWA.WINDOW_CORNER_PREFERENCE, &preference, sizeof(int));
    }

    /// <summary>
    /// Applies a system backdrop (Mica or Acrylic) when the OS supports it and transparency effects are on.
    /// Returns false when the caller must paint a solid background instead.
    /// </summary>
    public static bool TrySetBackdrop(Window window, int backdropType, bool allowed)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0 || window.AllowsTransparency)
        {
            return false;
        }

        var source = HwndSource.FromHwnd(hwnd);
        if (!allowed || !SupportsSystemBackdrop || source?.CompositionTarget is null)
        {
            if (SupportsSystemBackdrop && hwnd != 0)
            {
                var none = DWMSBT.NONE;
                _ = Dwmapi.DwmSetWindowAttribute(hwnd, DWMWA.SYSTEMBACKDROP_TYPE, &none, sizeof(int));
            }

            return false;
        }

        source.CompositionTarget.BackgroundColor = Colors.Transparent;
        var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
        if (Dwmapi.DwmExtendFrameIntoClientArea(hwnd, margins) != 0)
        {
            return false;
        }

        return Dwmapi.DwmSetWindowAttribute(hwnd, DWMWA.SYSTEMBACKDROP_TYPE, &backdropType, sizeof(int)) == 0;
    }

    /// <summary>
    /// Keeps a window free of caption buttons. With the frame extended over the whole client area (Mica), DWM draws the
    /// buttons of the system menu into the content; a flyout has none, so the system menu bits are removed now and
    /// whenever anything (WPF included) sets them again.
    /// </summary>
    public static void RemoveCaptionButtons(nint hwnd)
    {
        if (hwnd == 0)
        {
            return;
        }

        const int buttons = WS.SYSMENU | WS.MINIMIZEBOX | WS.MAXIMIZEBOX;
        var current = (int)User32.GetWindowLongPtr(hwnd, GWL.STYLE);
        if ((current & buttons) != 0)
        {
            User32.TrySetWindowLongPtr(hwnd, GWL.STYLE, current & ~buttons);
            User32.SetWindowPos(hwnd, 0, 0, 0, 0, 0,
                SWP.NOMOVE | SWP.NOSIZE | SWP.NOZORDER | SWP.NOACTIVATE | SWP.FRAMECHANGED | SWP.NOOWNERZORDER);
        }

        HwndSource.FromHwnd(hwnd)?.AddHook(KeepCaptionButtonsOff);
    }

    private static nint KeepCaptionButtonsOff(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == (int)WM.STYLECHANGING && wParam == GWL.STYLE && lParam != 0)
        {
            var change = (STYLESTRUCT*)lParam;
            change->styleNew &= ~(uint)(WS.SYSMENU | WS.MINIMIZEBOX | WS.MAXIMIZEBOX);
        }

        return 0;
    }

    public static bool IsCloaked(nint hwnd)
    {
        if (hwnd == 0)
        {
            return false;
        }

        var cloaked = 0;
        return Dwmapi.DwmGetWindowAttribute(hwnd, DWMWA.CLOAKED, &cloaked, sizeof(int)) == 0 && cloaked != 0;
    }

    public static void AddExtendedStyle(nint hwnd, int add, int remove = 0, bool frameChanged = true)
    {
        if (hwnd == 0)
        {
            return;
        }

        var current = (int)User32.GetWindowLongPtr(hwnd, GWL.EXSTYLE);
        var updated = (current | add) & ~remove;
        if (updated != current)
        {
            User32.TrySetWindowLongPtr(hwnd, GWL.EXSTYLE, updated);
            if (frameChanged)
            {
                User32.SetWindowPos(hwnd, 0, 0, 0, 0, 0,
                    SWP.NOMOVE | SWP.NOSIZE | SWP.NOZORDER | SWP.NOACTIVATE | SWP.FRAMECHANGED | SWP.NOOWNERZORDER);
            }
        }
    }
}
