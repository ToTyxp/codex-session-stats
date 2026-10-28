namespace AgentSpeed.Interop;

internal static class WS
{
    public const int CHILD = 0x40000000;
    public const int VISIBLE = 0x10000000;
    public const int CLIPSIBLINGS = 0x04000000;
    public const int CLIPCHILDREN = 0x02000000;
    public const int POPUP = unchecked((int)0x80000000);
    public const int SYSMENU = 0x00080000;
    public const int MINIMIZEBOX = 0x00020000;
    public const int MAXIMIZEBOX = 0x00010000;
}

internal static class WS_EX
{
    public const int NOPARENTNOTIFY = 0x00000004;
    public const int TOPMOST = 0x00000008;
    public const int TRANSPARENT = 0x00000020;
    public const int TOOLWINDOW = 0x00000080;
    public const int APPWINDOW = 0x00040000;
    public const int LAYERED = 0x00080000;
    public const int NOACTIVATE = 0x08000000;
}

internal static class GWL
{
    public const int STYLE = -16;
    public const int EXSTYLE = -20;
    public const int HWNDPARENT = -8;
}

internal static class SWP
{
    public const uint NOSIZE = 0x0001;
    public const uint NOMOVE = 0x0002;
    public const uint NOZORDER = 0x0004;
    public const uint NOREDRAW = 0x0008;
    public const uint NOACTIVATE = 0x0010;
    public const uint FRAMECHANGED = 0x0020;
    public const uint SHOWWINDOW = 0x0040;
    public const uint HIDEWINDOW = 0x0080;
    public const uint NOOWNERZORDER = 0x0200;
    public const uint ASYNCWINDOWPOS = 0x4000;
}

internal static class HWND
{
    public static readonly nint TOP = 0;
    public static readonly nint BOTTOM = 1;
    public static readonly nint TOPMOST = -1;
    public static readonly nint NOTOPMOST = -2;
}

internal static class WM
{
    public const uint DESTROY = 0x0002;
    public const uint SIZE = 0x0005;
    public const uint ACTIVATE = 0x0006;
    public const uint SETTEXT = 0x000C;
    public const uint PAINT = 0x000F;
    public const uint CLOSE = 0x0010;
    public const uint QUERYENDSESSION = 0x0011;
    public const uint ERASEBKGND = 0x0014;
    public const uint ENDSESSION = 0x0016;
    public const uint SETTINGCHANGE = 0x001A;
    public const uint SETCURSOR = 0x0020;
    public const uint MOUSEACTIVATE = 0x0021;
    public const uint WINDOWPOSCHANGED = 0x0047;
    public const uint CONTEXTMENU = 0x007B;
    public const uint STYLECHANGING = 0x007C;
    public const uint DISPLAYCHANGE = 0x007E;
    public const uint NCDESTROY = 0x0082;
    public const uint NCHITTEST = 0x0084;
    public const uint KEYDOWN = 0x0100;
    public const uint TIMER = 0x0113;
    public const uint MOUSEMOVE = 0x0200;
    public const uint LBUTTONDOWN = 0x0201;
    public const uint LBUTTONUP = 0x0202;
    public const uint LBUTTONDBLCLK = 0x0203;
    public const uint RBUTTONDOWN = 0x0204;
    public const uint RBUTTONUP = 0x0205;
    public const uint MOUSEHOVER = 0x02A1;
    public const uint MOUSELEAVE = 0x02A3;
    public const uint DPICHANGED = 0x02E0;
    public const uint DPICHANGED_BEFOREPARENT = 0x02E2;
    public const uint DPICHANGED_AFTERPARENT = 0x02E3;
    public const uint THEMECHANGED = 0x031A;
    public const uint DWMCOMPOSITIONCHANGED = 0x031E;
    public const uint USER = 0x0400;
    public const uint APP = 0x8000;
}

internal static class MA
{
    public const nint NOACTIVATE = 3;
}

internal static class TME
{
    public const uint HOVER = 0x00000001;
    public const uint LEAVE = 0x00000002;
    public const uint CANCEL = 0x80000000;
    public const uint HOVER_DEFAULT = 0xFFFFFFFF;
}

