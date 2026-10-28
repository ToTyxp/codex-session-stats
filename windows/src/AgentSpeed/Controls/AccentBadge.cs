using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AgentSpeed.Controls;

/// <summary>
/// A rounded square in the accent gradient (live reporting's badge, DESIGN 11): the two accent shades are read from the
/// theme each time the badge is loaded, so it follows the system accent.
/// </summary>
internal sealed class AccentBadge : Border
{
    public AccentBadge()
    {
        Background = Gradient(Color.FromRgb(0x4C, 0xC2, 0xFF), Color.FromRgb(0x00, 0x5A, 0x9E));
        Loaded += (_, _) => Paint();
    }

    private void Paint()
    {
        var light = TryFindResource("SystemAccentColorLight1") is Color l ? l : Color.FromRgb(0x4C, 0xC2, 0xFF);
        var dark = TryFindResource("SystemAccentColorDark1") is Color d ? d : Color.FromRgb(0x00, 0x5A, 0x9E);
        Background = Gradient(light, dark);
    }

    private static LinearGradientBrush Gradient(Color from, Color to) => new(from, to, new Point(0, 0), new Point(1, 1));
}
