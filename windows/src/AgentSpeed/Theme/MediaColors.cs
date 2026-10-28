using System.Windows.Media;
using AgentSpeed.Presentation.Colors;

namespace AgentSpeed.Theme;

internal static class MediaColors
{
    public static Color ToColor(this Rgba rgba) => Color.FromArgb(rgba.A8, rgba.R8, rgba.G8, rgba.B8);

    public static Color ToColor(this Rgba rgba, double alpha) => rgba.WithAlpha(rgba.A * alpha).ToColor();

    public static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static SolidColorBrush ToBrush(this Rgba rgba) => Frozen(rgba.ToColor());

    public static Pen FrozenPen(Brush brush, double thickness, PenLineCap cap = PenLineCap.Flat)
    {
        var pen = new Pen(brush, thickness) { StartLineCap = cap, EndLineCap = cap, LineJoin = PenLineJoin.Round };
        pen.Freeze();
        return pen;
    }

    public static Rgba ToRgba(this Color color) => new(color.R / 255.0, color.G / 255.0, color.B / 255.0, color.A / 255.0);
}

/// <summary>Cache of frozen solid brushes keyed by quantized color and alpha, for per-frame drawing without allocation.</summary>
internal sealed class BrushCache
{
    private readonly Dictionary<uint, SolidColorBrush> _brushes = new();

    public SolidColorBrush Get(Rgba color, double alpha = 1)
    {
        var a = (byte)Math.Clamp((int)Math.Round(color.A * alpha * 31) * 255 / 31, 0, 255);
        var key = (uint)a << 24 | (uint)color.R8 << 16 | (uint)color.G8 << 8 | color.B8;
        if (!_brushes.TryGetValue(key, out var brush))
        {
            if (_brushes.Count > 8192)
            {
                _brushes.Clear();
            }

            brush = MediaColors.Frozen(Color.FromArgb(a, color.R8, color.G8, color.B8));
            _brushes[key] = brush;
        }

        return brush;
    }

    public void Clear() => _brushes.Clear();
}
