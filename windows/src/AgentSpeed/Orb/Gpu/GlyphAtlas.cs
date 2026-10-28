using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AgentSpeed.Presentation.Orb.Gpu;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace AgentSpeed.Orb.Gpu;

/// <summary>
/// The orb's glyph atlas (DESIGN 2a, Techniques): every string the readout draws, rasterized once per size and display scale
/// with WPF's own DirectWrite text stack (so the typography is the one the WPF readout always had: Segoe UI Variable, tabular
/// digits, the same tracking and CJK fallbacks), packed into one R8G8 texture: R the coverage, G the coverage blurred for the
/// glow. Shared by every orb; rebuilt when it fills up.
/// </summary>
internal sealed class GlyphAtlas
{
    private const int InitialSide = 1024;
    private const int MaxSide = 2048;
    private const int Gap = 0;

    private static readonly Typeface ValueFace = new(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Typeface UnitFace = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
    private static readonly Typeface LabelFace = new(new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft JhengHei UI, Microsoft YaHei UI, Yu Gothic UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface InfoFace = new(new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft JhengHei UI, Microsoft YaHei UI, Yu Gothic UI"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
    private static readonly Typeface PillFace = new(new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft JhengHei UI, Microsoft YaHei UI, Yu Gothic UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Typeface TrafficNumberFace = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private readonly Dictionary<(OrbTextKey Key, int Scale), OrbGlyph> _entries = new();
    private readonly Dictionary<(OrbFont Font, int Size), double> _digitCells = new();
    private readonly Dictionary<(OrbFont Font, int Size), double> _capHeights = new();
    private readonly Dictionary<int, View> _views = new();
    private readonly List<(int X, int Y, int Width, int Height, byte[] Pixels)> _pending = [];
    private int _side = InitialSide;
    private int _shelfX;
    private int _shelfY;
    private int _shelfHeight;
    private bool _full;
    private ID3D11Texture2D? _texture;
    private ID3D11ShaderResourceView? _view;
    private CultureInfo _culture = CultureInfo.CurrentUICulture;

    /// <summary>Incremented whenever entries are dropped (the atlas filled up or the language changed).</summary>
    public int Generation { get; private set; }

    /// <summary>The atlas filled up: the next frame clears it and rasterizes what it needs again.</summary>
    public bool NeedsRebuild => _full;

    /// <summary>Strings rasterized since start (for the benchmark log).</summary>
    public long Rasterized { get; private set; }

    /// <summary>The language strings are shaped for (font fallback differs between zh-Hant, zh-Hans and ja).</summary>
    public CultureInfo Culture
    {
        get => _culture;
        set
        {
            if (!Equals(_culture, value))
            {
                _culture = value;
                Clear();
            }
        }
    }

    /// <summary>The glyph source for orbs drawn at <paramref name="scale"/> pixels per point.</summary>
    public IOrbGlyphs ForScale(double scale)
    {
        var key = ScaleKey(scale);
        if (!_views.TryGetValue(key, out var view))
        {
            view = new View(this, key);
            _views[key] = view;
        }

        return view;
    }

    /// <summary>Drops the entries if the atlas filled up during the previous frames (call between frames).</summary>
    public void BeginFrame()
    {
        if (!_full)
        {
            return;
        }

        if (_side < MaxSide)
        {
            _side = MaxSide;
            _texture?.Dispose();
            _view?.Dispose();
            _texture = null;
            _view = null;
        }

        Clear();
    }

    /// <summary>Uploads what was rasterized since the last upload and returns the atlas view.</summary>
    public ID3D11ShaderResourceView Upload(ID3D11Device device, ID3D11DeviceContext context)
    {
        if (_texture is null || _view is null)
        {
            // Every path that drops the texture (first use, growth, device loss) cleared the entries already: what was
            // rasterized since is pending for this new texture and must not be dropped (a frame without text).
            _texture = device.CreateTexture2D(Vortice.DXGI.Format.R8G8_UNorm, (uint)_side, (uint)_side, 1, 1, null, BindFlags.ShaderResource);
            _view = device.CreateShaderResourceView(_texture);
        }

        foreach (var (x, y, width, height, pixels) in _pending)
        {
            unsafe
            {
                fixed (byte* data = pixels)
                {
                    context.UpdateSubresource(_texture, 0, new Box(x, y, 0, x + width, y + height, 1), (IntPtr)data, (uint)(width * 2), 0);
                }
            }
        }

        _pending.Clear();
        return _view;
    }

    /// <summary>Forgets the GPU texture (device lost or disposed); strings are rasterized again as they are drawn.</summary>
    public void ResetDevice()
    {
        _view?.Dispose();
        _texture?.Dispose();
        _view = null;
        _texture = null;
        Clear();
    }

    private void Clear()
    {
        _entries.Clear();
        _digitCells.Clear();
        _capHeights.Clear();
        _pending.Clear();
        _shelfX = 0;
        _shelfY = 0;
        _shelfHeight = 0;
        _full = false;
        Generation++;
    }

    private static int ScaleKey(double scale) => (int)Math.Round(Math.Max(0.5, scale) * 100);

    private static Typeface Face(OrbFont font) => font switch
    {
        OrbFont.Value => ValueFace,
        OrbFont.Unit => UnitFace,
        OrbFont.Title => LabelFace,
        OrbFont.Info => InfoFace,
        OrbFont.TrafficNumber or OrbFont.Arrow => TrafficNumberFace,
        _ => PillFace,
    };

    private bool TryGet(in OrbTextKey key, int scaleKey, out OrbGlyph glyph)
    {
        if (_entries.TryGetValue((key, scaleKey), out glyph))
        {
            return true;
        }

        if (_full || string.IsNullOrEmpty(key.Text))
        {
            return false;
        }

        if (!Rasterize(key, scaleKey / 100.0, out glyph))
        {
            _full = true;
            return false;
        }

        _entries[(key, scaleKey)] = glyph;
        return true;
    }

    private FormattedText Format(string text, Typeface face, double size, double pixelsPerDip) =>
        new(text, _culture, FlowDirection.LeftToRight, face, Math.Max(1, size), Brushes.White, null, TextFormattingMode.Ideal, pixelsPerDip);

    private static bool IsLatin(string text)
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

    private bool Rasterize(in OrbTextKey key, double scale, out OrbGlyph glyph)
    {
        glyph = default;
        if (key.Font == OrbFont.Arrow)
        {
            return RasterizeArrow(key, scale, out glyph);
        }

        var face = Face(key.Font);
        var size = (double)key.Size;
        var text = key.Text;
        var tracking = key.Tracking > 0.01 && text.Length > 1 && IsLatin(text) ? key.Tracking : 0;

        // Layout in points: one run, or one character at a time with tracking (as the WPF readout always did).
        var visual = new DrawingVisual();
        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Grayscale);
        TextOptions.SetTextFormattingMode(visual, TextFormattingMode.Ideal);
        double advance;
        double ascent;
        double height;
        var margin = Math.Ceiling(0.12 * size);
        var sigma = Math.Max(0, key.GlowSigma);
        var pad = margin + (sigma > 0 ? Math.Ceiling(2.4 * sigma) : 0);
        using (var dc = visual.RenderOpen())
        {
            if (tracking <= 0)
            {
                var formatted = Format(text, face, size, scale);
                advance = formatted.WidthIncludingTrailingWhitespace;
                ascent = formatted.Baseline;
                height = formatted.Height;
                dc.DrawText(formatted, new Point(pad, pad));
            }
            else
            {
                advance = -tracking;
                ascent = 0;
                height = 0;
                var x = pad;
                foreach (var ch in text)
                {
                    var formatted = Format(ch.ToString(), face, size, scale);
                    ascent = Math.Max(ascent, formatted.Baseline);
                    height = Math.Max(height, formatted.Height);
                }

                foreach (var ch in text)
                {
                    var formatted = Format(ch.ToString(), face, size, scale);
                    dc.DrawText(formatted, new Point(x, pad + ascent - formatted.Baseline));
                    var w = formatted.WidthIncludingTrailingWhitespace + tracking;
                    x += w;
                    advance += w;
                }
            }
        }

        var widthPoints = Math.Max(0, advance) + 2 * pad;
        var heightPoints = height + 2 * pad;
        var width = (int)Math.Ceiling(widthPoints * scale);
        var rows = (int)Math.Ceiling(heightPoints * scale);
        if (width <= 0 || rows <= 0 || width > _side || rows > _side)
        {
            return true;
        }

        // The entry is uploaded with a one-texel border of zeros: the texture keeps the strings of earlier generations,
        // and bilinear sampling at a quad's edge reads one texel beyond it.
        if (!Allocate(width + 2, rows + 2, out var bx, out var by))
        {
            return false;
        }

        var ax = bx + 1;
        var ay = by + 1;

        var bitmap = new RenderTargetBitmap(width, rows, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var bgra = new byte[width * rows * 4];
        bitmap.CopyPixels(bgra, width * 4, 0);
        Rasterized++;

        var coverage = new float[width * rows];
        for (var i = 0; i < coverage.Length; i++)
        {
            coverage[i] = bgra[i * 4 + 3] / 255f;
        }

        var blurred = sigma > 0 ? GaussianBlur(coverage, width, rows, sigma * scale) : null;
        var stride = width + 2;
        var texels = new byte[stride * (rows + 2) * 2];
        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = y * width + x;
                var t = ((y + 1) * stride + x + 1) * 2;
                texels[t] = (byte)Math.Round(Math.Clamp(coverage[i], 0, 1) * 255);
                texels[t + 1] = blurred is null ? (byte)0 : (byte)Math.Round(Math.Clamp(blurred[i], 0, 1) * 255);
            }
        }

        _pending.Add((bx, by, width + 2, rows + 2, texels));

        var inverse = 1.0 / _side;
        glyph = new OrbGlyph(
            (float)Math.Max(0, advance),
            (float)ascent,
            (float)-pad,
            (float)-(pad + ascent),
            (float)(width / scale),
            (float)(rows / scale),
            (float)(ax * inverse),
            (float)(ay * inverse),
            (float)((ax + width) * inverse),
            (float)((ay + rows) * inverse));
        return true;
    }

    /// <summary>
    /// The traffic readout's arrow (DESIGN 10): a vector glyph 0.50 t wide and 0.66 t tall on the baseline, stroke
    /// max(0.9 pt, 0.11 t) with round caps, pointing down (<c>↓</c>, download) or up (<c>↑</c>, upload).
    /// </summary>
    private bool RasterizeArrow(in OrbTextKey key, double scale, out OrbGlyph glyph)
    {
        glyph = default;
        var t = (double)key.Size;
        var width = 0.50 * t;
        var height = 0.66 * t;
        var stroke = Math.Max(0.9, 0.11 * t);
        var sigma = Math.Max(0, key.GlowSigma);
        var pad = Math.Ceiling(stroke) + (sigma > 0 ? Math.Ceiling(2.4 * sigma) : 0);
        var down = key.Text != "↑";
        var inset = stroke / 2;
        var midX = pad + width / 2;
        var top = pad + inset;
        var bottom = pad + height - inset;
        var head = Math.Min(width / 2 - inset, 0.26 * t);
        var tip = down ? bottom : top;
        var back = down ? tip - head : tip + head;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(midX, top), false, false);
            context.LineTo(new Point(midX, bottom), true, false);
            context.BeginFigure(new Point(midX - head, back), false, false);
            context.LineTo(new Point(midX, tip), true, false);
            context.LineTo(new Point(midX + head, back), true, false);
        }

        geometry.Freeze();
        var pen = new Pen(Brushes.White, stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        pen.Freeze();
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawGeometry(null, pen, geometry);
        }

        var pixelsWide = (int)Math.Ceiling((width + 2 * pad) * scale);
        var pixelsHigh = (int)Math.Ceiling((height + 2 * pad) * scale);
        if (pixelsWide <= 0 || pixelsHigh <= 0 || pixelsWide > _side || pixelsHigh > _side)
        {
            return true;
        }

        if (!Allocate(pixelsWide + 2, pixelsHigh + 2, out var bx, out var by))
        {
            return false;
        }

        var bitmap = new RenderTargetBitmap(pixelsWide, pixelsHigh, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var bgra = new byte[pixelsWide * pixelsHigh * 4];
        bitmap.CopyPixels(bgra, pixelsWide * 4, 0);
        Rasterized++;
        Upload(bx, by, pixelsWide, pixelsHigh, bgra, sigma * scale);

        var inverse = 1.0 / _side;
        var ax = bx + 1;
        var ay = by + 1;
        glyph = new OrbGlyph(
            (float)width,
            (float)height,
            (float)-pad,
            (float)-(pad + height),
            (float)(pixelsWide / scale),
            (float)(pixelsHigh / scale),
            (float)(ax * inverse),
            (float)(ay * inverse),
            (float)((ax + pixelsWide) * inverse),
            (float)((ay + pixelsHigh) * inverse));
        return true;
    }

    /// <summary>Queues an entry's coverage (and its blurred copy for the glow) with a one-texel border of zeros.</summary>
    private void Upload(int bx, int by, int width, int rows, byte[] bgra, double sigmaPixels)
    {
        var coverage = new float[width * rows];
        for (var i = 0; i < coverage.Length; i++)
        {
            coverage[i] = bgra[i * 4 + 3] / 255f;
        }

        var blurred = sigmaPixels > 0 ? GaussianBlur(coverage, width, rows, sigmaPixels) : null;
        var stride = width + 2;
        var texels = new byte[stride * (rows + 2) * 2];
        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = y * width + x;
                var t = ((y + 1) * stride + x + 1) * 2;
                texels[t] = (byte)Math.Round(Math.Clamp(coverage[i], 0, 1) * 255);
                texels[t + 1] = blurred is null ? (byte)0 : (byte)Math.Round(Math.Clamp(blurred[i], 0, 1) * 255);
            }
        }

