using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AgentSpeed.Controls;
using AgentSpeed.Interop;

namespace AgentSpeed.Tray;

/// <summary>Draws the tray gauge icon with WPF and converts it to an HICON (straight-alpha 32 bpp).</summary>
internal static class TrayIconRenderer
{
    public static nint CreateIcon(int size, double heat, Color labelColor, bool monochrome)
    {
        size = Math.Clamp(size, 16, 256);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var stroke = Math.Max(2.0, Math.Round(size / 8.0));
            var inset = size * 0.04;
            var bounds = new Rect(inset, inset, size - 2 * inset, size - 2 * inset);
            GaugeGlyphPainter.Draw(dc, bounds, heat, labelColor, monochrome, strokeThickness: stroke, trackAlpha: 0.38);

            var ringRadius = (bounds.Height - stroke) / 2;
            var center = new Point(size / 2.0, size / 2.0 + ringRadius * 0.1464);
            var brush = new SolidColorBrush(labelColor);
            var angle = Presentation.Gauge.GaugeLayout.AngleForFraction(heat);
            var tip = GaugeGlyphPainter.Polar(center, ringRadius - stroke * 1.1, angle);
            var needle = new Pen(brush, Math.Max(1.5, size / 12.0)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(needle, center, tip);
            var hub = Math.Max(1.25, size * 0.09);
            dc.DrawEllipse(brush, null, center, hub, hub);
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var straight = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = size * 4;
        var pixels = new byte[stride * size];
        straight.CopyPixels(pixels, stride, 0);
        return CreateIconFromBgra(pixels, size);
    }

    private static nint CreateIconFromBgra(byte[] pixels, int size)
    {
        var info = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = size,
                biHeight = -size,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = Gdi32.BI_RGB,
            },
        };

        nint color = 0;
        nint mask = 0;
        try
        {
            color = Gdi32.CreateDIBSection(0, info, Gdi32.DIB_RGB_COLORS, out var bits, 0, 0);
            if (color == 0 || bits == 0)
            {
                return 0;
            }

            Marshal.Copy(pixels, 0, bits, pixels.Length);

            var maskStride = (size + 15) / 16 * 2;
            var maskBits = new byte[maskStride * size];
            var pinned = GCHandle.Alloc(maskBits, GCHandleType.Pinned);
            try
            {
                mask = Gdi32.CreateBitmap(size, size, 1, 1, pinned.AddrOfPinnedObject());
            }
            finally
            {
                pinned.Free();
            }

            if (mask == 0)
            {
                return 0;
            }

            var iconInfo = new ICONINFO { fIcon = 1, hbmMask = mask, hbmColor = color };
            return User32.CreateIconIndirect(iconInfo);
        }
        finally
        {
            if (color != 0)
            {
                Gdi32.DeleteObject(color);
            }

            if (mask != 0)
            {
                Gdi32.DeleteObject(mask);
            }
        }
    }
}
