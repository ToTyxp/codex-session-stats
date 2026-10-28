using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Theme;

namespace AgentSpeed.Orb.Render;

/// <summary>A frozen text drawing with its metrics; the baseline is at y = 0 and the left edge at x = 0.</summary>
internal sealed record TextDrawing(Drawing Drawing, double Width, double Ascent);

/// <summary>Builds and caches frozen text drawings (glyphs with an optional glow) for the orb readout.</summary>
internal sealed class TextDrawings
{
    public static readonly Typeface ValueFace = new(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    public static readonly Typeface UnitFace = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
    public static readonly Typeface LabelFace = new(new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft JhengHei UI, Microsoft YaHei UI, Yu Gothic UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    public static readonly Typeface InfoFace = new(new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft JhengHei UI, Microsoft YaHei UI, Yu Gothic UI"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
    public static readonly Typeface PillFace = new(new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft JhengHei UI, Microsoft YaHei UI, Yu Gothic UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    private const int Capacity = 512;
    private static readonly string[] Ascii = Enumerable.Range(0, 128).Select(i => ((char)i).ToString()).ToArray();
    private readonly Dictionary<Key, TextDrawing> _cache = new();
    private readonly Dictionary<(string Text, Typeface Face, double Size, double Tracking), double> _widths = new();
    private readonly Dictionary<(Typeface Face, double Size), double> _digitCells = new();
    private readonly BrushCache _brushes = new();
    private double _pixelsPerDip = 1;

    public double PixelsPerDip
    {
        get => _pixelsPerDip;
        set
        {
            if (Math.Abs(value - _pixelsPerDip) > 0.0001)
            {
                _pixelsPerDip = value;
                Clear();
            }
        }
    }

    /// <summary>Width of the widest digit (tabular cells).</summary>
    /// <summary>A one-character string without allocating for ASCII.</summary>
    public static string CharText(char ch) => ch < 128 ? Ascii[ch] : ch.ToString();

    public double DigitCell(Typeface face, double size)
    {
        var key = (face, Math.Round(size * 20) / 20);
        if (_digitCells.TryGetValue(key, out var known))
        {
            return known;
        }

        var cell = 0.0;
        for (var d = '0'; d <= '9'; d++)
        {
            cell = Math.Max(cell, Format(d.ToString(), face, size, Brushes.Black).WidthIncludingTrailingWhitespace);
        }

        _digitCells[key] = cell;
        return cell;
    }

    /// <summary>The width of a text (cached: measuring formats the text, far too slow for every frame).</summary>
    public double Measure(string text, Typeface face, double size, double tracking = 0)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var key = (text, face, Math.Round(size * 20) / 20, Math.Round(tracking * 20) / 20);
        if (_widths.TryGetValue(key, out var known))
        {
            return known;
        }

        if (_widths.Count >= Capacity)
        {
            _widths.Clear();
        }

        var width = MeasureUncached(text, face, size, tracking);
        _widths[key] = width;
        return width;
    }

    private double MeasureUncached(string text, Typeface face, double size, double tracking)
    {
        if (tracking <= 0.01 || !IsLatin(text))
        {
            return Format(text, face, size, Brushes.Black).WidthIncludingTrailingWhitespace;
        }

        var width = -tracking;
        foreach (var ch in text)
        {
            width += Format(ch.ToString(), face, size, Brushes.Black).WidthIncludingTrailingWhitespace + tracking;
        }

        return width;
    }

    /// <summary>
    /// A text drawing. <paramref name="glow"/> adds a soft halo (three widening strokes of the outline, the cheap stand-in
    /// for a Gaussian glow of σ <paramref name="glowRadius"/>); a black glow is the readout shadow of the liquid style.
    /// </summary>
    public TextDrawing Get(string text, Typeface face, double size, Rgba color, Rgba? glow = null, double glowRadius = 0,
        double tracking = 0)
    {
        var key = new Key(text, face, Math.Round(size * 20), Pack(color), glow is { } g ? Pack(g) : 0, Math.Round(glowRadius * 20),
            Math.Round(tracking * 20));
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var brush = _brushes.Get(color);
        var group = new DrawingGroup();
        double width;
        double ascent;
        using (var dc = group.Open())
        {
            var useTracking = tracking > 0.01 && IsLatin(text) && text.Length > 1;
            if (!useTracking)
            {
                var formatted = Format(text, face, size, brush);
                width = formatted.WidthIncludingTrailingWhitespace;
                ascent = formatted.Baseline;
                var origin = new Point(0, -formatted.Baseline);
                if (glow is { } halo && glowRadius > 0)
                {
                    DrawGlow(dc, formatted.BuildGeometry(origin), halo, glowRadius);
                }

                dc.DrawText(formatted, origin);
            }
            else
            {
                width = -tracking;
                ascent = 0;
                var x = 0.0;
                foreach (var ch in text)
                {
                    var formatted = Format(ch.ToString(), face, size, brush);
                    var origin = new Point(x, -formatted.Baseline);
                    if (glow is { } halo && glowRadius > 0)
                    {
                        DrawGlow(dc, formatted.BuildGeometry(origin), halo, glowRadius);
                    }

                    dc.DrawText(formatted, origin);
                    ascent = Math.Max(ascent, formatted.Baseline);
                    x += formatted.WidthIncludingTrailingWhitespace + tracking;
                    width += formatted.WidthIncludingTrailingWhitespace + tracking;
                }
            }
        }

        group.Freeze();
        var result = new TextDrawing(group, Math.Max(0, width), ascent);
        if (_cache.Count >= Capacity)
        {
            _cache.Clear();
        }

        _cache[key] = result;
        return result;
    }

    public void Clear()
    {
        _cache.Clear();
        _widths.Clear();
        _digitCells.Clear();
    }

    public static bool IsLatin(string text)
    {
        foreach (var ch in text)
        {
            if (ch > 0x024F)
            {
                return false;
            }
        }

        return true;
    }

    private FormattedText Format(string text, Typeface face, double size, Brush brush) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, Math.Max(1, size), brush, null,
            TextFormattingMode.Ideal, _pixelsPerDip);

    private void DrawGlow(DrawingContext dc, Geometry outline, Rgba color, double radius)
    {
        outline.Freeze();
        dc.DrawGeometry(null, MediaColors.FrozenPen(_brushes.Get(color, 0.10), radius * 1.6, PenLineCap.Round), outline);
        dc.DrawGeometry(null, MediaColors.FrozenPen(_brushes.Get(color, 0.16), radius * 0.9, PenLineCap.Round), outline);
        dc.DrawGeometry(null, MediaColors.FrozenPen(_brushes.Get(color, 0.22), radius * 0.35, PenLineCap.Round), outline);
    }

    private static uint Pack(Rgba c) => (uint)c.A8 << 24 | (uint)c.R8 << 16 | (uint)c.G8 << 8 | c.B8;

    private readonly record struct Key(string Text, Typeface Face, double Size, uint Color, uint Glow, double GlowRadius, double Tracking);
}
