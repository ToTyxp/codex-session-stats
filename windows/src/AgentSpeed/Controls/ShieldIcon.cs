using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AgentSpeed.Controls;

/// <summary>The Windows administrator shield (<c>SIID_SHIELD</c>) for buttons that ask for elevation, at the system's small icon size.</summary>
internal static partial class ShieldIcon
{
    private const uint SiidShield = 77;
    private const uint ShgsiIcon = 0x100;
    private const uint ShgsiSmallIcon = 0x1;

    private static ImageSource? _cached;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct StockIconInfo
    {
        public uint Size;
        public nint Icon;
        public int SystemImageIndex;
        public int IconIndex;
        public fixed char Path[260];
    }

    [LibraryImport("shell32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SHGetStockIconInfo(uint id, uint flags, ref StockIconInfo info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint icon);

    /// <summary>The shield, or null when the shell cannot provide it.</summary>
    public static unsafe ImageSource? Small()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var info = new StockIconInfo { Size = (uint)sizeof(StockIconInfo) };
        if (SHGetStockIconInfo(SiidShield, ShgsiIcon | ShgsiSmallIcon, ref info) != 0 || info.Icon == 0)
        {
            return null;
        }

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            _cached = source;
            return source;
        }
        finally
        {
            DestroyIcon(info.Icon);
        }
    }
}
