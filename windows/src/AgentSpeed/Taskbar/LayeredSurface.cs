using System.Runtime.InteropServices;
using AgentSpeed.Interop;

namespace AgentSpeed.Taskbar;

/// <summary>
/// A reusable 32 bpp top-down DIB section selected into a memory DC, presented with UpdateLayeredWindow.
/// GDI objects are recreated only when the size changes and are always released.
/// </summary>
internal sealed unsafe class LayeredSurface : IDisposable
{
    private nint _memoryDc;
    private nint _bitmap;
    private nint _previousBitmap;

    public int Width { get; private set; }

    public int Height { get; private set; }

    public nint Bits { get; private set; }

    public int ByteCount => Width * Height * 4;

    public bool Ensure(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (_bitmap != 0 && width == Width && height == Height)
        {
            return true;
        }

        ReleaseBitmap();
        if (_memoryDc == 0)
        {
            _memoryDc = Gdi32.CreateCompatibleDC(0);
            if (_memoryDc == 0)
            {
                return false;
            }
        }

        var info = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = width,
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = Gdi32.BI_RGB,
            },
        };

        _bitmap = Gdi32.CreateDIBSection(_memoryDc, info, Gdi32.DIB_RGB_COLORS, out var bits, 0, 0);
        if (_bitmap == 0 || bits == 0)
        {
            _bitmap = 0;
            return false;
        }

        _previousBitmap = Gdi32.SelectObject(_memoryDc, _bitmap);
        Bits = bits;
        Width = width;
        Height = height;
        return true;
    }

    /// <summary>Pushes the bitmap to the window. Position is managed separately with SetWindowPos.</summary>
    public bool Present(nint hwnd, out int error)
    {
        error = 0;
        if (_bitmap == 0 || hwnd == 0)
        {
            return false;
        }

        var size = new SIZE(Width, Height);
        var source = new POINT(0, 0);
        var blend = new BLENDFUNCTION
        {
            BlendOp = AC.SRC_OVER,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = AC.SRC_ALPHA,
        };

        if (User32.UpdateLayeredWindow(hwnd, 0, null, &size, _memoryDc, &source, 0, &blend, ULW.ALPHA))
        {
            return true;
        }

        error = Marshal.GetLastPInvokeError();
        return false;
    }

    public void Dispose()
    {
        ReleaseBitmap();
        if (_memoryDc != 0)
        {
            Gdi32.DeleteDC(_memoryDc);
            _memoryDc = 0;
        }
    }

    private void ReleaseBitmap()
    {
        if (_bitmap == 0)
        {
            return;
        }

        if (_memoryDc != 0 && _previousBitmap != 0)
        {
            Gdi32.SelectObject(_memoryDc, _previousBitmap);
        }

        Gdi32.DeleteObject(_bitmap);
        _bitmap = 0;
        _previousBitmap = 0;
        Bits = 0;
        Width = 0;
        Height = 0;
    }
}
