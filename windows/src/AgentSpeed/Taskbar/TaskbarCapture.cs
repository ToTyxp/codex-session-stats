using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AgentSpeed.Interop;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Orb;

namespace AgentSpeed.Taskbar;

/// <summary>Developer capture (<c>--capture-dir</c>, request <c>taskbar</c>): the widget for a state, rendered again in-process.</summary>
internal static class TaskbarCapture
{
    private const double BarHeight = 48;

    public static BitmapSource Render(TaskbarState state, double value, SpeedTier tier, double redline)
    {
        var scale = Monitors.All().FirstOrDefault(m => m.IsPrimary)?.Scale ?? 1;
        var renderer = new TaskbarRenderer();
        var scene = new BarScene { Redline = redline };
        scene.SetTarget(value, tier);
        scene.Snap();
        var width = Math.Max(1, renderer.MeasureLength(state, scale, vertical: false));
        var height = (int)Math.Ceiling(BarHeight * scale);
        var buffer = new byte[width * height * 4];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            renderer.Render(state, scene, width, height, scale, vertical: false, hovered: false, pressed: false, handle.AddrOfPinnedObject(), buffer.Length);
        }
        finally
        {
            handle.Free();
        }

        var bitmap = BitmapSource.Create(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32, null, buffer, width * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
