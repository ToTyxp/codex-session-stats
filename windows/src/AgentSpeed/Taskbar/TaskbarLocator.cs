using AgentSpeed.Interop;
using AgentSpeed.Presentation.Layout;

namespace AgentSpeed.Taskbar;

internal readonly record struct TaskbarGeometry(nint Taskbar, nint Tray, RECT TaskbarRect, RECT TrayRect, uint Dpi, bool Vertical)
{
    public double Scale => Dpi / 96.0;
}

/// <summary>Finds the primary taskbar and its notification area. Physical pixels throughout.</summary>
internal static class TaskbarLocator
{
    public const string TaskbarClass = "Shell_TrayWnd";
    public const string TrayClass = "TrayNotifyWnd";

    public static TaskbarGeometry? Find()
    {
        var taskbar = User32.FindWindow(TaskbarClass, null);
        if (taskbar == 0 || !User32.IsWindow(taskbar) || !User32.GetWindowRect(taskbar, out var bar) || bar.IsEmpty)
        {
            return null;
        }

        var tray = default(RECT);
        var trayWindow = User32.FindWindowEx(taskbar, 0, TrayClass, null);
        if (trayWindow != 0 && User32.GetWindowRect(trayWindow, out var trayRect) && !trayRect.IsEmpty
            && trayRect.Left >= bar.Left - 1 && trayRect.Right <= bar.Right + 1)
        {
            tray = trayRect;
        }

        var dpi = User32.GetDpiForWindow(taskbar);
        if (dpi == 0)
        {
            dpi = 96;
        }

        return new TaskbarGeometry(taskbar, trayWindow, bar, tray, dpi, bar.Height > bar.Width);
    }

    /// <summary>Widget rectangle in screen coordinates.</summary>
    public static RECT? WidgetRect(TaskbarGeometry geometry, int length)
    {
        var bar = ToRect(geometry.TaskbarRect);
        var tray = geometry.TrayRect.IsEmpty ? EstimatedTray(geometry) : ToRect(geometry.TrayRect);
        var gap = Math.Round(4 * geometry.Scale);
        var rect = PlacementMath.TaskbarWidget(bar, tray, length, gap, geometry.Vertical);
        if (rect is not { } r)
        {
            return null;
        }

        return new RECT((int)r.X, (int)r.Y, (int)r.Right, (int)r.Bottom);
    }

    /// <summary>True when the taskbar is slid off-screen (auto-hide) on its monitor.</summary>
    public static bool IsAutoHidden(TaskbarGeometry geometry)
    {
        var monitor = Monitors.FromRect(geometry.TaskbarRect);
        if (monitor is null)
        {
            return false;
        }

        var m = monitor.Bounds;
        var b = geometry.TaskbarRect;
        var visibleWidth = Math.Min(b.Right, m.Right) - Math.Max(b.Left, m.Left);
        var visibleHeight = Math.Min(b.Bottom, m.Bottom) - Math.Max(b.Top, m.Top);
        var thickness = geometry.Vertical ? visibleWidth : visibleHeight;
        return thickness < 8 * geometry.Scale;
    }

    /// <summary>A full-screen app (game, video, presentation) covers the taskbar's monitor.</summary>
    public static bool IsFullscreenActive(TaskbarGeometry geometry)
    {
        if (Shell32.SHQueryUserNotificationState(out var state) == 0
            && state is QUNS.RUNNING_D3D_FULL_SCREEN or QUNS.PRESENTATION_MODE)
        {
            return true;
        }

        var foreground = User32.GetForegroundWindow();
        if (foreground == 0 || foreground == geometry.Taskbar || foreground == User32.GetShellWindow()
            || foreground == User32.GetDesktopWindow())
        {
            return false;
        }

        var className = User32.GetClassName(foreground);
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
        {
            return false;
        }

        if (!User32.GetWindowRect(foreground, out var window))
        {
            return false;
        }

        var monitor = Monitors.FromRect(geometry.TaskbarRect);
        if (monitor is null)
        {
            return false;
        }

        var m = monitor.Bounds;
        return window.Left <= m.Left && window.Top <= m.Top && window.Right >= m.Right && window.Bottom >= m.Bottom;
    }

    private static RectD ToRect(RECT r) => new(r.Left, r.Top, r.Width, r.Height);

    /// <summary>Without a notification area window, leave room for a typical clock and tray icons.</summary>
    private static RectD EstimatedTray(TaskbarGeometry geometry)
    {
        var b = geometry.TaskbarRect;
        if (geometry.Vertical)
        {
            var h = Math.Round(180 * geometry.Scale);
            return new RectD(b.Left, b.Bottom - h, b.Width, h);
        }

        var w = Math.Round(220 * geometry.Scale);
        return new RectD(b.Right - w, b.Top, w, b.Height);
    }
}
