using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using AgentSpeed.Presentation.Formatting;

namespace AgentSpeed.Controls;

/// <summary>The traffic helper's status dot (DESIGN 10): green running, gray standby or not installed, amber outdated, red error.</summary>
internal sealed class HelperDotBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var (key, fallback) = value switch
        {
            TrafficHelperDot.Success => ("SystemFillColorSuccessBrush", Color.FromRgb(0x0F, 0x7B, 0x0F)),
            TrafficHelperDot.Caution => ("SystemFillColorCautionBrush", Color.FromRgb(0x9D, 0x5D, 0x00)),
            TrafficHelperDot.Critical => ("SystemFillColorCriticalBrush", Color.FromRgb(0xC4, 0x2B, 0x1C)),
            _ => ("TextFillColorTertiaryBrush", Color.FromRgb(0x8A, 0x8A, 0x8A)),
        };
        return Application.Current?.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
