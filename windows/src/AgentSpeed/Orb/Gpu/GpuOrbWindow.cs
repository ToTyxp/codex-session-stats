using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AgentSpeed.Interop;
using AgentSpeed.Orb.Render;
using AgentSpeed.Presentation.Layout;
using AgentSpeed.Presentation.Motion;
using AgentSpeed.Presentation.Orb;
using AgentSpeed.Presentation.Orb.Gpu;

namespace AgentSpeed.Orb.Gpu;

/// <summary>
/// One floating orb window drawn with Direct3D 11 through DirectComposition (DESIGN 2, 2a Techniques, 2b): a raw Win32
/// popup without a redirection bitmap, so a frame costs one present instead of WPF's composition and layered-window
/// readback. Behaviour matches the WPF orb window: never activated; outside the disc it lets clicks through
/// (WS_EX_TRANSPARENT toggled by the cursor's position); dragging asks the owner for the assisted target and follows it
/// through a critically damped spring; positions are physical pixels of the disc center.
/// </summary>
internal sealed class GpuOrbWindow : IOrbWindow, IOrbHostInput
{
    private const double DragSlop = 3;
    private const double DragSettle = 0.12;
    private const double FadeSeconds = 0.6;

    private readonly Func<double> _clock;
    private readonly OrbClock _orbClock;
    private readonly OrbHostWindow _host;
    private readonly GpuOrbSurface _surface;
    private readonly OrbAutomation _automation;
    private readonly DispatcherTimer _passThroughPoll;
    private readonly DispatcherTimer _housekeeping;
    private readonly MotionClient _mover;
    private readonly List<IOrbAttachment> _attachments = [];
    private readonly List<(nint Handle, PixelRect Rect)> _batch = [];
    private OrbWindowOptions _options;
    private bool _passThrough;
    private bool _dragging;
    private bool _dragMoved;
    private POINT _dragStartCursor;
    private PointD _dragStartCenter;
    private PointD _dragTarget;
    private PointD _center;
    private bool _hasCenter;
    private CriticalSpring _followX;
    private CriticalSpring _followY;
    private DampedSpring _moveX;
    private DampedSpring _moveY;
    private PointD _moveTarget;
    private Action? _moveDone;
    private bool _moving;
    private bool _following;
    private double _lastFrame;
    private bool _faded;
    private bool _hovering;
    private bool _discHover;
    private bool _plateHover;
    private DispatcherTimer? _hoverGrace;
    private double _idleSince = double.NaN;
    private int _pixels = 1;
    private bool _closed;

    private double _appearStart = double.NegativeInfinity;
    private bool _appearReduced;
    private double _vanishStart = double.NegativeInfinity;
    private double _vanishSeconds = 0.2;
    private double _opacityFrom = 1;
    private double _opacityTo = 1;
    private double _opacityStart = double.NegativeInfinity;
    private double _opacity = 1;

    private readonly OrbUnitToggle _unitToggle = new();
    private DispatcherTimer? _unitTimer;
    private DispatcherTimer? _hintTimer;
    private System.Windows.Controls.ToolTip? _hint;
    private string _unitHint = string.Empty;
    private bool _unitHover;

