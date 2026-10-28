using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using AgentSpeed.Core;
using AgentSpeed.Presentation.Chart;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.History;
using AgentSpeed.Presentation.Motion;
using AgentSpeed.Theme;

namespace AgentSpeed.History;

/// <summary>Texts and formatters the chart draws in the selected language.</summary>
/// <param name="Unit">The speed axis' unit (drawn only with the speed line).</param>
/// <param name="Axis">The left axis label of a value at a tick step: tokens, or money for the cost chart (DESIGN 9).</param>
internal sealed record ChartTexts(
    CultureInfo Culture,
    string Unit,
    string InProgress,
    Func<DateOnly, bool, string> Month,
    Func<DateOnly, string> MonthDay,
    Func<DaySummary, string> Accessible,
    Func<double, double, string> Axis)
{
    public static ChartTexts Empty { get; } = new(CultureInfo.InvariantCulture, "tok/s", string.Empty,
        (d, _) => d.ToString("MMM", CultureInfo.InvariantCulture), d => d.ToString("M/d", CultureInfo.InvariantCulture), _ => string.Empty,
        TokenAxis);

    /// <summary>Output tokens in the compact form.</summary>
    public static string TokenAxis(double value, double step) => SpeedFormat.Tokens((long)Math.Round(value));

    /// <summary>The label of the traffic chart's band of days before measuring began (<c>history.traffic.notRecorded</c>).</summary>
    public string NotRecorded { get; init; } = string.Empty;

    /// <summary>A traffic day (by index) for screen readers; the traffic chart's days need no usage.</summary>
    public Func<int, string>? TrafficAccessible { get; init; }
}

internal enum ChartTransition
{
    /// <summary>Jump to the new data.</summary>
    Snap,

    /// <summary>A new range or filter: bars spring, the speed line fades in.</summary>
    Morph,

    /// <summary>New data for the same view: bars spring, the line stays.</summary>
    Live,
}

/// <param name="Index">Day under the pointer or the keyboard selection; null when none.</param>
/// <param name="Pointer">The pointer (not the keyboard) chose it.</param>
internal readonly record struct ChartHover(int? Index, bool Pointer);

/// <summary>
/// Daily output bars (stacked by source) with the average-speed line on a second axis (DESIGN 4b, item 3), daily cost
/// bars on a money axis without the speed line (DESIGN 9), or mirrored traffic bars, download above the axis and upload
/// below it (DESIGN 10). Drawn into three layers so hovering never redraws the bars: the slot highlight behind, the
/// chart, and the crosshair in front.
/// </summary>
internal sealed class HistoryChart : FrameworkElement
{
    public const double TopBand = 20;
    public const double BottomBand = 22;

