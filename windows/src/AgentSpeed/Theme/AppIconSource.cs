using System.Windows.Media.Imaging;
using AgentSpeed.App;

namespace AgentSpeed.Theme;

/// <summary>Frames of the embedded app icon (Assets/AgentSpeed.ico), when the build includes it.</summary>
internal static class AppIconSource
{
    private static BitmapDecoder? _decoder;
    private static bool _missing;

    /// <summary>The largest frame no wider than <paramref name="maxPixels"/>, else the first frame; null without an icon.</summary>
    public static BitmapFrame? Frame(int maxPixels)
    {
        var decoder = Decoder();
        if (decoder is null)
        {
            return null;
        }

        return decoder.Frames.Where(f => f.PixelWidth <= maxPixels).OrderByDescending(f => f.PixelWidth).FirstOrDefault()
            ?? decoder.Frames.FirstOrDefault();
    }

    private static BitmapDecoder? Decoder()
    {
        if (_decoder is not null || _missing)
        {
            return _decoder;
        }

        try
        {
            var uri = new Uri("pack://application:,,,/AgentSpeed;component/Assets/AgentSpeed.ico", UriKind.Absolute);
            var decoder = BitmapDecoder.Create(uri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0)
            {
                throw new IOException("Icon has no frames.");
            }

            _decoder = decoder;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or InvalidOperationException
                                       or System.Runtime.InteropServices.COMException or FileFormatException)
        {
            _missing = true;
            AppLog.Info("App icon resource not available; using the drawn gauge.");
        }

        return _decoder;
    }
}