    public GpuOrbWindow(string orbId, Func<double> clock, OrbClock orbClock, uint seed, GpuDevice device)
    {
        OrbId = orbId;
        _clock = clock;
        _orbClock = orbClock;
        _host = new OrbHostWindow("Agent Speed Orb", this, topmost: true);
        _automation = new OrbAutomation(() => _host.Handle);
        _surface = new GpuOrbSurface(device, orbClock, seed, _automation) { Hwnd = _host.Handle, Motion = SampleMotion };
        _mover = new MotionClient(this);
        _passThroughPoll = new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Input, (_, _) => PollPassThrough(),
            Dispatcher.CurrentDispatcher);
        _passThroughPoll.Stop();
        _housekeeping = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => Housekeeping(), Dispatcher.CurrentDispatcher);
        _housekeeping.Stop();
    }

    public string OrbId { get; }

    public IOrbSurface Surface => _surface;

    public nint Handle => _host.Handle;

    public PointD Center => _center;

    public bool HasCenter => _hasCenter;

    public bool IsDragging => _dragging;

    public bool IsHovered => _hovering;

    public PointD DrawnCenter { get; private set; }

    public double DisplayScale { get; private set; } = 1;

    public OrbWindowOptions Options => _options;

    public bool IsVisible => !_closed && _host.IsVisible;

    public double Opacity => _opacity;

    System.Windows.Automation.Provider.IRawElementProviderSimple? IOrbHostInput.AutomationProvider => _automation;

    public event EventHandler? OpacityChanged;

    public event EventHandler? DashboardRequested;

    public event EventHandler? MenuRequested;

    public event EventHandler? Interacted;

    public event EventHandler<bool>? HoverChanged;

    public event EventHandler<PointD>? DragReleased;

    public event EventHandler? DragMoved;

    public event EventHandler? UnitToggleRequested;

    public Func<PointD, PointD>? DragAssist { get; set; }

    public void SetDimmed(bool dimmed)
    {
        if (_options.Dimmed != dimmed)
        {
            _options = _options with { Dimmed = dimmed };
            ApplyOpacity();
        }
    }

    public void Attach(IOrbAttachment attachment)
    {
        if (!_attachments.Contains(attachment))
        {
            _attachments.Add(attachment);
        }
    }

    public void Detach(IOrbAttachment attachment) => _attachments.Remove(attachment);

    public void SetPlateHover(bool hovering)
    {
        if (_plateHover != hovering)
        {
            _plateHover = hovering;
            ApplyHover();
        }

        if (hovering)
        {
            Restore();
        }
    }

    public void PlatePress(int clickCount)
    {
        Interacted?.Invoke(this, EventArgs.Empty);
        if (clickCount >= 2)
        {
            _dragging = false;
            DashboardRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        BeginDrag();
    }

    public void PlateMove()
    {
        if (_dragging)
        {
            ContinueDrag();
        }
    }

    public void PlateRelease()
    {
        if (_dragging)
        {
            EndDrag();
        }
    }

    public void PlateMenu()
    {
        Interacted?.Invoke(this, EventArgs.Empty);
        MenuRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Configure(OrbSurfaceConfig config, OrbWindowOptions options)
    {
        var sizeChanged = Math.Abs(config.Size - _surface.Diameter) > 0.01;
        _surface.Configure(config);
        if (sizeChanged && _hasCenter)
        {
            PlaceAt(_center);
        }

        _host.Topmost = options.AlwaysOnTop;
        var clickThroughChanged = options.ClickThrough != _options.ClickThrough;
        _options = options;
        if (!options.FadeWhenIdle)
        {
            _faded = false;
        }

        ApplyOpacity();
        if (clickThroughChanged || !_initialized)
        {
            _initialized = true;
            SetClickThrough(options.ClickThrough);
        }
    }

    private bool _initialized;

    public void SetActions(IReadOnlyList<(string Name, Action Invoke)> actions) => _automation.SetActions(actions);

    public void SetUnitHint(string hint) => _unitHint = hint;

    public void Announce(string text) => _automation.Announce(text);

    public void SetLanguage(XmlLanguage language)
    {
        if (GpuDevice.Shared is { } device)
        {
            try
            {
                GpuOrbSurface.SetLanguage(device, language.GetSpecificCulture());
            }
            catch (InvalidOperationException)
            {
                // A language without a specific culture keeps the current shaping.
            }
        }
    }

    public void OwnPlate(Window plate) => new WindowInteropHelper(plate).Owner = _host.Handle;

    public void MoveTo(PointD center)
    {
        StopMove();
        PlaceAt(center);
    }

    public void AnimateTo(PointD center, double response = 0.35, double damping = 0.8, Action? done = null)
    {
        if (_options.ReduceMotion || !_hasCenter || !IsVisible || (Math.Abs(center.X - _center.X) < 0.5 && Math.Abs(center.Y - _center.Y) < 0.5))
        {
            StopMove();
            PlaceAt(center);
            done?.Invoke();
            return;
        }

        _moveX = new DampedSpring(response, damping, _center.X);
        _moveY = new DampedSpring(response, damping, _center.Y);
        _moveTarget = center;
        _moveDone = done;
        _moving = true;
        StartFrames();
    }

    /// <summary>Springs in from 0.6 × scale and 0 opacity (a new orb), or fades in with Reduce Motion.</summary>
    public void Appear()
    {
        _appearStart = _clock();
        _appearReduced = _options.ReduceMotion;
        _surface.Kick();
    }

    /// <summary>Shrinks to 0.85 and fades out (split original, merge source, removal), then runs <paramref name="done"/>.</summary>
    public void Vanish(Action done, double seconds = 0.2)
    {
        _vanishStart = _clock();
        _vanishSeconds = Math.Max(0.01, seconds);
        _surface.Kick();
        var timer = new DispatcherTimer(TimeSpan.FromSeconds(_vanishSeconds), DispatcherPriority.Normal, (sender, _) =>
        {
            ((DispatcherTimer)sender!).Stop();
            done();
        }, Dispatcher.CurrentDispatcher);
        timer.Start();
    }

    public void NoteActivity(bool active)
    {
        if (active)
        {
            _idleSince = double.NaN;
            Restore();
        }
        else if (double.IsNaN(_idleSince))
        {
            _idleSince = _clock();
        }
    }

    public void BringToFront()
    {
        if (_host.Handle != 0)
        {
            User32.SetWindowPos(_host.Handle, _host.Topmost ? HWND.TOPMOST : HWND.TOP, 0, 0, 0, 0, SWP.NOMOVE | SWP.NOSIZE | SWP.NOACTIVATE);
        }
    }

    public void Show()
    {
        if (_closed)
        {
            return;
        }

        if (_hasCenter)
        {
            PlaceAt(_center);
        }

        _host.Show();
        _surface.Visible = true;
        _housekeeping.Start();
    }

    public void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _unitTimer?.Stop();
        SetUnitHover(false);
        _housekeeping.Stop();
        _passThroughPoll.Stop();
        _hoverGrace?.Stop();
        _mover.Active = false;
        _surface.Visible = false;
        _surface.Dispose();
        _host.Dispose();
    }

    public BitmapSource? Snapshot() => _surface.Snapshot();

    // ---- Input (IOrbHostInput) ----

    public bool IsOnDisc(int x, int y) => IsInsideDisc(x, y);

    public bool IsOnUnit(int x, int y) => !_options.ClickThrough && !_dragging && _surface.IsOnUnit(x / DisplayScale, y / DisplayScale);

    public void OnMouseMove(int x, int y)
    {
        if (_dragging)
        {
            ContinueDrag();
            return;
        }

        UpdateHover(x, y);
        SetUnitHover(IsOnUnit(x, y));
    }

    public void OnMouseLeave()
    {
        SetUnitHover(false);
        if (!_dragging)
        {
            SetDiscHover(false);
        }
    }

    public void OnLeftDown(int x, int y, bool doubleClick)
    {
        if (!IsInsideDisc(x, y))
        {
            return;
        }

        Interacted?.Invoke(this, EventArgs.Empty);
        _unitToggle.Press(new PointD(x / DisplayScale, y / DisplayScale), _clock(), IsOnUnit(x, y), doubleClick ? 2 : 1);
        if (doubleClick)
        {
            _unitTimer?.Stop();
            _dragging = false;
            DashboardRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (BeginDrag())
        {
            _host.CaptureMouse();
        }
    }

    public void OnLeftUp()
    {
        if (!_dragging)
        {
            return;
        }

        var moved = _dragMoved;
        EndDrag();
        _host.ReleaseMouse();
        if (User32.GetCursorPos(out var cursor) && User32.GetWindowRect(_host.Handle, out var rect)
            && _unitToggle.Release(new PointD((cursor.X - rect.Left) / DisplayScale, (cursor.Y - rect.Top) / DisplayScale), _clock(), moved,
                OrbNative.GetDoubleClickTime() / 1000.0)
            && _unitToggle.DueAt is { } due)
        {
            // The toggle fires once the double-click interval passed without a second click (DESIGN 9 Switching).
            _unitTimer ??= new DispatcherTimer(DispatcherPriority.Input, Dispatcher.CurrentDispatcher);
            _unitTimer.Stop();
            _unitTimer.Interval = TimeSpan.FromSeconds(Math.Max(0.01, due - _clock() + 0.005));
            _unitTimer.Tick -= OnUnitDue;
            _unitTimer.Tick += OnUnitDue;
            _unitTimer.Start();
        }
    }

    private void OnUnitDue(object? sender, EventArgs e)
    {
        _unitTimer?.Stop();
        if (_unitToggle.IsDue(_clock() + 0.01))
        {
            UnitToggleRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The unit's hover affordance, and its hint tooltip after 0.8 s (DESIGN 9 Switching).</summary>
    private void SetUnitHover(bool hovering)
    {
        if (_unitHover == hovering)
        {
            return;
        }

        _unitHover = hovering;
        _surface.SetUnitHover(hovering);
        if (!hovering)
        {
            _hintTimer?.Stop();
            if (_hint is not null)
            {
                _hint.IsOpen = false;
            }

            return;
        }

        if (string.IsNullOrEmpty(_unitHint))
        {
            return;
        }

        _hintTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(OrbUnitToggle.HintDelay), DispatcherPriority.Input, (_, _) =>
        {
            _hintTimer!.Stop();
            if (_unitHover && !_closed)
            {
                _hint ??= new System.Windows.Controls.ToolTip { Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse };
                _hint.Content = _unitHint;
                _hint.IsOpen = true;
            }
        }, Dispatcher.CurrentDispatcher);
        _hintTimer.Stop();
        _hintTimer.Start();
    }

    public void OnRightUp(int x, int y)
    {
        if (IsInsideDisc(x, y))
        {
            Interacted?.Invoke(this, EventArgs.Empty);
            MenuRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public void OnCaptureLost()
    {
        if (_dragging)
        {
            EndDrag();
        }
    }

    public void OnDpiChanged()
    {
        if (_hasCenter)
        {
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Loaded, () => PlaceAt(_center));
        }
    }

    // ---- Placement ----

    private void PlaceAt(PointD center)
    {
        _center = center;
        _hasCenter = true;
        if (_host.Handle == 0)
        {
            return;
        }

        var monitor = Monitors.FromPoint(new POINT((int)Math.Round(center.X), (int)Math.Round(center.Y)));
        var scale = monitor?.Scale ?? DisplayScale;
        var size = (int)Math.Round(_surface.Diameter * 1.5 * scale);
        var left = (int)Math.Round(center.X - size / 2.0);
        var top = (int)Math.Round(center.Y - size / 2.0);
        DrawnCenter = new PointD(left + size / 2.0, top + size / 2.0);
        DisplayScale = scale;
        _pixels = size;
        _batch.Clear();
        _batch.Add((_host.Handle, new PixelRect(left, top, size, size)));
        foreach (var attachment in _attachments)
        {
            if (attachment.Handle != 0 && attachment.Arrange(DrawnCenter, DisplayScale) is { } rect)
            {
                _batch.Add((attachment.Handle, rect));
            }
        }

        WindowBatch.Move(_batch);
        _surface.SetPixels(size, scale);
    }

    // ---- Drag and motion ----

    private bool BeginDrag()
    {
        if (!User32.GetCursorPos(out var cursor))
        {
            return false;
        }

        StopMove();
        _dragging = true;
        _dragMoved = false;
        _dragStartCursor = cursor;
        _dragStartCenter = _center;
        _dragTarget = _center;
        return true;
    }

    private void ContinueDrag()
    {
        if (!User32.GetCursorPos(out var cursor))
        {
            return;
        }

        var dx = cursor.X - _dragStartCursor.X;
        var dy = cursor.Y - _dragStartCursor.Y;
        if (!_dragMoved && Math.Abs(dx) < DragSlop && Math.Abs(dy) < DragSlop)
        {
            return;
        }

        if (!_dragMoved)
        {
            _dragMoved = true;
            _followX = new CriticalSpring(DragSettle, _center.X);
            _followY = new CriticalSpring(DragSettle, _center.Y);
        }

        var raw = new PointD(_dragStartCenter.X + dx, _dragStartCenter.Y + dy);
        _dragTarget = DragAssist?.Invoke(raw) ?? raw;
        if (_options.ReduceMotion)
        {
            PlaceAt(_dragTarget);
        }
        else
        {
            _following = true;
            StartFrames();
        }

        DragMoved?.Invoke(this, EventArgs.Empty);
    }

    private void EndDrag()
    {
        _dragging = false;
        _following = false;
        if (!_dragMoved)
        {
            StopFrames();
            return;
        }

        PlaceAt(_dragTarget);
        DragReleased?.Invoke(this, _dragTarget);
    }

    private void StartFrames()
    {
        _lastFrame = _clock();
        _mover.Active = true;
    }

    private void StopFrames() => _mover.Active = false;

    private void OnMotionFrame()
    {
        var now = _clock();
        var dt = Math.Clamp(now - _lastFrame, 0, 0.05);
        _lastFrame = now;
        if (_following && _dragging)
        {
            _followX.Step(_dragTarget.X, dt);
            _followY.Step(_dragTarget.Y, dt);
            if (_followX.IsSettled(_dragTarget.X, 0.3) && _followY.IsSettled(_dragTarget.Y, 0.3))
            {
                _followX.Snap(_dragTarget.X);
                _followY.Snap(_dragTarget.Y);
                PlaceAt(_dragTarget);
                StopFrames();
                return;
            }

            PlaceAt(new PointD(_followX.Value, _followY.Value));
            return;
        }

        if (_moving)
        {
            _moveX.Step(_moveTarget.X, dt);
            _moveY.Step(_moveTarget.Y, dt);
            if (_moveX.IsSettled(_moveTarget.X, 0.3) && _moveY.IsSettled(_moveTarget.Y, 0.3))
            {
                var done = _moveDone;
                StopMove();
                PlaceAt(_moveTarget);
                done?.Invoke();
                return;
            }

            PlaceAt(new PointD(_moveX.Value, _moveY.Value));
            return;
        }

        StopFrames();
    }

    private void StopMove()
    {
        if (_moving)
        {
            _moving = false;
            _moveDone = null;
        }

        if (!_dragging)
        {
            StopFrames();
        }
    }

    // ---- Hover and click-through ----

    private bool IsInsideDisc(int x, int y)
    {
        var scale = DisplayScale;
        var c = _pixels / 2.0;
        var r = (_surface.Diameter * 0.5 * (1 + 0.03 * Math.Max(0, _surface.Scene.Hover)) + 1) * scale;
        var dx = x - c;
        var dy = y - c;
        return dx * dx + dy * dy <= r * r;
    }

    private void UpdateHover(int x, int y)
    {
        var inside = IsInsideDisc(x, y);
        SetDiscHover(inside);
        if (inside)
        {
            Restore();
        }
        else if (!_options.ClickThrough)
        {
            SetPassThrough(true);
        }
    }

    private void SetDiscHover(bool hovering)
    {
        if (_discHover != hovering)
        {
            _discHover = hovering;
            ApplyHover();
        }
    }

    private void ApplyHover()
    {
        var hovering = _discHover || _plateHover;
        if (hovering || _attachments.Count == 0 || _dragging)
        {
            _hoverGrace?.Stop();
            SetHover(hovering);
            return;
        }

        if (_hovering && _hoverGrace is not { IsEnabled: true })
        {
            _hoverGrace ??= new DispatcherTimer(TimeSpan.FromSeconds(0.12), DispatcherPriority.Input, (_, _) =>
            {
                _hoverGrace!.Stop();
                SetHover(_discHover || _plateHover);
            }, Dispatcher.CurrentDispatcher);
            _hoverGrace.Start();
        }
    }

    private void SetHover(bool hovering)
    {
        if (_hovering == hovering)
        {
            return;
        }

        _hovering = hovering;
        _surface.SetHover(hovering);
        HoverChanged?.Invoke(this, hovering);
    }

    private void PollPassThrough()
    {
        if (!_passThrough || _host.Handle == 0 || !User32.GetCursorPos(out var cursor) || !User32.GetWindowRect(_host.Handle, out var rect))
        {
            _passThroughPoll.Stop();
            return;
        }

        var insideWindow = cursor.X >= rect.Left && cursor.X < rect.Right && cursor.Y >= rect.Top && cursor.Y < rect.Bottom;
        if (!insideWindow)
        {
            SetPassThrough(false);
            return;
        }

        if (IsInsideDisc(cursor.X - rect.Left, cursor.Y - rect.Top))
        {
            SetPassThrough(false);
            SetDiscHover(true);
            Restore();
        }
    }

    private void SetPassThrough(bool enabled)
    {
        if (_options.ClickThrough || _passThrough == enabled)
        {
            return;
        }

        _passThrough = enabled;
        _host.Transparent = enabled;
        if (enabled)
        {
            SetDiscHover(false);
            _passThroughPoll.Start();
        }
        else
        {
            _passThroughPoll.Stop();
        }
    }

    private void SetClickThrough(bool enabled)
    {
        _passThrough = false;
        _passThroughPoll.Stop();
        _host.Transparent = enabled;
        if (enabled)
        {
            _discHover = false;
            _plateHover = false;
            _hoverGrace?.Stop();
            SetHover(false);
        }
    }

    // ---- Opacity, fade, appear and vanish ----

    private void Housekeeping()
    {
        _surface.Paused = WindowEffects.IsCloaked(_host.Handle);
        if (_options.FadeWhenIdle && !_faded && !double.IsNaN(_idleSince) && _clock() - _idleSince >= 60 && _surface.Scene.HoverBlend < 0.01)
        {
            _faded = true;
            ApplyOpacity(animated: true);
        }
    }

    private void Restore()
    {
        if (!_faded)
        {
            return;
        }

        _faded = false;
        ApplyOpacity();
    }

    private void ApplyOpacity(bool animated = false)
    {
        var target = (_options.Dimmed ? 0.55 : 1) * (_faded ? 0.45 : 1);
        var now = _clock();
        if (animated && !_options.ReduceMotion)
        {
            _opacityFrom = CurrentOpacity(now);
            _opacityStart = now;
        }
        else
        {
            _opacityFrom = target;
            _opacityStart = double.NegativeInfinity;
        }

        _opacityTo = target;
        SetOpacity(CurrentOpacity(now));
        _surface.Kick();
    }

    private double CurrentOpacity(double now)
    {
        var t = (now - _opacityStart) / FadeSeconds;
        if (t >= 1 || t < 0)
        {
            return _opacityTo;
        }

        var eased = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
        return _opacityFrom + (_opacityTo - _opacityFrom) * eased;
    }

    private void SetOpacity(double opacity)
    {
        if (Math.Abs(opacity - _opacity) > 0.0005)
        {
            _opacity = opacity;
            OpacityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private OrbWindowMotion SampleMotion(double now)
    {
        var opacity = CurrentOpacity(now);
        SetOpacity(opacity);
        var demand = double.IsFinite(_opacityStart) && now - _opacityStart < FadeSeconds ? FrameClass.Fps30 : FrameClass.None;
        var scale = 1.0;
        var appear = now - _appearStart;
        if (double.IsFinite(_appearStart) && appear >= 0 && appear < 0.5)
        {
            demand = FrameClass.Fps60;
            if (_appearReduced)
            {
                opacity *= Math.Clamp(appear / 0.2, 0, 1);
            }
            else
            {
                opacity *= Math.Clamp(appear / 0.2, 0, 1);
                scale = 0.6 + 0.4 * OrbReadoutDraw.SpringPop(appear, response: 0.42, damping: 0.5);
            }
        }

        var vanish = now - _vanishStart;
        if (double.IsFinite(_vanishStart) && vanish >= 0)
        {
            var t = Math.Clamp(vanish / _vanishSeconds, 0, 1);
            opacity *= 1 - t;
            if (!_options.ReduceMotion)
            {
                scale *= 1 - 0.15 * t;
            }

            demand = t < 1 ? FrameClass.Fps60 : demand;
        }

        return new OrbWindowMotion(scale, opacity, demand);
    }

    /// <summary>Drives drag-follow and layout springs on the shared frame clock (no WPF render loop).</summary>
    private sealed class MotionClient(GpuOrbWindow owner) : IOrbFrameClient
    {
        private bool _active;

        public bool Active
        {
            get => _active;
            set
            {
                if (_active == value)
                {
                    return;
                }

                _active = value;
                if (value)
                {
                    owner._orbClock.Register(this);
                }
                else
                {
                    owner._orbClock.Unregister(this);
                }
            }
        }

        public bool IsActive => _active;

        public bool IsPreferred => true;

        public FrameClass Demand => _active ? FrameClass.Fps60 : FrameClass.None;

        public double? NextWake => null;

        public FrameClass Cap => FrameClass.Fps60;

        public void Render(double now) => owner.OnMotionFrame();
    }
}
