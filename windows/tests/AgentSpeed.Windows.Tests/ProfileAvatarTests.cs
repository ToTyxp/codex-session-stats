using System.Runtime.ExceptionServices;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AgentSpeed.Core.Reporting;
using AgentSpeed.Reporting;

namespace AgentSpeed.Windows.Tests;

public sealed class ProfileAvatarTests
{
    [Fact]
    public void PngIsCenterCroppedReencodedWithoutMetadataAndTheSourceIsPreserved() => OnSta(() =>
    {
        var path = Path.Combine(Path.GetTempPath(), "agent-speed-avatar-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            var image = Source(120, 60, (x, _) => x is < 30 or >= 90 ? Colors.Red : Colors.Lime);
            var metadata = new BitmapMetadata("png");
            metadata.SetQuery("/tEXt/{str=Comment}", "GPS-SENTINEL-DO-NOT-UPLOAD");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image, null, metadata, null));
            using (var file = File.Create(path)) encoder.Save(file);
            var original = SHA256.HashData(File.ReadAllBytes(path));
            var prepared = ReportAvatarBitmap.Prepare(path);
            Assert.True(ReportAvatarImage.IsUpload(prepared.Png));
            Assert.Equal((256, 256), (prepared.Preview.PixelWidth, prepared.Preview.PixelHeight));
            Assert.Equal(original, SHA256.HashData(File.ReadAllBytes(path)));
            Assert.DoesNotContain("GPS-SENTINEL", Encoding.UTF8.GetString(prepared.Png), StringComparison.Ordinal);
            using var png = new MemoryStream(prepared.Png);
            Assert.Single(new PngBitmapDecoder(png, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames);
            foreach (var (x, y) in new[] { (10, 10), (128, 128), (245, 245) })
            {
                var color = Pixel(prepared.Preview, x, y);
                Assert.True(color.G > 240 && color.R < 10 && color.B < 10);
            }
        }
        finally { File.Delete(path); }
    });

    [Fact]
    public void JpegOrientationIsAppliedBeforeCroppingAndNoExifIsUploaded() => OnSta(() =>
    {
        var path = Path.Combine(Path.GetTempPath(), "agent-speed-avatar-" + Guid.NewGuid().ToString("N") + ".jpg");
        try
        {
            var image = Source(120, 80, (x, y) => y < 40 ? (x < 60 ? Colors.Red : Colors.Lime) : (x < 60 ? Colors.Blue : Colors.Yellow));
            var metadata = new BitmapMetadata("jpg");
            metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)6);
            var encoder = new JpegBitmapEncoder { QualityLevel = 100 };
            encoder.Frames.Add(BitmapFrame.Create(image, null, metadata, null));
            using (var file = File.Create(path)) encoder.Save(file);
            var prepared = ReportAvatarBitmap.Prepare(path);
            var topLeft = Pixel(prepared.Preview, 24, 24);
            var topRight = Pixel(prepared.Preview, 230, 24);
            Assert.True(topLeft.B > 180 && topLeft.R < 80 && topLeft.G < 80);
            Assert.True(topRight.R > 180 && topRight.G < 80 && topRight.B < 80);
            Assert.True(ReportAvatarImage.IsUpload(prepared.Png));
            Assert.DoesNotContain("Exif", Encoding.ASCII.GetString(prepared.Png), StringComparison.Ordinal);
        }
        finally { File.Delete(path); }
    });

    [Fact]
    public void SvgAndTruncatedImagesAreRejectedBeforeUpload() => OnSta(() =>
    {
        var path = Path.Combine(Path.GetTempPath(), "agent-speed-avatar-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            File.WriteAllText(path, "<svg xmlns='http://www.w3.org/2000/svg'><script/></svg>");
            Assert.Throws<InvalidDataException>(() => ReportAvatarBitmap.Prepare(path));
            File.WriteAllBytes(path, [137, 80, 78, 71, 13, 10, 26, 10]);
            Assert.Throws<InvalidDataException>(() => ReportAvatarBitmap.Prepare(path));
        }
        finally { File.Delete(path); }
    });

    private static BitmapSource Source(int width, int height, Func<int, int, Color> color)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var c = color(x, y); var at = (y * width + x) * 4;
            pixels[at] = c.B; pixels[at + 1] = c.G; pixels[at + 2] = c.R; pixels[at + 3] = 255;
        }
        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
    }
    private static Color Pixel(BitmapSource source, int x, int y)
    {
        var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var bytes = new byte[4];
        bitmap.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), bytes, 4, 0);
        return Color.FromArgb(bytes[3], bytes[2], bytes[1], bytes[0]);
    }
    private static void OnSta(Action work)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { work(); } catch (Exception exception) { error = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
