using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.Formatting;

namespace AgentSpeed.Taskbar;

/// <summary>
/// Draws the taskbar traffic block (DESIGN 10): <c>↓ 1.28 MB/s</c> over <c>↑ 64.0 KB/s</c> in 10 px medium tabular text,
/// the number right-aligned in a cell as wide as <c>8.88</c> and the unit left-aligned in a cell as wide as <c>MB/s</c>
/// (bits: <c>Mb/s</c>), so the block's width never changes with the values. Numbers in the label color at 85 %, arrows
/// and units in the secondary color (a missing value's dash too). Runs on the taskbar thread.
/// </summary>
internal sealed class TaskbarTrafficText
{
    public const double TextSize = 10;

    /// <summary>Baseline distance of the two lines.</summary>
    public const double Pitch = 12;

    /// <summary>Before the block.</summary>
    public const double Gap = 8;

    public const double ArrowGap = 2;
    public const double UnitGap = 2.5;

    private readonly Typeface _face = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
    private readonly Dictionary<(char, int), FormattedText> _glyphs = new();
    private readonly Dictionary<string, FormattedText> _texts = new(StringComparer.Ordinal);
    private double _scale = -1;
    private Color _primary;
    private Color _secondary;
    private double _digitWidth;

    /// <summary>The block's width with its gap; constant for a unit system.</summary>
    public double Width(BarTrafficBlock block, double scale, Color primary, Color secondary)
    {
        Ensure(scale, primary, secondary);
        var (arrow, number, unit) = Cells(block.Bits);
        return Gap + arrow + ArrowGap + number + UnitGap + unit;
    }

    /// <summary>The block's height (vertical taskbars stack it under the value).</summary>
    public static double Height => Pitch * 2 + 2;

    /// <summary>Draws the block starting at <paramref name="x"/> (its gap included), the two lines centered on <paramref name="centerY"/>.</summary>
    public void Draw(DrawingContext dc, BarTrafficBlock block, double x, double centerY, double scale, Color primary, Color secondary)
    {
        Ensure(scale, primary, secondary);
        var (arrow, number, _) = Cells(block.Bits);
        var left = x + Gap;
        var numberRight = left + arrow + ArrowGap + number;
        var capHeight = (_face.CapsHeight > 0 ? _face.CapsHeight : 0.7) * TextSize;
        var first = Math.Round((centerY - Pitch / 2 + capHeight / 2) * scale) / scale;
        DrawLine(dc, "↓", block.Rx, left, numberRight, first, scale);
        DrawLine(dc, "↑", block.Tx, left, numberRight, first + Pitch, scale);
    }

    private void DrawLine(DrawingContext dc, string arrow, Core.Traffic.TrafficFormat.Parts? parts, double left, double numberRight,
        double baseline, double scale)
    {
        var glyph = Text(arrow);
        dc.DrawText(glyph, new Point(Snap(left, scale), baseline - glyph.Baseline));
        var numberText = parts?.Number ?? BarTrafficBlock.Dash;
        var role = parts is null ? 1 : 0;
        var x = numberRight - Tabular(numberText, role);
        foreach (var c in numberText)
        {
            var character = Glyph(c, role);
            var width = char.IsDigit(c) ? _digitWidth : character.WidthIncludingTrailingWhitespace;
            dc.DrawText(character, new Point(Snap(x + (width - character.WidthIncludingTrailingWhitespace) / 2, scale), baseline - character.Baseline));
            x += width;
        }

        if (parts is { } value)
        {
            var unit = Text(value.Unit);
            dc.DrawText(unit, new Point(Snap(numberRight + UnitGap, scale), baseline - unit.Baseline));
        }
    }

    /// <summary>Widths of the arrow, the <c>8.88</c> number cell and the <c>MB/s</c> unit cell.</summary>
    private (double Arrow, double Number, double Unit) Cells(bool bits) =>
        (Math.Max(Text("↓").WidthIncludingTrailingWhitespace, Text("↑").WidthIncludingTrailingWhitespace),
            Tabular("8.88", 0),
            Text(bits ? "Mb/s" : "MB/s").WidthIncludingTrailingWhitespace);

    private double Tabular(string text, int role)
    {
        var width = 0.0;
        foreach (var c in text)
        {
            width += char.IsDigit(c) ? _digitWidth : Glyph(c, role).WidthIncludingTrailingWhitespace;
        }

        return width;
    }

    /// <summary>An arrow or unit in the secondary color.</summary>
    private FormattedText Text(string text)
    {
        if (!_texts.TryGetValue(text, out var formatted))
        {
            formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _face, TextSize,
                new SolidColorBrush(_secondary), null, TextFormattingMode.Ideal, _scale);
            if (_texts.Count > 32)
            {
                _texts.Clear();
            }

            _texts[text] = formatted;
        }

        return formatted;
    }

    /// <summary>A number character: role 0 in the label color at 85 %, role 1 (the dash) in the secondary color.</summary>
    private FormattedText Glyph(char c, int role)
    {
        if (!_glyphs.TryGetValue((c, role), out var text))
        {
            var color = role == 0 ? Color.FromArgb((byte)(_primary.A * 0.85), _primary.R, _primary.G, _primary.B) : _secondary;
            text = new FormattedText(c.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _face, TextSize,
                new SolidColorBrush(color), null, TextFormattingMode.Ideal, _scale);
            _glyphs[(c, role)] = text;
        }

        return text;
    }

    private void Ensure(double scale, Color primary, Color secondary)
    {
        if (Math.Abs(scale - _scale) < 0.001 && primary == _primary && secondary == _secondary)
        {
            return;
        }

        _scale = scale;
        _primary = primary;
        _secondary = secondary;
        _glyphs.Clear();
        _texts.Clear();
        _digitWidth = 0;
        for (var c = '0'; c <= '9'; c++)
        {
            _digitWidth = Math.Max(_digitWidth, Glyph(c, 0).WidthIncludingTrailingWhitespace);
        }
    }

    private static double Snap(double value, double scale) => Math.Round(value * scale) / scale;
}
