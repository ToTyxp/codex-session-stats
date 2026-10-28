using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AgentSpeed.Core.Reporting;

namespace AgentSpeed.Reporting;

internal static class ReportAvatarBitmap
{
    public static (byte[] Png, BitmapSource Preview) Prepare(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length is <= 0 or > ReportAvatarImage.MaxInputBytes) throw new InvalidDataException();
        var bytes = new byte[(int)file.Length];
        file.ReadExactly(bytes);
        if (ReportAvatarImage.ReadHeader(bytes) is null) throw new InvalidDataException();
        using var stream = new MemoryStream(bytes, writable: false);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count != 1) throw new InvalidDataException();
        BitmapSource image = decoder.Frames[0];
        var orientation = 1;
        if (image is BitmapFrame { Metadata: BitmapMetadata metadata })
        {
            try { orientation = Convert.ToInt32(metadata.GetQuery("/app1/ifd/{ushort=274}"), System.Globalization.CultureInfo.InvariantCulture); }
            catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException) { }
        }
        var matrix = orientation switch
        {
            2 => new Matrix(-1, 0, 0, 1, 0, 0), 3 => new Matrix(-1, 0, 0, -1, 0, 0),
            4 => new Matrix(1, 0, 0, -1, 0, 0), 5 => new Matrix(0, 1, 1, 0, 0, 0),
            6 => new Matrix(0, 1, -1, 0, 0, 0), 7 => new Matrix(0, -1, -1, 0, 0, 0),
            8 => new Matrix(0, -1, 1, 0, 0, 0), _ => Matrix.Identity,
        };
        if (!matrix.IsIdentity) image = new TransformedBitmap(image, new MatrixTransform(matrix));
        var size = Math.Min(image.PixelWidth, image.PixelHeight);
        var crop = new CroppedBitmap(image, new Int32Rect((image.PixelWidth - size) / 2, (image.PixelHeight - size) / 2, size, size));
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen()) context.DrawImage(crop, new Rect(0, 0, ReportAvatarImage.Size, ReportAvatarImage.Size));
        var rendered = new RenderTargetBitmap(ReportAvatarImage.Size, ReportAvatarImage.Size, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(drawing);
        rendered.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rendered));
        using var png = new MemoryStream();
        encoder.Save(png);
        var output = png.ToArray();
        if (!ReportAvatarImage.IsUpload(output)) throw new InvalidDataException();
        return (output, rendered);
    }

    public static BitmapSource Preview(byte[] png)
    {
        if (!ReportAvatarImage.IsUpload(png)) throw new InvalidDataException();
        using var stream = new MemoryStream(png, writable: false);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count != 1) throw new InvalidDataException();
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }
}
