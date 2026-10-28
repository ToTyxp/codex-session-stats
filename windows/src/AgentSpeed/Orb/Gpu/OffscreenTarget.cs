using System.Windows.Media;
using System.Windows.Media.Imaging;
using AgentSpeed.Presentation.Orb.Gpu;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace AgentSpeed.Orb.Gpu;

/// <summary>
/// Renders draw lists into an offscreen texture and reads the pixels back (the developer capture and the deterministic
/// frame harness). Premultiplied BGRA, exactly what the composition swap chain would show.
/// </summary>
internal sealed class OffscreenTarget : IDisposable
{
    private ID3D11Texture2D? _texture;
    private ID3D11Texture2D? _staging;
    private ID3D11RenderTargetView? _view;
    private int _width;
    private int _height;
    private int _generation = -1;

    /// <summary>Draws <paramref name="list"/> at <paramref name="width"/> × <paramref name="height"/> pixels and returns the pixels, or null.</summary>
    public byte[]? Render(GpuDevice device, OrbDrawList list, int width, int height)
    {
        if (device.IsLost && !device.Recover())
        {
            return null;
        }

        Ensure(device, width, height);
        var r = device.Current;
        if (!OrbGpuRenderer.Draw(device, list, _view!, width, height))
        {
            return null;
        }

        r.Context.CopyResource(_staging!, _texture!);
        var mapped = r.Context.Map(_staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var pixels = new byte[width * height * 4];
            unsafe
            {
                var source = (byte*)mapped.DataPointer;
                for (var y = 0; y < height; y++)
                {
                    new ReadOnlySpan<byte>(source + y * mapped.RowPitch, width * 4).CopyTo(pixels.AsSpan(y * width * 4, width * 4));
                }
            }

            return pixels;
        }
        finally
        {
            r.Context.Unmap(_staging!, 0);
        }
    }

    /// <summary>The pixels as a frozen premultiplied BGRA bitmap at 96 dpi.</summary>
    public static BitmapSource ToBitmap(byte[] pixels, int width, int height)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private void Ensure(GpuDevice device, int width, int height)
    {
        if (_texture is not null && width == _width && height == _height && _generation == device.Generation)
        {
            return;
        }

        Release();
        var r = device.Current;
        _texture = r.Device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1,
            BindFlags.RenderTarget | BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None));
        _staging = r.Device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1,
            BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read, 1, 0, ResourceOptionFlags.None));
        _view = r.Device.CreateRenderTargetView(_texture);
        _width = width;
        _height = height;
        _generation = device.Generation;
    }

    private void Release()
    {
        _view?.Dispose();
        _staging?.Dispose();
        _texture?.Dispose();
        _view = null;
        _staging = null;
        _texture = null;
    }

    public void Dispose() => Release();
}
