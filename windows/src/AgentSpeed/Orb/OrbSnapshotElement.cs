using System.Windows;
using System.Windows.Media.Imaging;

namespace AgentSpeed.Orb;

/// <summary>An orb in the developer capture (<c>--capture-dir</c>): the capture draws its snapshot instead of rendering a WPF visual.</summary>
internal sealed class OrbSnapshotElement(IOrbWindow window) : FrameworkElement
{
    public BitmapSource? Snapshot() => window.Snapshot();

    /// <summary>The GPU snapshot already carries the window's opacity (the dim and the idle fade).</summary>
    public bool IncludesOpacity => window is Gpu.GpuOrbWindow;
}