internal static class NIM
{
    public const uint ADD = 0x00000000;
    public const uint MODIFY = 0x00000001;
    public const uint DELETE = 0x00000002;
    public const uint SETFOCUS = 0x00000003;
    public const uint SETVERSION = 0x00000004;
}

internal static class NIF
{
    public const uint MESSAGE = 0x00000001;
    public const uint ICON = 0x00000002;
    public const uint TIP = 0x00000004;
    public const uint STATE = 0x00000008;
    public const uint INFO = 0x00000010;
    public const uint SHOWTIP = 0x00000080;
}

internal static class NIIF
{
    public const uint INFO = 0x00000001;
    public const uint USER = 0x00000004;
    public const uint LARGE_ICON = 0x00000020;
    public const uint RESPECT_QUIET_TIME = 0x00000080;
}

internal static class NIN
{
    public const uint SELECT = WM.USER + 0;
    public const uint KEYSELECT = WM.USER + 1;
    public const uint BALLOONSHOW = WM.USER + 2;
    public const uint BALLOONHIDE = WM.USER + 3;
    public const uint BALLOONTIMEOUT = WM.USER + 4;
    public const uint BALLOONUSERCLICK = WM.USER + 5;
    public const uint POPUPOPEN = WM.USER + 6;
    public const uint POPUPCLOSE = WM.USER + 7;
    public const uint NOTIFYICON_VERSION_4 = 4;
}

internal static class ABM
{
    public const uint GETSTATE = 0x00000004;
    public const uint GETTASKBARPOS = 0x00000005;
}

internal static class ABE
{
    public const uint LEFT = 0;
    public const uint TOP = 1;
    public const uint RIGHT = 2;
    public const uint BOTTOM = 3;
}

internal static class MONITOR
{
    public const uint DEFAULTTONULL = 0;
    public const uint DEFAULTTOPRIMARY = 1;
    public const uint DEFAULTTONEAREST = 2;
    public const uint MONITORINFOF_PRIMARY = 1;
}

internal static class GW
{
    public const uint HWNDPREV = 3;
    public const uint OWNER = 4;
}

internal static class GA
{
    public const uint ROOT = 2;
}

internal static class SM
{
    public const int CXICON = 11;
    public const int CXSMICON = 49;
    public const int CYSMICON = 50;
}

internal static class DWMWA
{
    public const int USE_IMMERSIVE_DARK_MODE = 20;
    public const int WINDOW_CORNER_PREFERENCE = 33;
    public const int BORDER_COLOR = 34;
    public const int SYSTEMBACKDROP_TYPE = 38;
    public const int CLOAKED = 14;
}

internal static class DWMWCP
{
    public const int DEFAULT = 0;
    public const int DONOTROUND = 1;
    public const int ROUND = 2;
    public const int ROUNDSMALL = 3;
}

internal static class DWMSBT
{
    public const int AUTO = 0;
    public const int NONE = 1;
    public const int MAINWINDOW = 2;
    public const int TRANSIENTWINDOW = 3;
    public const int TABBEDWINDOW = 4;
}

internal static class QUNS
{
    public const int NOT_PRESENT = 1;
    public const int BUSY = 2;
    public const int RUNNING_D3D_FULL_SCREEN = 3;
    public const int PRESENTATION_MODE = 4;
    public const int ACCEPTS_NOTIFICATIONS = 5;
    public const int QUIET_TIME = 6;
    public const int APP = 7;
}

internal static class MSGFLT
{
    public const uint ALLOW = 1;
}

internal static class ULW
{
    public const uint ALPHA = 0x00000002;
}

internal static class AC
{
    public const byte SRC_OVER = 0x00;
    public const byte SRC_ALPHA = 0x01;
}

internal static class SW
{
    public const int HIDE = 0;
    public const int SHOWNOACTIVATE = 4;
    public const int SHOWNA = 8;
}

internal static class IDC
{
    public const nint ARROW = 32512;
    public const nint HAND = 32649;
}

internal static class Errors
{
    public const int ERROR_CLASS_ALREADY_EXISTS = 1410;
}

internal static class WinEvent
{
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    public const int OBJID_WINDOW = 0;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
}
