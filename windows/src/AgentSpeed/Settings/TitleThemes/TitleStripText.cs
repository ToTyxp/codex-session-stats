using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using AgentSpeed.Presentation.Titles;

namespace AgentSpeed.Settings.TitleThemes;

/// <summary>
/// A theme card's preview strip (DESIGN 5, Title themes): levels 1, 5, 9 and 13 in the heat colors of their thresholds
/// joined by a tertiary <c> · </c>, one line. It shows whole titles only (<see cref="TitleCardStrip.Choose"/>), measured
/// with the font it draws in, so it never ends on a cut title.
/// </summary>
internal sealed class TitleStripText : FrameworkElement
{
    public static readonly DependencyProperty TitlesProperty = DependencyProperty.Register(nameof(Titles), typeof(IReadOnlyList<string>),
        typeof(TitleStripText), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BrushesProperty = DependencyProperty.Register(nameof(Brushes), typeof(IReadOnlyList<Brush>),
        typeof(TitleStripText), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SeparatorBrushProperty = DependencyProperty.Register(nameof(SeparatorBrush), typeof(Brush),
        typeof(TitleStripText), new FrameworkPropertyMetadata(System.Windows.Media.Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(typeof(TitleStripText),
        new FrameworkPropertyMetadata(11.0, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(typeof(TitleStripText),
        new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure));

    private FormattedText? _text;
    private IReadOnlyList<int> _choice = [];
    private double _laidOutWidth = double.NaN;

    public TitleStripText()
    {
        SnapsToDevicePixels = true;
        IsHitTestVisible = false;
        Focusable = false;
    }

    public IReadOnlyList<string>? Titles
    {
        get => (IReadOnlyList<string>?)GetValue(TitlesProperty);
        set => SetValue(TitlesProperty, value);
    }

    /// <summary>The colors of the four titles.</summary>
    public IReadOnlyList<Brush>? Brushes
    {
        get => (IReadOnlyList<Brush>?)GetValue(BrushesProperty);
        set => SetValue(BrushesProperty, value);
    }

    public Brush SeparatorBrush
    {
        get => (Brush)GetValue(SeparatorBrushProperty);
        set => SetValue(SeparatorBrushProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    /// <summary>Indexes into <see cref="TitleCardStrip.Levels"/> of the titles shown at the current width.</summary>
    public IReadOnlyList<int> Choice => _choice;

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = Math.Ceiling(Format("Ag").Height);
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 0;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Math.Abs(finalSize.Width - _laidOutWidth) > 0.01)
        {
            _laidOutWidth = finalSize.Width;
            _text = null;
            InvalidateVisual();
        }

        return finalSize;
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == TitlesProperty || e.Property == BrushesProperty || e.Property == SeparatorBrushProperty
            || e.Property == FontSizeProperty || e.Property == FontFamilyProperty || e.Property == LanguageProperty)
        {
            _text = null;
            InvalidateVisual();
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _text = null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Titles is not { Count: 4 } titles || ActualWidth <= 0)
        {
            return;
        }

        _text ??= Build(titles, ActualWidth);
        drawingContext.DrawText(_text, new Point(0, 0));
    }

    private FormattedText Build(IReadOnlyList<string> titles, double width)
    {
        _choice = TitleCardStrip.Choose(titles, width, text => Format(text).WidthIncludingTrailingWhitespace);
        var joined = TitleCardStrip.Join(titles, _choice);
        var text = Format(joined);
        text.SetForegroundBrush(SeparatorBrush);
        var brushes = Brushes;
        var start = 0;
        for (var i = 0; i < _choice.Count; i++)
        {
            if (i > 0)
            {
                start += TitleCardStrip.Separator.Length;
            }

            var title = titles[_choice[i]];
            if (brushes is { Count: 4 } && title.Length > 0)
            {
                text.SetForegroundBrush(brushes[_choice[i]], start, title.Length);
            }

            start += title.Length;
        }

        text.MaxLineCount = 1;
        return text;
    }

    private FormattedText Format(string text) =>
        new(text, Culture(), FlowDirection, new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            FontSize, SeparatorBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    /// <summary>The window's language picks the CJK glyph forms.</summary>
    private CultureInfo Culture()
    {
        try
        {
            return Language.GetEquivalentCulture();
        }
        catch (InvalidOperationException)
        {
            return CultureInfo.CurrentUICulture;
        }
    }
}