    private const double LabelSize = 11;
    private const double MaxDuration = 0.45;
    private const double FadeDuration = 0.2;
    private const double UploadOpacity = 0.55;
    private const double NotRecordedLabelWidth = 120;

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(typeof(HistoryChart),
        new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.Inherits, OnVisualsChanged));

    public static readonly DependencyProperty LabelBrushProperty = Register(nameof(LabelBrush), Brushes.Gray);
    public static readonly DependencyProperty MutedBrushProperty = Register(nameof(MutedBrush), Brushes.Gray);
    public static readonly DependencyProperty GridBrushProperty = Register(nameof(GridBrush), Brushes.LightGray);
    public static readonly DependencyProperty LineBrushProperty = Register(nameof(LineBrush), Brushes.Black);
    public static readonly DependencyProperty HighlightBrushProperty = Register(nameof(HighlightBrush), Brushes.WhiteSmoke);
    public static readonly DependencyProperty FocusBrushProperty = Register(nameof(FocusBrush), Brushes.Black);

    private readonly VisualCollection _layers;
    private readonly DrawingVisual _back = new();
    private readonly DrawingVisual _main = new();
    private readonly DrawingVisual _front = new();

    private HistoryBars _target = HistoryBars.Empty(DayRange.Single(DateOnly.MinValue));
    private HistoryBars _shown;
    private HistoryBars _from;
    private HistoryBars? _fadeFrom;
    private double _fadeFromMax;
    private double _fromMax;
    private double _shownMax;
    private double _targetMax = 1;
    private double _fadeFromBelow;
    private double _fromBelow;
    private double _shownBelow;
    private double _targetBelow;
    private double _lineAlpha = 1;
    private double _fade = 1;
    private double _fromLeft = double.NaN;
    private double _fromRight = double.NaN;
    private double _marginT = 1;
    private DampedSpring _spring;
    private bool _crossfading;
    private bool _animating;
    private TimeSpan? _lastFrame;
    private double _elapsed;
    private Func<int, DaySummary?> _dayAt = _ => null;
    private SourceKind? _source;
    private DateOnly _today;
    private ChartTexts _texts = ChartTexts.Empty;
    private ChartFrame _frame = new(Rect.Empty, new BarSlots(0, 0, 0), new AxisTicks(1, 4), new AxisTicks(1, 4), 1, null, 0);
    private int? _pointerIndex;
    private int? _keyIndex;
    private int? _linkedIndex;
    private bool _showFocus;

    public HistoryChart()
    {
        _shown = _target;
        _from = _target;
        _layers = new VisualCollection(this) { _back, _main, _front };
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = false;
        IsKeyboardFocusedChanged += (_, _) =>
        {
            if (!IsKeyboardFocused)
            {
                _showFocus = false;
                if (_keyIndex is not null)
                {
                    _keyIndex = null;
                    HoverChanged?.Invoke(this, new ChartHover(ActiveIndex, _pointerIndex is not null));
                }
            }
            else if (InputManager.Current.MostRecentInputDevice is KeyboardDevice)
            {
                _showFocus = true;
                if (_keyIndex is null && LastIndexWithData() is { } last)
                {
                    SetKeyIndex(last);
                }
            }

            RenderOverlay();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                FinishAnimation();
            }
        };
    }

    public event EventHandler<ChartHover>? HoverChanged;

    public event EventHandler<DateOnly>? DayActivated;

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public Brush LabelBrush { get => (Brush)GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }

    public Brush MutedBrush { get => (Brush)GetValue(MutedBrushProperty); set => SetValue(MutedBrushProperty, value); }

    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }

    public Brush LineBrush { get => (Brush)GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }

    public Brush HighlightBrush { get => (Brush)GetValue(HighlightBrushProperty); set => SetValue(HighlightBrushProperty, value); }

    public Brush FocusBrush { get => (Brush)GetValue(FocusBrushProperty); set => SetValue(FocusBrushProperty, value); }

    public bool ReduceMotion { get; set; }

    public bool HighContrast
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                RenderAll();
            }
        }
    }

    public HistoryBars Bars => _target;

    public DayRange Range => _target.Range;

    /// <summary>The plot area in element coordinates.</summary>
    public Rect Plot => _frame.Plot;

    /// <summary>Day chosen with the keyboard, if any.</summary>
    public int? KeyIndex => _keyIndex;

    /// <summary>The day highlighted from outside (the table row under the pointer).</summary>
    public DateOnly? LinkedDay
    {
        set
        {
            int? index = value is { } day && _target.Range.Contains(day) ? _target.Range.IndexOf(day) : null;
            if (index != _linkedIndex)
            {
                _linkedIndex = index;
                RenderOverlay();
            }
        }
    }

    public void SetData(HistoryBars bars, Func<int, DaySummary?> dayAt, SourceKind? source, DateOnly today, ChartTexts texts,
        ChartTransition transition)
    {
        ArgumentNullException.ThrowIfNull(bars);
        var previousRange = _target.Range;
        var rangeChanged = bars.Range != previousRange;
        var metricChanged = bars.Metric != _target.Metric;
        _dayAt = dayAt;
        _source = source;
        _today = today;
        _texts = texts;
        var previousShown = _shown;
        var previousMax = _shownMax;
        var previousBelow = _shownBelow;
        _target = bars;
        var (above, below) = AxesOf(bars);
        _targetMax = above.Max;
        _targetBelow = below?.Max ?? 0;
        if (rangeChanged)
        {
            _pointerIndex = null;
            _keyIndex = _keyIndex is { } k && bars.Range.Contains(previousRange.DayAt(k)) ? bars.Range.IndexOf(previousRange.DayAt(k)) : null;
            _linkedIndex = null;
        }

        _marginT = 1;
        if (transition == ChartTransition.Snap || !IsVisible || ActualWidth <= 0)
        {
            StopLoop();
            _shown = bars;
            _shownMax = _targetMax;
            _shownBelow = _targetBelow;
            _lineAlpha = 1;
            _fade = 1;
            _fadeFrom = null;
            _crossfading = false;
        }
        else if (ReduceMotion)
        {
            _crossfading = true;
            _fadeFrom = previousShown;
            _fadeFromMax = previousMax > 0 ? previousMax : _targetMax;
            _fadeFromBelow = previousMax > 0 ? previousBelow : _targetBelow;
            _shown = bars;
            _shownMax = _targetMax;
            _shownBelow = _targetBelow;
            _fade = 0;
            _lineAlpha = 1;
            StartLoop();
        }
        else
        {
            _crossfading = false;
            _fadeFrom = null;
            _fade = 1;
            _from = previousShown;
            _fromMax = previousMax > 0 ? previousMax : _targetMax;
            _fromBelow = previousMax > 0 ? previousBelow : _targetBelow;
            if (metricChanged && previousMax > 0)
            {
                // Output, cost and traffic share no unit: the old bars start from their heights on the new scale.
                var factor = _targetMax / previousMax;
                _from = previousShown.Scaled(factor);
                _fromMax = _targetMax;
                _fromBelow = previousBelow * factor;
            }

            // The axes' room (labels of another width, the speed axis coming or going) eases along with the bars.
            if (!_frame.Plot.IsEmpty)
            {
                _fromLeft = _frame.Plot.Left;
                _fromRight = ActualWidth - _frame.Plot.Right;
                _marginT = 0;
            }

            _spring = new DampedSpring(0.3, 0.86);
            if (transition == ChartTransition.Morph)
            {
                _lineAlpha = 0;
            }

            StartLoop();
        }

        Peer()?.ResetChildrenCache();
        RenderAll();
        if (rangeChanged)
        {
            HoverChanged?.Invoke(this, new ChartHover(ActiveIndex, _pointerIndex is not null));
        }
    }

    /// <summary>Grows the bars from the baseline (or fades them in with Reduce Motion), for the first show.</summary>
    public void PlayEntrance()
    {
        if (!IsVisible || _target.Max <= 0)
        {
            return;
        }

        var empty = HistoryBars.Empty(_target.Range);
        if (ReduceMotion)
        {
            _crossfading = true;
            _fadeFrom = empty;
            _fadeFromMax = _targetMax;
            _fadeFromBelow = _targetBelow;
            _fade = 0;
        }
        else
        {
            _crossfading = false;
            _from = empty;
            _fromMax = _targetMax;
            _fromBelow = _targetBelow;
            _shown = empty;
            _spring = new DampedSpring(0.32, 0.86);
            _lineAlpha = 0;
        }

        StartLoop();
        RenderMain();
    }

    /// <summary>Pointer index, else keyboard index.</summary>
    public int? ActiveIndex => _pointerIndex ?? _keyIndex;

    /// <summary>The slot of day <paramref name="index"/> (full plot height), in element coordinates.</summary>
    public Rect SlotBounds(int index)
    {
        var f = _frame;
        if (f.Plot.IsEmpty || f.Slots.Slot <= 0)
        {
            return Rect.Empty;
        }

        return new Rect(f.Plot.Left + index * f.Slots.Slot, f.Plot.Top, f.Slots.Slot, f.Plot.Height);
    }

    /// <summary>Top of day <paramref name="index"/>'s finished bar, in element coordinates.</summary>
    public double BarTop(int index)
    {
        var f = _frame with { ScaleMax = Math.Max(_targetMax, 1e-9), ScaleBelow = _targetBelow };
        if (f.Plot.IsEmpty || index < 0 || index >= _target.Count || _targetMax <= 0)
        {
            return f.Plot.Bottom;
        }

        return Math.Max(f.Plot.Top, f.Y(_target.Total(index)));
    }

    public DaySummary? DayAt(int index) => _dayAt(index);

    public string AccessibleName(int index) =>
        _target.IsMirrored && _texts.TrafficAccessible is { } traffic ? traffic(index)
        : _dayAt(index) is { } day ? _texts.Accessible(day) : string.Empty;

    /// <summary>Indices of days with data, oldest first (what the automation tree lists).</summary>
    public IEnumerable<int> IndicesWithData()
    {
        for (var i = 0; i < _target.Count; i++)
        {
            if (_target.HasData(i))
            {
                yield return i;
            }
        }
    }

    public void ClearKeyboardSelection()
    {
        if (_keyIndex is not null)
        {
            _keyIndex = null;
            RenderOverlay();
            HoverChanged?.Invoke(this, new ChartHover(ActiveIndex, _pointerIndex is not null));
        }
    }

    protected override int VisualChildrenCount => _layers.Count;

    protected override Visual GetVisualChild(int index) => _layers[index];

    protected override AutomationPeer OnCreateAutomationPeer() => new HistoryChartPeer(this);

    protected override void OnRender(DrawingContext drawingContext) =>
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        RenderAll();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        RenderAll();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        var f = _frame;
        int? index = null;
        if (!f.Plot.IsEmpty && f.Slots.Count > 0 && p.X >= f.Plot.Left - 2 && p.X <= f.Plot.Right + 2)
        {
            index = f.Slots.IndexAt(p.X - f.Plot.Left);
        }

        Cursor = index is { } i && _target.HasData(i) ? Cursors.Hand : null;
        if (index != _pointerIndex)
        {
            _pointerIndex = index;
            RenderOverlay();
            HoverChanged?.Invoke(this, new ChartHover(ActiveIndex, _pointerIndex is not null));
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_pointerIndex is not null)
        {
            _pointerIndex = null;
            RenderOverlay();
            HoverChanged?.Invoke(this, new ChartHover(ActiveIndex, false));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _showFocus = false;
        Focus();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_pointerIndex is { } index && _target.HasData(index))
        {
            DayActivated?.Invoke(this, _target.Range.DayAt(index));
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int? next = e.Key switch
        {
            Key.Left => Step(-1),
            Key.Right => Step(1),
            Key.Home => FirstIndexWithData(),
            Key.End => LastIndexWithData(),
            Key.PageUp => Jump(-7),
            Key.PageDown => Jump(7),
            _ => null,
        };

        if (next is { } index)
        {
            _showFocus = true;
            SetKeyIndex(index);
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Enter or Key.Space when _keyIndex is { } current && _target.HasData(current):
                DayActivated?.Invoke(this, _target.Range.DayAt(current));
                e.Handled = true;
                break;
            case Key.Escape when _keyIndex is not null:
                ClearKeyboardSelection();
                e.Handled = true;
                break;
        }
    }

    private static DependencyProperty Register(string name, Brush fallback) => DependencyProperty.Register(
        name, typeof(Brush), typeof(HistoryChart), new FrameworkPropertyMetadata(fallback, OnVisualsChanged));

    private static void OnVisualsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((HistoryChart)d).RenderAll();

    private HistoryChartPeer? Peer() => UIElementAutomationPeer.FromElement(this) as HistoryChartPeer;

    private void SetKeyIndex(int index)
    {
        index = Math.Clamp(index, 0, Math.Max(0, _target.Count - 1));
        if (_keyIndex == index && _pointerIndex is null)
        {
            RenderOverlay();
            return;
        }

        _keyIndex = index;
        RenderOverlay();
        HoverChanged?.Invoke(this, new ChartHover(ActiveIndex, _pointerIndex is not null));
        Peer()?.RaiseFocusChanged(index);
    }

    private int? Step(int direction)
    {
        var start = _keyIndex ?? (direction > 0 ? -1 : _target.Count);
        for (var i = start + direction; i >= 0 && i < _target.Count; i += direction)
        {
            if (_target.HasData(i))
            {
                return i;
            }
        }

        return _keyIndex ?? (direction > 0 ? FirstIndexWithData() : LastIndexWithData());
    }

    private int? Jump(int days)
    {
        if (_target.Count == 0)
        {
            return null;
        }

        var target = Math.Clamp((_keyIndex ?? _target.Count - 1) + days, 0, _target.Count - 1);
        return target;
    }

    private int? FirstIndexWithData()
    {
        for (var i = 0; i < _target.Count; i++)
        {
            if (_target.HasData(i))
            {
                return i;
            }
        }

        return null;
    }

    private int? LastIndexWithData()
    {
        for (var i = _target.Count - 1; i >= 0; i--)
        {
            if (_target.HasData(i))
            {
                return i;
            }
        }

        return null;
    }

    // ---- Animation ----

    private void StartLoop()
    {
        _elapsed = 0;
        _lastFrame = null;
        if (!_animating)
        {
            _animating = true;
            CompositionTarget.Rendering += OnFrame;
        }
    }

    private void StopLoop()
    {
        if (_animating)
        {
            _animating = false;
            CompositionTarget.Rendering -= OnFrame;
        }
    }

    private void FinishAnimation()
    {
        if (!_animating)
        {
            return;
        }

        StopLoop();
        _shown = _target;
        _shownMax = _targetMax;
        _shownBelow = _targetBelow;
        _marginT = 1;
        _lineAlpha = 1;
        _fade = 1;
        _fadeFrom = null;
        _crossfading = false;
        RenderMain();
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = e is RenderingEventArgs args ? args.RenderingTime : TimeSpan.FromMilliseconds(Environment.TickCount64);
        var dt = _lastFrame is { } last ? Math.Clamp((now - last).TotalSeconds, 0, 1 / 20.0) : 1 / 60.0;
        if (_lastFrame == now)
        {
            return;
        }

        _lastFrame = now;
        _elapsed += dt;

        if (_crossfading)
        {
            _fade = Easing.OutQuad(_elapsed / FadeDuration);
            if (_elapsed >= FadeDuration)
            {
                FinishAnimation();
                return;
            }

            RenderMain();
            return;
        }

        _spring.Step(1, dt);
        var t = _spring.Value;
        _lineAlpha = Math.Max(_lineAlpha, Math.Clamp(_elapsed / 0.3, 0, 1));
        if (_elapsed >= MaxDuration || _spring.IsSettled(1, 0.002))
        {
            FinishAnimation();
            return;
        }

        _shown = HistoryBars.Blend(_from, _target, t);
        _shownMax = Easing.Lerp(_fromMax, _targetMax, Math.Clamp(t, 0, 1.1));
        _shownBelow = Math.Max(0, Easing.Lerp(_fromBelow, _targetBelow, Math.Clamp(t, 0, 1.1)));
        _marginT = Math.Clamp(t, 0, 1);
        RenderMain();
    }

    // ---- Rendering ----

    private void RenderAll()
    {
        RenderMain();
        RenderOverlay();
    }

    private double PixelsPerDip => VisualTreeHelper.GetDpi(this).PixelsPerDip;

    private FormattedText Text(string text, Brush brush, double size = LabelSize, FontWeight? weight = null) =>
        new(text, _texts.Culture, FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal), size, brush, PixelsPerDip);

    /// <summary>The value axis above zero, and below it for traffic (null without upload).</summary>
    private static (AxisTicks Above, AxisTicks? Below) AxesOf(HistoryBars bars) =>
        bars.IsMirrored ? HistoryChartMath.MirroredTicks(bars.Max, bars.MaxBelow) : (HistoryChartMath.Ticks(bars.Max), null);

    /// <summary>
    /// The value labels of the left axis, at their signed values. On the traffic chart the outermost labels name their
    /// side: <c>↓</c> (download) above the axis, <c>↑</c> (upload) below.
    /// </summary>
    private IEnumerable<(double Value, string Text)> AxisLabels(AxisTicks above, AxisTicks? below)
    {
        var mirrored = _target.IsMirrored;
        for (var i = 0; i <= above.Intervals; i++)
        {
            var value = above.Value(i);
            var text = _texts.Axis(value, above.Step);
            yield return (value, !mirrored ? text : i == 0 ? "0" : i == above.Intervals ? "↓ " + text : text);
        }

        if (below is not { } lower)
        {
            yield break;
        }

        for (var i = 1; i <= lower.Intervals; i++)
        {
            var text = _texts.Axis(lower.Value(i), lower.Step);
            yield return (-lower.Value(i), i == lower.Intervals ? "↑ " + text : text);
        }
    }

    private ChartFrame Layout()
    {
        var width = ActualWidth;
        var height = ActualHeight;
        var (ticks, lower) = AxesOf(_target);
        var speedTicks = HistoryChartMath.AlignedTicks(_target.MaxSpeed, ticks.Intervals);
        var left = 0.0;
        foreach (var (_, text) in AxisLabels(ticks, lower))
        {
            left = Math.Max(left, Text(text, LabelBrush).WidthIncludingTrailingWhitespace);
        }

        var right = 12.0;
        if (_target.HasSpeed)
        {
            for (var i = 0; i <= speedTicks.Intervals; i++)
            {
                right = Math.Max(right, Text(SpeedFormat.Speed(speedTicks.Value(i)), LabelBrush).WidthIncludingTrailingWhitespace + 10);
            }

            right = Math.Max(right, Text(_texts.Unit, MutedBrush, 10).WidthIncludingTrailingWhitespace + 4);
        }

        left = Math.Ceiling(Math.Max(24, left + 10));
        right = Math.Ceiling(right);
        if (_marginT < 1 && double.IsFinite(_fromLeft) && double.IsFinite(_fromRight))
        {
            left = Easing.Lerp(_fromLeft, left, _marginT);
            right = Easing.Lerp(_fromRight, right, _marginT);
        }
        var plot = new Rect(left, TopBand, Math.Max(0, width - left - right), Math.Max(0, height - TopBand - BottomBand));
        var slots = HistoryChartMath.Slots(_target.Count, plot.Width);
        return new ChartFrame(plot, slots, ticks, speedTicks, Math.Max(_shownMax, 1e-9), lower, _shownBelow);
    }

    private void RenderMain()
    {
        using var dc = _main.RenderOpen();
        if (ActualWidth <= 8 || ActualHeight <= TopBand + BottomBand + 8)
        {
            return;
        }

        _frame = Layout();
        var f = _frame;
        var ppd = PixelsPerDip;
        DrawNotRecorded(dc, f, ppd);
        DrawGrid(dc, f, ppd);
        DrawMonths(dc, f, ppd);

        if (_crossfading && _fadeFrom is { } old && old.Count == _target.Count)
        {
            dc.PushOpacity(1 - _fade);
            DrawBars(dc, f with { ScaleMax = Math.Max(_fadeFromMax, 1e-9), ScaleBelow = _fadeFromBelow }, old, ppd, caption: false);
            dc.Pop();
            dc.PushOpacity(_fade);
            DrawBars(dc, f, _target, ppd, caption: true);
            dc.Pop();
        }
        else if (_crossfading)
        {
            dc.PushOpacity(_fade);
            DrawBars(dc, f, _target, ppd, caption: true);
            dc.Pop();
        }
        else
        {
            DrawBars(dc, f, _shown, ppd, caption: !_animating);
        }

        DrawSpeedLine(dc, f, _crossfading ? _fade : _lineAlpha);
    }

    private void DrawGrid(DrawingContext dc, ChartFrame f, double ppd)
    {
        var gridPen = MediaColors.FrozenPen(GridBrush, 1);
        var index = 0;
        foreach (var (value, text) in AxisLabels(f.Ticks, f.Lower))
        {
            var i = index++;
            var y = f.Y(value);
            if (y < f.Plot.Top - 0.5 || y > f.Plot.Bottom + 0.5)
            {
                continue;
            }

            var snapped = Snap(y, ppd);
            dc.DrawLine(gridPen, new Point(f.Plot.Left, snapped), new Point(f.Plot.Right, snapped));
            var label = Text(text, LabelBrush);
            var labelY = Math.Max(0, y - label.Height / 2);
            dc.DrawText(label, new Point(f.Plot.Left - 8 - label.WidthIncludingTrailingWhitespace, labelY));
            if (_target.HasSpeed && i <= f.SpeedTicks.Intervals)
            {
                var speed = Text(SpeedFormat.Speed(f.SpeedTicks.Value(i)), LabelBrush);
                dc.DrawText(speed, new Point(f.Plot.Right + 8, labelY));
            }
        }

        if (_target.HasSpeed)
        {
            var unit = Text(_texts.Unit, MutedBrush, 10);
            dc.DrawText(unit, new Point(ActualWidth - unit.WidthIncludingTrailingWhitespace, 0));
        }
    }

    private void DrawMonths(DrawingContext dc, ChartFrame f, double ppd)
    {
        if (f.Slots.Slot <= 0)
        {
            return;
        }

        var marks = HistoryChartMath.Marks(_target.Range, f.Slots.Slot);
        var rulePen = MediaColors.FrozenPen(GridBrush, 1);
        foreach (var index in marks.Rules)
        {
            var x = Snap(f.Plot.Left + index * f.Slots.Slot, ppd);
            dc.PushOpacity(0.75);
            dc.DrawLine(rulePen, new Point(x, f.Plot.Top - 6), new Point(x, f.Plot.Bottom));
            dc.Pop();
        }

        var unitLeft = _target.HasSpeed ? ActualWidth - Text(_texts.Unit, MutedBrush, 10).WidthIncludingTrailingWhitespace - 8 : ActualWidth;
        foreach (var mark in marks.Months)
        {
            var withYear = mark.Kind == AxisLabelKind.Year || (mark.Index == 0 && mark.Day.Year != _today.Year);
            var label = Text(_texts.Month(mark.Day, withYear), LabelBrush, LabelSize, FontWeights.SemiBold);
            var x = f.Plot.Left + mark.Index * f.Slots.Slot + (mark.Index == 0 ? 0 : 4);
            if (x + label.WidthIncludingTrailingWhitespace <= unitLeft)
            {
                dc.DrawText(label, new Point(x, 2));
            }
        }

        var lastRight = double.NegativeInfinity;
        foreach (var mark in marks.Days)
        {
            var wide = f.Slots.Slot >= 44;
            var isToday = mark.Day == _today;
            var label = Text(wide ? _texts.MonthDay(mark.Day) : mark.Day.Day.ToString(_texts.Culture), isToday ? LabelBrush : MutedBrush,
                10.5, isToday ? FontWeights.SemiBold : FontWeights.Normal);
            var width = label.WidthIncludingTrailingWhitespace;
            var x = Math.Clamp(f.Plot.Left + f.Slots.Center(mark.Index) - width / 2, 0, Math.Max(0, ActualWidth - width));
            if (x < lastRight + 4)
            {
                continue;
            }

            dc.DrawText(label, new Point(x, f.Plot.Bottom + 5));
            lastRight = x + width;
        }
    }

    private void DrawBars(DrawingContext dc, ChartFrame f, HistoryBars bars, double ppd, bool caption)
    {
        if (f.Slots.Slot <= 0 || bars.Count != f.Slots.Count)
        {
            return;
        }

        var single = _source is not null;
        var todayIndex = bars.Range.Contains(_today) ? bars.Range.IndexOf(_today) : -1;
        var zero = f.ZeroY;
        var aboveHeight = zero - f.Plot.Top;
        var belowHeight = f.Plot.Bottom - zero;
        if (bars.HasOutline && !single)
        {
            DrawOutlines(dc, f, bars, ppd);
        }

        for (var i = 0; i < bars.Count; i++)
        {
            var values = bars.Values(i);
            var under = bars.Below(i);
            if (values.Count == 0 && under.Count == 0)
            {
                continue;
            }

            var tops = HistoryChartMath.Stack(values, f.ScaleMax, aboveHeight);
            var depths = under.Count > 0 && f.ScaleBelow > 0 ? HistoryChartMath.Stack(under, f.ScaleBelow, belowHeight) : [];
            var hasTop = tops.Length > 0 && tops[^1] > 0.05;
            var hasDepth = depths.Length > 0 && depths[^1] > 0.05;
            if (!hasTop && !hasDepth)
            {
                continue;
            }

            var left = Snap(f.Plot.Left + f.Slots.Left(i), ppd) - 0.5 / ppd;
            var right = Snap(f.Plot.Left + f.Slots.Left(i) + f.Slots.BarWidth, ppd) - 0.5 / ppd;
            var width = Math.Max(right - left, 1 / ppd);
            var isToday = i == todayIndex;
            if (hasTop)
            {
                DrawStack(dc, bars, tops, zero, left, width, ppd, single, isToday, upward: true);
            }

            if (hasDepth)
            {
                dc.PushOpacity(HighContrast ? 1 : UploadOpacity);
                DrawStack(dc, bars, depths, zero, left, width, ppd, single, isToday, upward: false);
                dc.Pop();
            }

            if (caption && isToday && !string.IsNullOrEmpty(_texts.InProgress))
            {
                var text = Text(_texts.InProgress, MutedBrush, 10);
                var textWidth = text.WidthIncludingTrailingWhitespace;
                var barTop = Math.Min(zero - (hasTop ? tops[^1] : 0), bars.HasOutline && !single ? f.Y(bars.Outline(i).Above) : zero);
                var y = barTop - text.Height - 3;
                if (y >= f.Plot.Top - TopBand + 14 && textWidth <= Math.Max(f.Slots.Slot * 3, 40))
                {
                    var x = Math.Clamp(f.Plot.Left + f.Slots.Center(i) - textWidth / 2, f.Plot.Left, f.Plot.Right - textWidth);
                    dc.DrawText(text, new Point(x, Math.Max(f.Plot.Top - 2, y)));
                }
            }
        }
    }

    /// <summary>
    /// One day's stacked segments from the axis at <paramref name="zero"/>: upward (output, cost, download) with the last
    /// segment's top rounded, or downward (upload) with its bottom rounded. A hairline gap separates the segments.
    /// </summary>
    private void DrawStack(DrawingContext dc, HistoryBars bars, double[] extents, double zero, double left, double width, double ppd,
        bool single, bool isToday, bool upward)
    {
        var thin = width <= 3;
        var lastSegment = -1;
        var firstSegment = -1;
        for (var k = 0; k < extents.Length; k++)
        {
            var inner = k == 0 ? 0 : extents[k - 1];
            if (extents[k] - inner > 0.01)
            {
                lastSegment = k;
                if (firstSegment < 0)
                {
                    firstSegment = k;
                }
            }
        }

        for (var k = 0; k < extents.Length; k++)
        {
            var inner = k == 0 ? 0 : extents[k - 1];
            var height = extents[k] - inner;
            if (height <= 0.01)
            {
                continue;
            }

            double top;
            double bottom;
            if (upward)
            {
                top = zero - extents[k];
                bottom = zero - inner;
                if (k != firstSegment && height > 3)
                {
                    bottom -= 1;
                }
            }
            else
            {
                top = zero + inner;
                bottom = zero + extents[k];
                if (k != firstSegment && height > 3)
                {
                    top += 1;
                }
            }

            var source = k < bars.Order.Count ? bars.Order[k] : SourceKind.Codex;
            var brush = BarBrush(source, gradient: single && k == 0 && !HighContrast && upward, hatched: isToday);
            var rect = new Rect(left, top, width, Math.Max(bottom - top, 0.5 / ppd));
            if (k == lastSegment && !thin && rect.Height > 1)
            {
                var radius = Math.Min(3, Math.Min(width / 2, rect.Height));
                dc.DrawGeometry(brush, null, upward ? RoundedTop(rect, radius) : RoundedBottom(rect, radius));
            }
            else
            {
                dc.DrawRectangle(brush, null, rect);
            }
        }
    }

    /// <summary>
    /// Behind each day's traffic bars, with every source: a hairline outline of this computer's total, so the agents' share
    /// of all traffic is visible (DESIGN 10).
    /// </summary>
    private void DrawOutlines(DrawingContext dc, ChartFrame f, HistoryBars bars, double ppd)
    {
        var width = f.Slots.BarWidth + Math.Min(4, Math.Max(2, f.Slots.BarWidth * 0.25));
        var brush = HighContrast ? SystemColors.WindowTextBrush : LabelBrush;
        var pen = MediaColors.FrozenPen(brush, 1);
        dc.PushOpacity(HighContrast ? 0.75 : 0.45);
        for (var i = 0; i < bars.Count; i++)
        {
            var outline = bars.Outline(i);
            if (outline.Above <= 0 && outline.Below <= 0)
            {
                continue;
            }

            var top = Math.Max(f.Plot.Top, f.Y(outline.Above));
            var bottom = Math.Min(f.Plot.Bottom, f.Y(-outline.Below));
            var left = Snap(f.Plot.Left + f.Slots.Center(i) - width / 2, ppd);
            var right = Snap(f.Plot.Left + f.Slots.Center(i) + width / 2, ppd);
            var rect = new Rect(left, Snap(top, ppd), Math.Max(1, right - left), Math.Max(1, Snap(bottom, ppd) - Snap(top, ppd)));
            var radius = Math.Min(4, rect.Width / 3);
            dc.DrawRoundedRectangle(null, pen, rect, radius, radius);
        }

        dc.Pop();
    }

    /// <summary>
    /// Days of the traffic chart before measuring began: a faint hatched band, labelled <c>history.traffic.notRecorded</c>
    /// when it is wide enough (DESIGN 10).
    /// </summary>
    private void DrawNotRecorded(DrawingContext dc, ChartFrame f, double ppd)
    {
        if (!_target.IsMirrored || _target.NotRecordedDays <= 0 || f.Slots.Slot <= 0)
        {
            return;
        }

        var left = Snap(f.Plot.Left, ppd);
        var right = Snap(f.Plot.Left + _target.NotRecordedDays * f.Slots.Slot, ppd);
        var rect = new Rect(left, f.Plot.Top, Math.Max(0, right - left), f.Plot.Height);
        if (rect.Width <= 0.5)
        {
            return;
        }

        var ink = HighContrast ? SystemColors.WindowTextBrush : LabelBrush;
        dc.PushOpacity(0.05);
        dc.DrawRectangle(ink, null, rect);
        dc.Pop();
        dc.PushClip(new RectangleGeometry(rect));
        dc.PushOpacity(HighContrast ? 0.3 : 0.14);
        var pen = MediaColors.FrozenPen(ink, 1);
        for (var offset = -rect.Height; offset < rect.Width; offset += 6)
        {
            dc.DrawLine(pen, new Point(rect.Left + offset, rect.Bottom), new Point(rect.Left + offset + rect.Height, rect.Top));
        }

        dc.Pop();
        dc.Pop();
        if (rect.Width >= NotRecordedLabelWidth && !string.IsNullOrEmpty(_texts.NotRecorded))
        {
            var label = Text(_texts.NotRecorded, LabelBrush);
            label.MaxTextWidth = rect.Width - 16;
            label.TextAlignment = TextAlignment.Center;
            label.MaxLineCount = 3;
            label.Trimming = TextTrimming.CharacterEllipsis;
            dc.DrawText(label, new Point(rect.Left + 8, rect.Top + 8));
        }
    }

    private void DrawSpeedLine(DrawingContext dc, ChartFrame f, double alpha)
    {
        if (!_target.HasSpeed || alpha <= 0.01 || f.Slots.Slot <= 0 || f.SpeedTicks.Max <= 0)
        {
            return;
        }

        Point At(int i) => new(f.Plot.Left + f.Slots.Center(i),
            f.Plot.Bottom - Math.Min(1, _target.Speed(i)!.Value / f.SpeedTicks.Max) * f.Plot.Height);

        var brush = HighContrast ? SystemColors.WindowTextBrush : LineBrush;
        var pen = new Pen(brush, 1.5) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        dc.PushOpacity(alpha * (HighContrast ? 1 : 0.85));
        var smooth = f.Slots.Slot >= 8;
        foreach (var (start, count) in HistoryChartMath.Runs(_target.Speeds))
        {
            var points = Enumerable.Range(start, count).Select(At).ToArray();
            if (points.Length >= 2)
            {
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    g.BeginFigure(points[0], false, false);
                    if (smooth)
                    {
                        foreach (var s in ChartMath.CatmullRom(points.Select(p => new ChartPoint(p.X, p.Y)).ToArray()))
                        {
                            g.BezierTo(new Point(s.Control1.X, s.Control1.Y), new Point(s.Control2.X, s.Control2.Y),
                                new Point(s.End.X, s.End.Y), true, true);
                        }
                    }
                    else
                    {
                        g.PolyLineTo(points.Skip(1).ToArray(), true, true);
                    }
                }

                geometry.Freeze();
                dc.DrawGeometry(null, pen, geometry);
            }

            if (f.Slots.Slot >= 6 || points.Length == 1)
            {
                foreach (var p in points)
                {
                    dc.DrawEllipse(brush, null, p, 2.25, 2.25);
                }
            }
        }

        dc.Pop();
    }

    private void RenderOverlay()
    {
        var f = _frame;
        using (var back = _back.RenderOpen())
        {
            var index = _pointerIndex ?? _keyIndex ?? _linkedIndex;
            if (index is { } i && !f.Plot.IsEmpty && f.Slots.Slot > 0 && i < f.Slots.Count)
            {
                var width = Math.Max(f.Slots.Slot, 6);
                var x = f.Plot.Left + f.Slots.Center(i) - width / 2;
                back.DrawRoundedRectangle(HighlightBrush, null, new Rect(x, f.Plot.Top - 4, width, f.Plot.Height + 4), 4, 4);
            }
        }

        using var front = _front.RenderOpen();
        if (ActiveIndex is { } active && !f.Plot.IsEmpty && f.Slots.Slot > 0 && active < f.Slots.Count)
        {
            var ppd = PixelsPerDip;
            var x = Snap(f.Plot.Left + f.Slots.Center(active), ppd);
            var dashed = new Pen(LabelBrush, 1) { DashStyle = new DashStyle([2, 3], 0) };
            dashed.Freeze();
            front.PushOpacity(0.7);
            front.DrawLine(dashed, new Point(x, f.Plot.Top - 4), new Point(x, f.Plot.Bottom));
            front.Pop();
            if (active < _target.Count && _target.Speed(active) is { } speed && f.SpeedTicks.Max > 0)
            {
                var y = f.Plot.Bottom - Math.Min(1, speed / f.SpeedTicks.Max) * f.Plot.Height;
                var lineBrush = HighContrast ? SystemColors.WindowTextBrush : LineBrush;
                front.DrawEllipse(HighlightBrush, null, new Point(f.Plot.Left + f.Slots.Center(active), y), 6, 6);
                front.DrawEllipse(lineBrush, null, new Point(f.Plot.Left + f.Slots.Center(active), y), 3.5, 3.5);
            }
        }

        if (_showFocus && IsKeyboardFocused && ActualWidth > 4)
        {
            front.DrawRoundedRectangle(null, MediaColors.FrozenPen(FocusBrush, 2), new Rect(1, 1, ActualWidth - 2, ActualHeight - 2), 6, 6);
        }
    }

    private Brush BarBrush(SourceKind source, bool gradient, bool hatched) =>
        BarBrushes.For(HighContrast ? SystemColors.HighlightColor : HeatPalette.SourceColor(source).ToColor(), gradient, hatched);

    private static StreamGeometry RoundedTop(Rect rect, double radius)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(rect.Left, rect.Bottom), true, true);
            g.LineTo(new Point(rect.Left, rect.Top + radius), false, false);
            g.ArcTo(new Point(rect.Left + radius, rect.Top), new Size(radius, radius), 0, false, SweepDirection.Clockwise, false, false);
            g.LineTo(new Point(rect.Right - radius, rect.Top), false, false);
            g.ArcTo(new Point(rect.Right, rect.Top + radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise, false, false);
            g.LineTo(new Point(rect.Right, rect.Bottom), false, false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static StreamGeometry RoundedBottom(Rect rect, double radius)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(rect.Left, rect.Top), true, true);
            g.LineTo(new Point(rect.Right, rect.Top), false, false);
            g.LineTo(new Point(rect.Right, rect.Bottom - radius), false, false);
            g.ArcTo(new Point(rect.Right - radius, rect.Bottom), new Size(radius, radius), 0, false, SweepDirection.Clockwise, false, false);
            g.LineTo(new Point(rect.Left + radius, rect.Bottom), false, false);
            g.ArcTo(new Point(rect.Left, rect.Bottom - radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise, false, false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static double Snap(double value, double ppd) => (Math.Round(value * ppd - 0.5) + 0.5) / ppd;

    /// <summary>The layout of the last frame drawn.</summary>
    /// <param name="ScaleMax">The value at the plot's top.</param>
    /// <param name="Lower">Ticks below the axis (traffic upload); null without.</param>
    /// <param name="ScaleBelow">The value (positive) at the plot's bottom; 0 when the axis is the bottom.</param>
    private readonly record struct ChartFrame(Rect Plot, BarSlots Slots, AxisTicks Ticks, AxisTicks SpeedTicks, double ScaleMax,
        AxisTicks? Lower, double ScaleBelow)
    {
        /// <summary>Where 0 is drawn: the bottom, or between download and upload.</summary>
        public double ZeroY => Plot.Top + ScaleMax / Span * Plot.Height;

        private double Span => Math.Max(ScaleMax + Math.Max(0, ScaleBelow), 1e-9);

        /// <summary>The height of a signed value (negative: below the axis).</summary>
        public double Y(double value) => ZeroY - value / Span * Plot.Height;
    }
}
