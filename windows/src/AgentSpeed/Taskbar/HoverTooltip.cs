using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Documents;
using System.Windows.Media.Effects;
using AgentSpeed.Interop;
using AgentSpeed.Theme;

namespace AgentSpeed.Taskbar;

/// <summary>Fluent tooltip for the taskbar widget, positioned above (or beside) it in physical pixels. Click-through.</summary>
internal sealed class HoverTooltip : Window
{
    private const double ShadowMargin = 14;

    private readonly TextBlock _primary;
    private readonly TextBlock _secondary;
    private RECT _anchor;
    private bool _reduceMotion;

    public HoverTooltip()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Focusable = false;
        IsHitTestVisible = false;
        Style = new Style(typeof(Window));
        Left = -32000;
        Top = -32000;
        Title = "Agent Speed";

        _primary = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold };
        _primary.SetResourceReference(TextBlock.ForegroundProperty, "ToolTipForegroundBrush");
        _primary.SetResourceReference(TextBlock.FontFamilyProperty, AppFonts.UiFontKey);
        _secondary = new TextBlock { FontSize = 12, Margin = new Thickness(0, 2, 0, 0) };
        _secondary.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        _secondary.SetResourceReference(TextBlock.FontFamilyProperty, AppFonts.UiFontKey);
        Typography.SetNumeralAlignment(_primary, FontNumeralAlignment.Tabular);
        Typography.SetNumeralAlignment(_secondary, FontNumeralAlignment.Tabular);

        var stack = new StackPanel();
        stack.Children.Add(_primary);
        stack.Children.Add(_secondary);

        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 8, 12, 9),
            Margin = new Thickness(ShadowMargin),
            Child = stack,
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Direction = 270, Opacity = 0.22, Color = Colors.Black },
        };
        border.SetResourceReference(Border.BackgroundProperty, "ToolTipBackgroundBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "ToolTipBorderBrush");
        Content = border;

        SourceInitialized += (_, _) => WindowEffects.AddExtendedStyle(new WindowInteropHelper(this).Handle,
            WS_EX.TOOLWINDOW | WS_EX.NOACTIVATE | WS_EX.TRANSPARENT);
        SizeChanged += (_, _) => Reposition();
    }

    public void ShowFor(RECT anchor, string primary, string secondary, bool reduceMotion)
    {
        _anchor = anchor;
        _reduceMotion = reduceMotion;
        SetText(primary, secondary);
        if (!IsVisible)
        {
            Opacity = 0;
            Show();
        }

        Reposition();
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, reduceMotion ? TimeSpan.Zero : TimeSpan.FromMilliseconds(140)));
    }

    public void SetText(string primary, string secondary)
    {
        _primary.Text = primary;
        _secondary.Text = secondary;
        _secondary.Visibility = string.IsNullOrEmpty(secondary) ? Visibility.Collapsed : Visibility.Visible;
    }

    public void HideTip()
    {
        if (!IsVisible)
        {
            return;
        }

        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        Hide();
    }

    private void Reposition()
    {
        if (!IsVisible)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0)
        {
            return;
        }

        var center = new POINT((_anchor.Left + _anchor.Right) / 2, (_anchor.Top + _anchor.Bottom) / 2);
        var monitor = Monitors.FromPoint(center);
        var scale = monitor?.Scale ?? 1;
        var width = (int)Math.Ceiling(ActualWidth * scale);
        var height = (int)Math.Ceiling(ActualHeight * scale);
        var margin = (int)Math.Round(ShadowMargin * scale);
        var gap = (int)Math.Round(8 * scale) - margin;

        var work = monitor?.WorkArea ?? new RECT(0, 0, 10000, 10000);
        var bounds = monitor?.Bounds ?? work;
        int x, y;
        if (_anchor.Top >= work.Bottom - 2 || _anchor.Top > (bounds.Top + bounds.Bottom) / 2)
        {
            x = center.X - width / 2;
            y = _anchor.Top - gap - height;
        }
        else if (_anchor.Bottom <= work.Top + 2)
        {
            x = center.X - width / 2;
            y = _anchor.Bottom + gap;
        }
        else if (_anchor.Left > (bounds.Left + bounds.Right) / 2)
        {
            x = _anchor.Left - gap - width;
            y = center.Y - height / 2;
        }
        else
        {
            x = _anchor.Right + gap;
            y = center.Y - height / 2;
        }

        x = Math.Clamp(x, bounds.Left - margin, Math.Max(bounds.Left - margin, bounds.Right - width + margin));
        User32.SetWindowPos(hwnd, HWND.TOPMOST, x, y, 0, 0, SWP.NOSIZE | SWP.NOACTIVATE);
    }
}
