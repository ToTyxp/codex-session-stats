using System.Windows.Controls;
using System.Windows.Media;
using AgentSpeed.Presentation.Motion;

namespace AgentSpeed.History;

/// <summary>Eases a scroll viewer's vertical offset to a target (0.35 s ease-out); jumps when motion is reduced.</summary>
internal sealed class SmoothScroller
{
    private const double Duration = 0.35;

    private readonly ScrollViewer _viewer;

    private double _from;
    private double _to;
    private TimeSpan? _start;
    private bool _running;

    public SmoothScroller(ScrollViewer viewer)
    {
        _viewer = viewer;
        viewer.PreviewMouseWheel += (_, _) => Stop();
        viewer.PreviewMouseDown += (_, _) => Stop();
    }

    public void ScrollTo(double offset, bool reduceMotion)
    {
        offset = Math.Clamp(offset, 0, Math.Max(0, _viewer.ScrollableHeight));
        if (reduceMotion || Math.Abs(offset - _viewer.VerticalOffset) < 1)
        {
            Stop();
            _viewer.ScrollToVerticalOffset(offset);
            return;
        }

        _from = _viewer.VerticalOffset;
        _to = offset;
        _start = null;
        if (!_running)
        {
            _running = true;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    public void Stop()
    {
        if (_running)
        {
            _running = false;
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = e is RenderingEventArgs args ? args.RenderingTime : TimeSpan.FromMilliseconds(Environment.TickCount64);
        _start ??= now;
        var t = (now - _start.Value).TotalSeconds / Duration;
        _viewer.ScrollToVerticalOffset(Easing.Lerp(_from, _to, Easing.OutCubic(t)));
        if (t >= 1)
        {
            Stop();
        }
    }
}
