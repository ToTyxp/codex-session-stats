using System.Windows.Media;
using AgentSpeed.Presentation.Layout;
using AgentSpeed.Presentation.Motion;

namespace AgentSpeed.Dashboard;

/// <summary>
/// Runs <see cref="DisclosureMotion"/> once per composed frame, from <see cref="CompositionTarget.Rendering"/>, so each
/// frame's heights are applied before that frame is laid out and drawn. Frames run only while it moves; a retarget
/// mid-flight keeps the current velocities.
/// </summary>
internal sealed class HeightSpring
{
    private const double MaxStep = 1 / 30.0;

    private readonly DisclosureMotion _motion = new();
    private TimeSpan _lastFrame = TimeSpan.MinValue;

    /// <summary>After each animated frame, with the heights to apply.</summary>
    public event EventHandler<FlyoutHeights>? Stepped;

    /// <summary>The heights reached their targets, by settling or by <see cref="Finish"/>.</summary>
    public event EventHandler? Settled;

    public bool IsRunning { get; private set; }

    public FlyoutHeights Current => _motion.Current;

    public FlyoutHeights Target => _motion.Target;

    public double Cap => _motion.Cap;

    /// <summary>Starts from <paramref name="from"/> at rest, or continues the running animation toward the new target.</summary>
    public void AnimateTo(FlyoutHeights from, FlyoutHeights target, double cap)
    {
        if (IsRunning)
        {
            _motion.Retarget(target, cap);
            return;
        }

        _motion.Start(from, target, cap);
        _lastFrame = TimeSpan.MinValue;
        IsRunning = true;
        CompositionTarget.Rendering += OnRendering;
    }

    public void Retarget(FlyoutHeights target, double cap)
    {
        if (IsRunning)
        {
            _motion.Retarget(target, cap);
        }
    }

    /// <summary>Completes a running animation at once.</summary>
    public void Finish()
    {
        if (!IsRunning)
        {
            return;
        }

        Stop();
        _motion.Snap(_motion.Target);
        Stepped?.Invoke(this, _motion.Current);
        Settled?.Invoke(this, EventArgs.Empty);
    }

    private void Stop()
    {
        CompositionTarget.Rendering -= OnRendering;
        IsRunning = false;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        // Rendering can fire more than once per composed frame; step only when the frame time advances.
        var now = e is RenderingEventArgs args ? args.RenderingTime : TimeSpan.Zero;
        if (now == _lastFrame)
        {
            return;
        }

        var dt = _lastFrame == TimeSpan.MinValue ? 1 / 60.0 : Math.Clamp((now - _lastFrame).TotalSeconds, 0, MaxStep);
        _lastFrame = now;
        var heights = _motion.Step(dt);
        if (_motion.IsSettled)
        {
            Finish();
            return;
        }

        Stepped?.Invoke(this, heights);
    }
}
