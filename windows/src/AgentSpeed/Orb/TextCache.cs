using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace AgentSpeed.Orb;

/// <summary>Formatted text and outline geometry cache, invalidated on DPI, size or color changes.</summary>
internal sealed class TextCache
{
    private readonly Dictionary<(string Text, int Style), FormattedText> _texts = new();
    private readonly Dictionary<(string Text, int Style), Geometry> _outlines = new();
    private readonly List<TextStyle> _styles = new();
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

    /// <summary>
    /// Registers a style; returns its id. Re-registering the same values returns the same id. Ids stay valid until
    /// <see cref="TrimStyles"/>, so a frame can hold several of them at once.
    /// </summary>
    public int Style(Typeface typeface, double size, Brush brush)
    {
        for (var i = 0; i < _styles.Count; i++)
        {
            var s = _styles[i];
            if (s.Typeface.Equals(typeface) && Math.Abs(s.Size - size) < 0.0001 && ReferenceEquals(s.Brush, brush))
            {
                return i;
            }
        }

        _styles.Add(new TextStyle(typeface, size, brush));
        return _styles.Count - 1;
    }

    /// <summary>Drops every style once too many have accumulated. Call only between frames, when no id is held.</summary>
    public void TrimStyles()
    {
        if (_styles.Count > 64)
        {
            Clear();
            _styles.Clear();
        }
    }

    public FormattedText Get(string text, int style)
    {
        if (!_texts.TryGetValue((text, style), out var formatted))
        {
            var s = _styles[style];
            formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, s.Typeface, s.Size,
                s.Brush, null, TextFormattingMode.Ideal, _pixelsPerDip);
            if (_texts.Count > 512)
            {
                _texts.Clear();
            }

            _texts[(text, style)] = formatted;
        }

        return formatted;
    }

    /// <summary>Outline at the origin (top-left of the line box), for glow strokes.</summary>
    public Geometry Outline(string text, int style)
    {
        if (!_outlines.TryGetValue((text, style), out var geometry))
        {
            geometry = Get(text, style).BuildGeometry(new Point(0, 0));
            geometry.Freeze();
            if (_outlines.Count > 256)
            {
                _outlines.Clear();
            }

            _outlines[(text, style)] = geometry;
        }

        return geometry;
    }

    public void Clear()
    {
        _texts.Clear();
        _outlines.Clear();
    }

    private sealed record TextStyle(Typeface Typeface, double Size, Brush Brush);
}
