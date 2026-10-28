using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using AgentSpeed.Core;
using AgentSpeed.Interop;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Layout;
using AgentSpeed.Theme;

namespace AgentSpeed.Orb;

/// <summary>A small non-activating overlay window placed in physical pixels (chips and alignment guides).</summary>
internal abstract class OrbOverlayWindow : Window
{
    private readonly bool _clickThrough;
    private nint _hwnd;

    protected OrbOverlayWindow(bool clickThrough)
    {
        _clickThrough = clickThrough;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        Focusable = false;
        Style = new Style(typeof(Window));
        Title = "Agent Speed Orb Overlay";
        Left = -32000;
        Top = -32000;
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            WindowEffects.AddExtendedStyle(_hwnd, WS_EX.TOOLWINDOW | WS_EX.NOACTIVATE | (_clickThrough ? WS_EX.TRANSPARENT | WS_EX.LAYERED : 0));
        };
    }

    /// <summary>Places the window at a physical-pixel rectangle.</summary>
    protected void PlacePixels(int left, int top, int width, int height)
    {
        if (!IsVisible)
        {
            Show();
        }

        if (_hwnd != 0)
        {
            User32.SetWindowPos(_hwnd, HWND.TOPMOST, left, top, Math.Max(1, width), Math.Max(1, height), SWP.NOACTIVATE | SWP.NOOWNERZORDER);
        }
    }
}

/// <summary>
/// The source chip (DESIGN 2b, Identity at a glance): a capsule with the orb's source dots and name, shown after 0.35 s
/// of hover, during a drag and for 2 s after a change; the undo chip variant carries a button.
/// </summary>
internal sealed class OrbChipWindow : OrbOverlayWindow
{
    private readonly Border _capsule = new();
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _label = new() { FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button? _button;
    private bool _dark = true;

    public OrbChipWindow(bool withButton)
        : base(clickThrough: !withButton)
    {
        SizeToContent = SizeToContent.WidthAndHeight;
        _capsule.CornerRadius = new CornerRadius(9);
        _capsule.Padding = new Thickness(8, 0, withButton ? 4 : 8, 0);
        _capsule.MinHeight = 18;
        _capsule.BorderThickness = new Thickness(1);
        _capsule.Margin = new Thickness(6);
        _capsule.Effect = null;
        _capsule.Child = _row;
        _row.Children.Add(_label);
        if (withButton)
        {
            _button = new Button
            {
                Margin = new Thickness(8, 1, 0, 1),
                Padding = new Thickness(8, 0, 8, 1),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                MinHeight = 16,
                Cursor = Cursors.Hand,
                Focusable = false,
            };
            _button.Click += (_, _) => ButtonClicked?.Invoke(this, EventArgs.Empty);
            _row.Children.Add(_button);
        }

        Content = _capsule;
    }

    public event EventHandler? ButtonClicked;

    /// <summary>Sets the content: source dots (unavailable ones faded), the text and the optional button label.</summary>
    public void SetContent(IReadOnlyList<SourceKind> dots, SourceSet unavailable, string text, string? button, bool dark,
        System.Windows.Markup.XmlLanguage language)
    {
        _dark = dark;
        Language = language;
        while (_row.Children.Count > 1 && _row.Children[0] is Ellipse)
        {
            _row.Children.RemoveAt(0);
        }

        var index = 0;
        foreach (var source in dots.Take(4))
        {
            var color = HeatPalette.SourceColor(source);
            var dot = new Ellipse
            {
                Width = 6,
                Height = 6,
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Fill = MediaColors.Frozen((dark ? color : OkLab.Darken(color, 0.1)).ToColor(unavailable.Contains(source) ? 0.35 : 1)),
            };
            _row.Children.Insert(index++, dot);
        }

        _label.Text = text;
        _label.Foreground = MediaColors.Frozen(dark ? Color.FromArgb(235, 255, 255, 255) : Color.FromArgb(230, 11, 13, 18));
        _capsule.Background = MediaColors.Frozen(dark ? Color.FromArgb(209, 20, 22, 30) : Color.FromArgb(230, 255, 255, 255));
        _capsule.BorderBrush = MediaColors.Frozen(dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(28, 0, 0, 0));
        if (_button is not null)
        {
            _button.Content = button ?? string.Empty;
        }
    }

    /// <summary>
    /// Places the chip 6 pt below the disc, or on the side facing the screen center when the space below is taken or
    /// off-screen, clamped inside the work area (physical pixels).
    /// </summary>
    public void PlaceNear(PointD center, double radius, RectD work, double scale, IReadOnlyList<(PointD Center, double Radius)> others,
        double stackOffset = 0)
    {
        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
        var width = Math.Max(1, ActualWidth * scale);
        var height = Math.Max(1, ActualHeight * scale);
        const double gap = 0;
        var below = new RectD(center.X - width / 2, center.Y + radius + gap + stackOffset, width, height);
        var placed = below;
        if (below.Bottom > work.Bottom || Overlaps(below, others))
        {
            var toRight = center.X < work.Center.X;
            placed = new RectD(toRight ? center.X + radius + gap : center.X - radius - gap - width,
                center.Y - height / 2 + stackOffset, width, height);
        }

        var x = Math.Clamp(placed.X, work.X, Math.Max(work.X, work.Right - width));
        var y = Math.Clamp(placed.Y, work.Y, Math.Max(work.Y, work.Bottom - height));
        PlacePixels((int)Math.Round(x), (int)Math.Round(y), (int)Math.Ceiling(width), (int)Math.Ceiling(height));
    }

    public void FadeIn(bool reduceMotion)
    {
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(reduceMotion ? 0.01 : 0.15)));
    }

    private static bool Overlaps(RectD rect, IReadOnlyList<(PointD Center, double Radius)> others)
    {
        foreach (var (c, r) in others)
        {
            var nx = Math.Clamp(c.X, rect.X, rect.Right);
            var ny = Math.Clamp(c.Y, rect.Y, rect.Bottom);
            var dx = c.X - nx;
            var dy = c.Y - ny;
            if (dx * dx + dy * dy < r * r)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>A 1 pt dashed alignment guide in the accent color at 60 % (DESIGN 2b), shown while dragging.</summary>
internal sealed class OrbGuideWindow : OrbOverlayWindow
{
    private readonly Line _line = new() { StrokeThickness = 1, StrokeDashArray = [4, 3], SnapsToDevicePixels = true };
    private readonly Canvas _canvas = new();

    public OrbGuideWindow()
        : base(clickThrough: true)
    {
        _canvas.Children.Add(_line);
        Content = _canvas;
    }

    public void ShowGuide(OrbGuide guide, double scale, Color accent)
    {
        var left = (int)Math.Floor(Math.Min(guide.Start.X, guide.End.X)) - 2;
        var top = (int)Math.Floor(Math.Min(guide.Start.Y, guide.End.Y)) - 2;
        var width = (int)Math.Ceiling(Math.Abs(guide.End.X - guide.Start.X)) + 4;
        var height = (int)Math.Ceiling(Math.Abs(guide.End.Y - guide.Start.Y)) + 4;
        _line.Stroke = MediaColors.Frozen(Color.FromArgb(153, accent.R, accent.G, accent.B));
        _line.X1 = (guide.Start.X - left) / scale;
        _line.Y1 = (guide.Start.Y - top) / scale;
        _line.X2 = (guide.End.X - left) / scale;
        _line.Y2 = (guide.End.Y - top) / scale;
        PlacePixels(left, top, width, height);
    }
}
