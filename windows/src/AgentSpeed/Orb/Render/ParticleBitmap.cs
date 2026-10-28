using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AgentSpeed.Presentation.Render;

namespace AgentSpeed.Orb.Render;

/// <summary>
/// A particle layer: particles are splatted on the CPU (<see cref="SpriteRaster"/>) into a device-pixel bitmap and only
/// the changed rectangle is uploaded, which costs far less than re-recording hundreds of ellipses every frame.
/// </summary>
internal sealed class ParticleBitmap
{
    private WriteableBitmap? _bitmap;
    private Rect _rect;
    private double _scale = 1;
    private bool _hasContent;

    public DrawingVisual Visual { get; } = new();

    public SpriteRaster? Raster { get; private set; }

    /// <summary>Device pixels per DIP.</summary>
    public double Scale => _scale;

    /// <summary>Top-left of the layer in DIP (layer coordinates).</summary>
    public Point Origin => _rect.TopLeft;

    /// <summary>Covers <paramref name="rect"/> (DIP) at <paramref name="scale"/> device pixels per DIP.</summary>
    public void Configure(Rect rect, double scale)
    {
        scale = Math.Clamp(scale, 0.5, 4);
        var width = Math.Max(1, (int)Math.Ceiling(rect.Width * scale));
        var height = Math.Max(1, (int)Math.Ceiling(rect.Height * scale));
        if (_bitmap is not null && _bitmap.PixelWidth == width && _bitmap.PixelHeight == height && _rect == rect)
        {
            return;
        }

        _rect = rect;
        _scale = scale;
        _bitmap = new WriteableBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32, null);
        Raster = new SpriteRaster(width, height);
        _hasContent = false;
        using var dc = Visual.RenderOpen();
        dc.DrawImage(_bitmap, rect);
    }

    /// <summary>Converts a point in layer DIP coordinates to raster pixels.</summary>
    public (double X, double Y) ToPixels(double x, double y) => ((x - _rect.X) * _scale, (y - _rect.Y) * _scale);

    public SpriteRaster? Begin()
    {
        if (Raster is null)
        {
            return null;
        }

        Raster.Begin();
        return Raster;
    }

    /// <summary>Uploads what changed since the previous frame.</summary>
    public unsafe void End()
    {
        if (Raster is not { } raster || _bitmap is not { } bitmap)
        {
            return;
        }

        var dirty = raster.End();
        if (dirty.IsEmpty)
        {
            return;
        }

        _hasContent = !raster.Drawn.IsEmpty;
        bitmap.Lock();
        try
        {
            var stride = bitmap.BackBufferStride;
            var target = (byte*)bitmap.BackBuffer;
            fixed (uint* source = raster.Pixels)
            {
                for (var y = dirty.Y; y < dirty.Bottom; y++)
                {
                    var from = source + y * raster.Width + dirty.X;
                    var to = (uint*)(target + y * stride) + dirty.X;
                    Buffer.MemoryCopy(from, to, dirty.Width * 4L, dirty.Width * 4L);
                }
            }

            bitmap.AddDirtyRect(new Int32Rect(dirty.X, dirty.Y, dirty.Width, dirty.Height));
        }
        finally
        {
            bitmap.Unlock();
        }
    }

    private bool _skip;

    /// <summary>
    /// Above 50 fps the particle layer updates on every other frame (36 fps at a 72 fps redline): particles keep their
    /// motion, the instrument keeps its frame rate, and the splat and upload cost halves (the main per-frame cost of the
    /// particle styles in WPF).
    /// </summary>
    public bool SkipThisFrame(double dt)
    {
        if (dt <= 0 || dt >= 0.02)
        {
            _skip = false;
            return false;
        }

        _skip = !_skip;
        return _skip;
    }

    /// <summary>True when the last uploaded frame drew anything.</summary>
    public bool HasContent => _hasContent;

    public void Clear()
    {
        if (Raster is null)
        {
            return;
        }

        Raster.Begin();
        End();
    }
}