        _pending.Add((bx, by, width + 2, rows + 2, texels));
    }

    /// <summary>Shelf packing; false when the atlas is full.</summary>
    private bool Allocate(int width, int height, out int x, out int y)
    {
        if (_shelfX + width + Gap > _side)
        {
            _shelfY += _shelfHeight + Gap;
            _shelfX = 0;
            _shelfHeight = 0;
        }

        if (_shelfY + height + Gap > _side)
        {
            x = 0;
            y = 0;
            return false;
        }

        x = _shelfX;
        y = _shelfY;
        _shelfX += width + Gap;
        _shelfHeight = Math.Max(_shelfHeight, height);
        return true;
    }

    /// <summary>A separable Gaussian blur (σ in pixels), for the glow channel; runs once per rasterized string.</summary>
    private static float[] GaussianBlur(float[] source, int width, int height, double sigma)
    {
        var radius = Math.Max(1, (int)Math.Ceiling(2.4 * sigma));
        var kernel = new float[radius * 2 + 1];
        var sum = 0.0;
        for (var i = -radius; i <= radius; i++)
        {
            var w = Math.Exp(-0.5 * i * i / (sigma * sigma));
            kernel[i + radius] = (float)w;
            sum += w;
        }

        for (var i = 0; i < kernel.Length; i++)
        {
            kernel[i] /= (float)sum;
        }

        var temp = new float[source.Length];
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                var acc = 0f;
                for (var k = -radius; k <= radius; k++)
                {
                    var xx = x + k;
                    if ((uint)xx < (uint)width)
                    {
                        acc += source[row + xx] * kernel[k + radius];
                    }
                }

                temp[row + x] = acc;
            }
        }

        var result = new float[source.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var acc = 0f;
                for (var k = -radius; k <= radius; k++)
                {
                    var yy = y + k;
                    if ((uint)yy < (uint)height)
                    {
                        acc += temp[yy * width + x] * kernel[k + radius];
                    }
                }

                result[y * width + x] = acc;
            }
        }

        return result;
    }

    private double DigitCell(OrbFont font, double size, double scale)
    {
        var key = (font, (int)Math.Round(size * 20));
        if (_digitCells.TryGetValue(key, out var known))
        {
            return known;
        }

        var cell = 0.0;
        for (var d = '0'; d <= '9'; d++)
        {
            cell = Math.Max(cell, Format(d.ToString(), Face(font), size, scale).WidthIncludingTrailingWhitespace);
        }

        _digitCells[key] = cell;
        return cell;
    }

    private double CapHeight(OrbFont font, double size)
    {
        var key = (font, (int)Math.Round(size * 20));
        if (_capHeights.TryGetValue(key, out var known))
        {
            return known;
        }

        var height = Face(font).TryGetGlyphTypeface(out var glyphs) ? glyphs.CapsHeight * size : 0.7 * size;
        _capHeights[key] = height;
        return height;
    }

    /// <summary>The atlas as seen by orbs at one display scale.</summary>
    private sealed class View(GlyphAtlas owner, int scaleKey) : IOrbGlyphs
    {
        public bool TryGet(in OrbTextKey key, out OrbGlyph glyph) => owner.TryGet(key, scaleKey, out glyph);

        public double DigitCell(OrbFont font, double size) => owner.DigitCell(font, size, scaleKey / 100.0);

        public double CapHeight(OrbFont font, double size) => owner.CapHeight(font, size);
    }
}
