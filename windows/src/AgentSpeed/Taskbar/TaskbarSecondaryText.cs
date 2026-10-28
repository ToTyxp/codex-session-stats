using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.Cost;

namespace AgentSpeed.Taskbar;

/// <summary>
/// Draws the taskbar money block (DESIGN 3): <c>即時 0.43 CN¥/s</c> over <c>今日 128.40 CN¥</c> in 10 px medium tabular
/// text, labels left-aligned in a column, both numbers right-aligned in one shared cell, units after them; labels and
/// units in the secondary color, numbers in the label color at 85 % (secondary when dim). Runs on the taskbar thread;
/// every WPF object here belongs to that thread.
/// </summary>
internal sealed class TaskbarSecondaryText
{
    public const double TextSize = 10;

    /// <summary>Baseline distance of the two lines.</summary>
    public const double Pitch = 12;

    /// <summary>Before the block, after the unit (or the value when the unit is hidden).</summary>
    public const double Gap = 8;

    public const double LabelGap = 3;
    public const double UnitGap = 2.5;

    private readonly Typeface _face = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
    private readonly Dictionary<(char, int), FormattedText> _glyphs = new();
    private readonly Dictionary<string, FormattedText> _texts = new(StringComparer.Ordinal);
    private double _scale = -1;
    private Color _primary;
    private Color _secondary;
    private double _digitWidth;

    /// <summary>Width (DIP) the wider number needs; digits are tabular, so it changes only with the digit count or format.</summary>
    public double NumberWidth(BarSecondary block, double scale, Color primary, Color secondary)
    {
        Ensure(scale, primary, secondary);
        return Math.Max(Tabular(block.Live.Number), Tabular(block.Today.Number));
    }

    /// <summary>The block's width with its gap, for a number cell of <paramref name="cell"/> DIP.</summary>
    public double Width(BarSecondary block, double cell, double scale, Color primary, Color secondary)
    {
        var number = Math.Max(cell, NumberWidth(block, scale, primary, secondary));
        return Gap + LabelWidth(block) + LabelGap + number + UnitGap + Math.Max(Text(block.Live.Unit).Width, Text(block.Today.Unit).Width);
    }

    /// <summary>Draws the block starting at <paramref name="x"/> (its gap included), the two lines centered on <paramref name="centerY"/>.</summary>
    public void Draw(DrawingContext dc, BarSecondary block, double x, double centerY, double cell, double scale, Color primary, Color secondary)
    {
        var numberWidth = NumberWidth(block, scale, primary, secondary);
        var left = x + Gap;
        var numberRight = left + LabelWidth(block) + LabelGap + Math.Max(cell, numberWidth);
        var capHeight = (_face.CapsHeight > 0 ? _face.CapsHeight : 0.7) * TextSize;
        var first = Math.Round((centerY - Pitch / 2 + capHeight / 2) * scale) / scale;
        DrawLine(dc, block.Live, left, numberRight, first, scale);
        DrawLine(dc, block.Today, left, numberRight, first + Pitch, scale);
    }

    /// <summary>The block's height (vertical taskbars stack it under the value).</summary>
    public static double Height => Pitch * 2 + 2;

    private void DrawLine(DrawingContext dc, BarMoneyLine line, double left, double numberRight, double baseline, double scale)
    {
        var label = Text(line.Label);
        dc.DrawText(label, new Point(Snap(left, scale), baseline - label.Baseline));
        var role = line.Dim ? 1 : 0;
        var x = numberRight - Tabular(line.Number);
        foreach (var c in line.Number)
        {
            var glyph = Glyph(c, role);
            var width = char.IsDigit(c) ? _digitWidth : glyph.WidthIncludingTrailingWhitespace;
            dc.DrawText(glyph, new Point(Snap(x + (width - glyph.WidthIncludingTrailingWhitespace) / 2, scale), baseline - glyph.Baseline));
            x += width;
        }

        var unit = Text(line.Unit);
        dc.DrawText(unit, new Point(Snap(numberRight + UnitGap, scale), baseline - unit.Baseline));
    }

    private double LabelWidth(BarSecondary block) => Math.Max(Text(block.Live.Label).Width, Text(block.Today.Label).Width);

    private double Tabular(string text)
    {
        var width = 0.0;
        foreach (var c in text)
        {
            width += char.IsDigit(c) ? _digitWidth : Glyph(c, 0).WidthIncludingTrailingWhitespace;
        }

        return width;
    }

    /// <summary>A label or unit in the secondary color.</summary>
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
