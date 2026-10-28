using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AgentSpeed.Presentation.History;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Motion;
using AgentSpeed.Theme;

namespace AgentSpeed.History;

/// <summary>
/// The daily table (DESIGN 4b, item 4), drawn rather than templated so hundreds of days stay instant: only the rows
/// near the visible part of the page are drawn, and hover, selection and the reveal flash live in their own layer.
/// </summary>
internal sealed class DailyTable : FrameworkElement
{
    public const double RowHeight = 32;

    /// <summary>Horizontal inset of the columns inside the row tint.</summary>
    public const double Inset = 12;

    public const double CellPadding = 8;

    /// <summary>The cell padding a narrow window tightens to before columns go below their content (DESIGN 9 adds 費用).</summary>
    public const double MinCellPadding = 4;

    /// <summary>Room kept in every header for the sort arrow, so sorting never moves a column.</summary>
    public const double SortGlyphRoom = 16;

    private const double TextSize = 13;
    private const double HeaderSize = 12;
    private const int Overscan = 16;
    private const double FlashDuration = 1.2;

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(typeof(DailyTable),
        new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.Inherits, OnTextVisualsChanged));

    public static readonly DependencyProperty TextBrushProperty = RegisterBrush(nameof(TextBrush), Brushes.Black, text: true);
    public static readonly DependencyProperty SecondaryBrushProperty = RegisterBrush(nameof(SecondaryBrush), Brushes.DimGray, text: true);
    public static readonly DependencyProperty MutedBrushProperty = RegisterBrush(nameof(MutedBrush), Brushes.Gray, text: true);
    public static readonly DependencyProperty AltRowBrushProperty = RegisterBrush(nameof(AltRowBrush), Brushes.Transparent, text: true);
    public static readonly DependencyProperty HoverBrushProperty = RegisterBrush(nameof(HoverBrush), Brushes.WhiteSmoke, text: false);
    public static readonly DependencyProperty SelectedBrushProperty = RegisterBrush(nameof(SelectedBrush), Brushes.Gainsboro, text: false);
    public static readonly DependencyProperty FocusBrushProperty = RegisterBrush(nameof(FocusBrush), Brushes.Black, text: false);
    public static readonly DependencyProperty AccentBrushProperty = RegisterBrush(nameof(AccentBrush), Brushes.SteelBlue, text: false);

    private readonly VisualCollection _layers;
    private readonly DrawingVisual _under = new();
    private readonly DrawingVisual _main = new();
    private readonly DrawingVisual _over = new();
    private readonly Dictionary<int, FormattedText[]> _cells = new();
    private readonly TextBlock _cellTipText = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 360 };
    private readonly ToolTip _cellTip = new() { Placement = PlacementMode.Bottom };
    private readonly DispatcherTimer _cellTipDelay;

    private IReadOnlyList<DaySummary> _rows = [];
    private Localizer? _loc;
    private HistoryMoney? _money;
    private double _padding = CellPadding;
    private int? _tipRow;
    private Window? _window;
    private double _pointerX;
    private DateOnly _today;
    private IReadOnlyList<ColumnSpan> _spans = [];
    private bool _peakHidden;
    private bool _trafficShown;
    private double _spansWidth = -1;
    private (int First, int Last) _rendered = (0, -1);
    private ScrollViewer? _scroller;
    private int? _hover;
    private int? _current;
    private int? _linked;
    private bool _showFocus;
    private DateOnly? _flashDay;
    private TimeSpan? _flashStart;
    private double _flash;
    private bool _flashing;

    public DailyTable()
    {
        _layers = new VisualCollection(this) { _under, _main, _over };
        _cellTip.PlacementTarget = this;
        _cellTip.Content = _cellTipText;
        Typography.SetNumeralAlignment(_cellTipText, FontNumeralAlignment.Tabular);
        // The table is one element, so the tooltip service never sees the pointer move between cells: the cost cell's
        // tip is opened and closed here, after the system's hover delay.
        _cellTipDelay = new DispatcherTimer(DispatcherPriority.Input, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(0, ToolTipService.GetInitialShowDelay(this))),
        };
        _cellTipDelay.Tick += (_, _) =>
        {
            _cellTipDelay.Stop();
            if (_tipRow is not null && IsMouseOver)
            {
                _cellTip.IsOpen = true;
            }
        };
        Focusable = true;
        FocusVisualStyle = null;
        SnapsToDevicePixels = true;
        Loaded += (_, _) =>
        {
            AttachScroller();
            _window = Window.GetWindow(this);
            if (_window is not null)
            {
                _window.Deactivated += OnWindowDeactivated;
            }
        };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                CloseCellTip();
            }
        };
        Unloaded += (_, _) =>
        {
            CloseCellTip();
            DetachScroller();
            if (_window is not null)
            {
                _window.Deactivated -= OnWindowDeactivated;
                _window = null;
            }
        };
        IsKeyboardFocusedChanged += (_, _) =>
        {
            _showFocus = IsKeyboardFocused && InputManager.Current.MostRecentInputDevice is KeyboardDevice;
            if (_showFocus && _current is null && _rows.Count > 0)
            {
                SetCurrent(0, reveal: false);
            }

            RenderOverlay();
        };
    }

    public event EventHandler? SpansChanged;

    /// <summary>The day under the pointer (null when it leaves), to highlight in the chart.</summary>
    public event EventHandler<DateOnly?>? HoverDayChanged;

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }

    public Brush SecondaryBrush { get => (Brush)GetValue(SecondaryBrushProperty); set => SetValue(SecondaryBrushProperty, value); }

    public Brush MutedBrush { get => (Brush)GetValue(MutedBrushProperty); set => SetValue(MutedBrushProperty, value); }

    public Brush AltRowBrush { get => (Brush)GetValue(AltRowBrushProperty); set => SetValue(AltRowBrushProperty, value); }

    public Brush HoverBrush { get => (Brush)GetValue(HoverBrushProperty); set => SetValue(HoverBrushProperty, value); }

    public Brush SelectedBrush { get => (Brush)GetValue(SelectedBrushProperty); set => SetValue(SelectedBrushProperty, value); }

    public Brush FocusBrush { get => (Brush)GetValue(FocusBrushProperty); set => SetValue(FocusBrushProperty, value); }

    public Brush AccentBrush { get => (Brush)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }

    public bool ReduceMotion { get; set; }

    public IReadOnlyList<DaySummary> Rows => _rows;

    /// <summary>Column positions (relative to this element), shared with the header.</summary>
    public IReadOnlyList<ColumnSpan> Spans => _spans;

    /// <summary>The padding inside each column the spans were laid out with (tighter in a narrow window).</summary>
    public double Padding => _padding;

    public int? Current => _current;

    /// <summary>
    /// Peaks exist only for days observed live, and only for all sources (METRICS 8); without any the column goes. The
    /// traffic column shows once traffic was ever measured (DESIGN 10).
    /// </summary>
    public bool IsHidden(HistoryColumn column) => column switch
    {
        HistoryColumn.Peak => _peakHidden,
        HistoryColumn.Traffic => !_trafficShown,
        _ => false,
    };

    private IEnumerable<HistoryColumn> ShownColumns => HistoryTable.Columns.Where(c => !IsHidden(c));

    /// <summary>The day highlighted from outside (the chart's hovered bar).</summary>
    public DateOnly? LinkedDay
    {
        set
        {
            int? index = value is { } day ? IndexOf(day) : null;
            if (index != _linked)
            {
                _linked = index;
                RenderUnder();
            }
        }
    }

    public void SetRows(IReadOnlyList<DaySummary> rows, DateOnly today, Localizer loc, HistoryMoney money, bool traffic = false)
    {
        var currentDay = DayOf(_current);
        var hoverDay = DayOf(_hover);
        var linkedDay = DayOf(_linked);
        _rows = rows;
        _today = today;
        _loc = loc;
        _money = money;
        _peakHidden = rows.All(r => r.Peak is null);
        _trafficShown = traffic;
        _cells.Clear();
        _current = currentDay is { } current ? IndexOf(current) : null;
        _hover = hoverDay is { } hover ? IndexOf(hover) : null;
        _linked = linkedDay is { } linked ? IndexOf(linked) : null;
        _spansWidth = -1;
        UpdateSpans();
        InvalidateMeasure();
        (UIElementAutomationPeer.FromElement(this) as DailyTablePeer)?.ResetChildrenCache();
        RenderAll(force: true);
        if (_tipRow is not null)
        {
            // Live data: the tip follows the row under the pointer, or closes when that row has gone.
            _tipRow = null;
            UpdateCellTip(_hover, _pointerX, keepOpen: true);
        }
    }

    private DateOnly? DayOf(int? index) => index is { } i && i >= 0 && i < _rows.Count ? _rows[i].Day : null;

    public int? IndexOf(DateOnly day)
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            if (_rows[i].Day == day)
            {
                return i;
            }
        }

        return null;
    }

    public string RowText(int index) =>
        _loc is { } loc && _money is { } money && index >= 0 && index < _rows.Count
            ? HistoryTable.RowText(_rows[index], _today, loc, money, ShownColumns)
            : string.Empty;

    public string AccessibleRow(int index)
    {
        if (_loc is not { } loc || _money is not { } money || index < 0 || index >= _rows.Count)
        {
            return string.Empty;
        }

        var row = _rows[index];
        return HistoryText.JoinParts(ShownColumns.Select(c =>
            HistoryText.Labelled(loc[HistoryTable.HeaderKey(c)], HistoryTable.AccessibleCell(row, c, _today, loc, money), loc)), loc);
    }

    /// <summary>Why a row's cost is partial or missing (<c>history.unpricedDay</c>, <c>history.costUnavailable</c>), for screen readers.</summary>
    public string AccessibleHelp(int index) =>
        _loc is { } loc && index >= 0 && index < _rows.Count ? HistoryText.CostNote(_rows[index].Cost, loc) ?? string.Empty : string.Empty;

    public Rect RowBounds(int index) => new(0, index * RowHeight, ActualWidth, RowHeight);

    /// <summary>Scrolls the page so <paramref name="day"/>'s row sits mid-view, selects it and flashes it for 1.2 s.</summary>
    public void Reveal(DateOnly day, SmoothScroller scroller)
    {
        if (IndexOf(day) is not { } index)
        {
            return;
        }

        _current = index;
        if (_scroller is { } viewer)
        {
            var top = TranslatePoint(new Point(0, index * RowHeight), viewer).Y + viewer.VerticalOffset;
            scroller.ScrollTo(HistoryLayout.CenteredOffset(top, RowHeight, viewer.ViewportHeight, viewer.ScrollableHeight), ReduceMotion);
        }

        _flashDay = day;
        _flashStart = null;
        _flash = 0;
        if (!_flashing)
        {
            _flashing = true;
            CompositionTarget.Rendering += OnFlashFrame;
        }

        RenderOverlay();
    }

    public bool CopyCurrent()
    {
        if (_current is not { } index || index >= _rows.Count)
        {
            return false;
        }

        try
        {
            Clipboard.SetText(RowText(index));
            return true;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return false;
        }
    }

    protected override int VisualChildrenCount => _layers.Count;

    protected override Visual GetVisualChild(int index) => _layers[index];

    protected override AutomationPeer OnCreateAutomationPeer() => new DailyTablePeer(this);

    protected override Size MeasureOverride(Size availableSize) => new(0, _rows.Count * RowHeight);

    protected override void OnRender(DrawingContext drawingContext) =>
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (sizeInfo.WidthChanged)
        {
            UpdateSpans();
            _cells.Clear();
        }

        RenderAll(force: true);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _cells.Clear();
        _spansWidth = -1;
        UpdateSpans();
        RenderAll(force: true);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var position = e.GetPosition(this);
        var index = (int)Math.Floor(position.Y / RowHeight);
        int? hover = index >= 0 && index < _rows.Count ? index : null;
        if (hover != _hover)
        {
            _hover = hover;
            RenderUnder();
            HoverDayChanged?.Invoke(this, hover is { } h ? _rows[h].Day : null);
        }

        _pointerX = position.X;
        UpdateCellTip(hover, position.X);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        CloseCellTip();
        if (_hover is not null)
        {
            _hover = null;
            RenderUnder();
            HoverDayChanged?.Invoke(this, null);
        }
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        CloseCellTip();
        var index = (int)Math.Floor(e.GetPosition(this).Y / RowHeight);
        if (index >= 0 && index < _rows.Count && e.ChangedButton is MouseButton.Left or MouseButton.Right)
        {
            Focus();
            _showFocus = false;
            SetCurrent(index, reveal: false);
            e.Handled = e.ChangedButton == MouseButton.Left;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_rows.Count == 0)
        {
            return;
        }

        var page = Math.Max(1, (int)((_scroller?.ViewportHeight ?? RowHeight * 10) / RowHeight) - 1);
        var current = _current ?? -1;
        int? next = e.Key switch
        {
            Key.Down => Math.Min(_rows.Count - 1, current + 1),
            Key.Up => Math.Max(0, current < 0 ? 0 : current - 1),
            Key.PageDown => Math.Min(_rows.Count - 1, current + page),
            Key.PageUp => Math.Max(0, current - page),
            Key.Home => 0,
            Key.End => _rows.Count - 1,
            _ => null,
        };

        if (next is { } index)
        {
            _showFocus = true;
            SetCurrent(index, reveal: true);
            e.Handled = true;
        }
        else if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = CopyCurrent();
        }
    }

    private static DependencyProperty RegisterBrush(string name, Brush fallback, bool text) => DependencyProperty.Register(
        name, typeof(Brush), typeof(DailyTable), new FrameworkPropertyMetadata(fallback, text ? OnTextVisualsChanged : OnOverlayChanged));

    private static void OnTextVisualsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var table = (DailyTable)d;
        table._cells.Clear();
        table._spansWidth = -1;
        table.UpdateSpans();
        table.RenderAll(force: true);
    }

    private static void OnOverlayChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((DailyTable)d).RenderOverlay();

    /// <summary>
    /// The cost cell explains itself (DESIGN 9): the amount with US dollars beside another currency, why it is partial
    /// (<c>history.unpricedDay</c>) or missing (<c>history.costUnavailable</c>); the traffic cell says when it was not
    /// recorded or only partly (DESIGN 10). The tip sits under the hovered cell.
    /// </summary>
    private void UpdateCellTip(int? row, double x, bool keepOpen = false)
    {
        string? text = null;
        var column = ColumnAt(x);
        if (row is { } r && r < _rows.Count && _loc is { } loc && _money is { } money && column is { } c)
        {
            text = HistoryTable.Columns[c] switch
            {
                HistoryColumn.Cost => HistoryTable.CostTooltip(_rows[r].Cost, money, loc),
                HistoryColumn.Traffic when !IsHidden(HistoryColumn.Traffic) => HistoryTrafficText.Tooltip(_rows[r].Traffic, loc),
                _ => null,
            };
        }

        if (text is null)
        {
            CloseCellTip();
            return;
        }

        if (row == _tipRow)
        {
            return;
        }

        _tipRow = row;
        var span = _spans[column!.Value];
        _cellTipText.Text = text;
        // An open tip moves to the new cell at once; a closed one waits for the hover delay.
        _cellTip.PlacementRectangle = new Rect(span.X, row!.Value * RowHeight, span.Width, RowHeight);
        if (!_cellTip.IsOpen && !keepOpen)
        {
            _cellTipDelay.Stop();
            _cellTipDelay.Start();
        }
    }

    private void OnWindowDeactivated(object? sender, EventArgs e) => CloseCellTip();

    private void CloseCellTip()
    {
        _tipRow = null;
        _cellTipDelay.Stop();
        _cellTip.IsOpen = false;
    }

    /// <summary>The index (in <see cref="HistoryTable.Columns"/>) of the shown column under <paramref name="x"/>.</summary>
    private int? ColumnAt(double x)
    {
        for (var c = 0; c < _spans.Count && c < HistoryTable.Columns.Count; c++)
        {
            if (_spans[c].Width >= 1 && x >= _spans[c].X && x < _spans[c].Right)
            {
                return c;
            }
        }

        return null;
    }

    private void SetCurrent(int index, bool reveal)
    {
        index = Math.Clamp(index, 0, _rows.Count - 1);
        _current = index;
        if (reveal)
        {
            BringIntoView(new Rect(0, index * RowHeight - RowHeight / 2, Math.Max(1, ActualWidth), RowHeight * 2));
        }

        RenderOverlay();
        (UIElementAutomationPeer.FromElement(this) as DailyTablePeer)?.RaiseFocusChanged(index);
    }

    // ---- Columns ----

    private double PixelsPerDip => VisualTreeHelper.GetDpi(this).PixelsPerDip;

    private CultureInfo Culture => _loc?.Culture ?? CultureInfo.CurrentCulture;

    private Typeface Face(FontWeight weight) => new(FontFamily, FontStyles.Normal, weight, FontStretches.Normal);

    private double Measure(string text, double size, FontWeight weight) =>
        new FormattedText(text, Culture, FlowDirection.LeftToRight, Face(weight), size, Brushes.Black, PixelsPerDip)
            .WidthIncludingTrailingWhitespace;

    private void UpdateSpans()
    {
        var width = Math.Max(0, ActualWidth - 2 * Inset);
        if (Math.Abs(width - _spansWidth) < 0.01 || _loc is not { } loc)
        {
            return;
        }

        if (_money is not { } money)
        {
            return;
        }

        _spansWidth = width;
        // Content widths, without the cell padding: the fit tightens the padding first in a narrow window.
        var specs = new List<ColumnSpec>();
        foreach (var column in HistoryTable.Columns)
        {
            var header = Measure(loc[HistoryTable.HeaderKey(column)], HeaderSize, FontWeights.SemiBold) + SortGlyphRoom;
            // Proportional digits and separators differ slightly, so the few longest texts are measured, plus a pixel of slack.
            var weight = column == HistoryColumn.Date ? FontWeights.SemiBold : FontWeights.Normal;
            var widest = _rows
                .Select(r => HistoryTable.Cell(r, column, _today, loc, money))
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(t => t.Length)
                .Take(6)
                .Select(t => Measure(t, TextSize, weight))
                .DefaultIfEmpty(0)
                .Max();
            var cell = Math.Ceiling(widest) + 2;
            if (IsHidden(column))
            {
                specs.Add(new ColumnSpec(0, 0));
                continue;
            }

            specs.Add(column switch
            {
                HistoryColumn.Models => new ColumnSpec(Math.Max(124, header), 66, 1),
                HistoryColumn.Date => new ColumnSpec(Math.Max(header, cell), cell, 0),
                _ => new ColumnSpec(Math.Max(header, cell), Math.Max(cell, 24), 0),
            });
        }

        var fit = HistoryTable.Fit(specs, width, CellPadding, MinCellPadding);
        _padding = fit.Padding;
        _spans = fit.Spans.Select(s => new ColumnSpan(s.X + Inset, s.Width)).ToArray();
        SpansChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- Rendering ----

    private void AttachScroller()
    {
        DependencyObject? node = this;
        while (node is not null and not ScrollViewer)
        {
            node = VisualTreeHelper.GetParent(node);
        }

        _scroller = node as ScrollViewer;
        if (_scroller is not null)
        {
            _scroller.ScrollChanged += OnScrollChanged;
        }

        RenderAll(force: true);
    }

    private void DetachScroller()
    {
        if (_scroller is not null)
        {
            _scroller.ScrollChanged -= OnScrollChanged;
            _scroller = null;
        }

        if (_flashing)
        {
            _flashing = false;
            CompositionTarget.Rendering -= OnFlashFrame;
        }
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange != 0)
        {
            CloseCellTip();
        }

        RenderAll(force: false);
    }

    private (int First, int Last) Visible(int overscan)
    {
        if (_scroller is not { } viewer || !IsVisible)
        {
            return HistoryLayout.RowWindow(0, 40 * RowHeight, RowHeight, _rows.Count, overscan);
        }

        var top = -TranslatePoint(new Point(0, 0), viewer).Y;
        return HistoryLayout.RowWindow(top, viewer.ViewportHeight, RowHeight, _rows.Count, overscan);
    }

    private void RenderAll(bool force)
    {
        var needed = Visible(0);
        var covered = needed.First > needed.Last
            || (needed.First >= _rendered.First && needed.Last <= _rendered.Last && _rendered.First <= _rendered.Last);
        if (force || !covered)
        {
            RenderMain(Visible(Overscan));
        }

        if (force)
        {
            RenderOverlay();
        }
    }

    private void RenderMain((int First, int Last) window)
    {
        _rendered = window;
        using var dc = _main.RenderOpen();
        if (_loc is not { } loc || _money is not { } money || _spans.Count != HistoryTable.Columns.Count || ActualWidth <= 0)
        {
            return;
        }

        foreach (var key in _cells.Keys.Where(k => k < window.First - Overscan || k > window.Last + Overscan).ToArray())
        {
            _cells.Remove(key);
        }

        for (var i = window.First; i <= window.Last && i < _rows.Count; i++)
        {
            var y = i * RowHeight;
            if (i % 2 == 1)
            {
                dc.DrawRoundedRectangle(AltRowBrush, null, new Rect(4, y, ActualWidth - 8, RowHeight), 4, 4);
            }

            if (!_cells.TryGetValue(i, out var cells))
            {
                cells = BuildCells(_rows[i], loc, money);
                _cells[i] = cells;
            }

            for (var c = 0; c < cells.Length; c++)
            {
                var span = _spans[c];
                if (span.Width < 1)
                {
                    continue;
                }

                dc.DrawText(cells[c], new Point(span.X + _padding, y + (RowHeight - cells[c].Height) / 2));
            }
        }
    }

    private FormattedText[] BuildCells(DaySummary row, Localizer loc, HistoryMoney money)
    {
        var cells = new FormattedText[HistoryTable.Columns.Count];
        for (var c = 0; c < cells.Length; c++)
        {
            var column = HistoryTable.Columns[c];
            var text = HistoryTable.Cell(row, column, _today, loc, money);
            var quietCost = column == HistoryColumn.Cost && row.Cost.SortValue is null;
            var brush = column switch
            {
                HistoryColumn.Speed when row.AverageSpeed is null => MutedBrush,
                HistoryColumn.Peak when row.Peak is null => MutedBrush,
                HistoryColumn.Traffic when row.Traffic is null => MutedBrush,
                HistoryColumn.Models when row.Models.Count == 0 => MutedBrush,
                HistoryColumn.Cost when quietCost => row.Cost.Kind == HistoryCostKind.Subscription ? SecondaryBrush : MutedBrush,
                HistoryColumn.CacheHit or HistoryColumn.Agents or HistoryColumn.Models or HistoryColumn.Peak => SecondaryBrush,
                _ => TextBrush,
            };
            var weight = column == HistoryColumn.Date && row.Day >= _today.AddDays(-1) ? FontWeights.SemiBold : FontWeights.Normal;
            if (column == HistoryColumn.Models && row.Models.Count > 2
                && Measure(text, TextSize, weight) > _spans[c].Width - 2 * _padding)
            {
                // Keep the "+n" readable: one name and the count rather than a clipped second name.
                text = HistoryText.TopModels(row.Models, loc, 1);
            }

            var size = quietCost && row.Cost.Kind == HistoryCostKind.Subscription ? TextSize - 1 : TextSize;
            var formatted = new FormattedText(text, Culture, FlowDirection.LeftToRight, Face(weight), size, brush, PixelsPerDip)
            {
                MaxTextWidth = Math.Max(1, _spans[c].Width - 2 * _padding),
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis,
                TextAlignment = HistoryTable.IsNumeric(column) ? TextAlignment.Right : TextAlignment.Left,
            };
            if (column == HistoryColumn.Cost && row.Cost.IsPartial)
            {
                // The ≥ sits small and secondary in front of the amount, as on the orb (DESIGN 9).
                formatted.SetFontSize(TextSize - 2, 0, 1);
                formatted.SetForegroundBrush(SecondaryBrush, 0, 1);
            }

            cells[c] = formatted;
        }

        return cells;
    }

    private void RenderOverlay()
    {
        RenderUnder();
        using var dc = _over.RenderOpen();
        if (_showFocus && IsKeyboardFocused && _current is { } current && current < _rows.Count)
        {
            dc.DrawRoundedRectangle(null, MediaColors.FrozenPen(FocusBrush, 2), Rect.Inflate(RowBounds(current), -3, -1), 5, 5);
        }
    }

    private void RenderUnder()
    {
        using var dc = _under.RenderOpen();
        if (_rows.Count == 0)
        {
            return;
        }

        if (_current is { } current && current < _rows.Count)
        {
            dc.DrawRoundedRectangle(SelectedBrush, null, Row(current), 4, 4);
        }

        foreach (var index in new[] { _hover, _linked })
        {
            if (index is { } i && i < _rows.Count && i != _current)
            {
                dc.DrawRoundedRectangle(HoverBrush, null, Row(i), 4, 4);
            }
        }

        if (_flash > 0.001 && _flashDay is { } day && IndexOf(day) is { } flashed)
        {
            dc.PushOpacity(_flash);
            dc.DrawRoundedRectangle(AccentBrush, null, Row(flashed), 4, 4);
            dc.Pop();
        }
    }

    private Rect Row(int index) => new(4, index * RowHeight, Math.Max(0, ActualWidth - 8), RowHeight);

    private void OnFlashFrame(object? sender, EventArgs e)
    {
        var now = e is RenderingEventArgs args ? args.RenderingTime : TimeSpan.FromMilliseconds(Environment.TickCount64);
        _flashStart ??= now;
        var t = (now - _flashStart.Value).TotalSeconds;
        const double rise = 0.12;
        _flash = t < rise
            ? 0.3 * Easing.OutQuad(t / rise)
            : 0.3 * (1 - Easing.InOutSine((t - rise) / (FlashDuration - rise)));
        if (t >= FlashDuration)
        {
            _flash = 0;
            _flashing = false;
            CompositionTarget.Rendering -= OnFlashFrame;
        }

        RenderUnder();
    }
}
