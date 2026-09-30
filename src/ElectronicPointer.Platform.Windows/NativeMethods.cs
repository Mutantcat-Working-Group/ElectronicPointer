using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Mutantcat.ElectronicPointer.Platform.Windows;

/// <summary>
/// The Win32 surface the overlay needs. Kept in one file so the native dependency list is
/// easy to audit, and so every entry point is documented once.
/// </summary>
internal static partial class NativeMethods
{
    public const int GwlExStyle = -20;
    public const int WsExLayered = 0x00080000;
    public const int WsExTransparent = 0x00000020;
    public const int WsExToolWindow = 0x00000080;
    public const int WsExNoActivate = 0x08000000;
    public const int WsExTopmost = 0x00000008;

    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoMove = 0x0002;
    public const uint SwpNoZOrder = 0x0004;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpFrameChanged = 0x0020;
    public const uint SwpShowWindow = 0x0040;

    public const int HwndTopmost = -1;
    public const int HwndNoTopmost = -2;

    public const int WmHotkey = 0x0312;
    public const int WmDestroy = 0x0002;

    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;

    // DWMWA_WINDOW_CORNER_PREFERENCE and its values, for the window manager that rounds
    // the corners of a frameless window whether the application asked for it or not.
    public const int DwmwaWindowCornerPreference = 33;

    // DWM_WINDOW_CORNER_PREFERENCE: 0 default, 1 do not round, 2 round, 3 round small.
    // Two is the value that asks for the system's rounding, so 1 is the only one that
    // keeps the application's own corners on the screen.
    public const int DwmwcpDoNotRound = 1;

    public const int SmXVirtualScreen = 76;
    public const int SmYVirtualScreen = 77;
    public const int CxVirtualScreen = 78;
    public const int CyVirtualScreen = 79;
    public const int LogPixelsX = 88;

    public const uint BiRgb = 0;

    public const int Srccopy = 0x00CC0020;
    public const int DibRgbColors = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MonitorInfoEx
    {
        public int CbSize;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public char[] Device;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfoHeader
    {
        public uint BiSize;
        public int BiWidth;
        public int BiHeight;
        public ushort BiPlanes;
        public ushort BiBitCount;
        public uint BiCompression;
        public uint BiSizeImage;
        public int BiXPelsPerMeter;
        public int BiYPelsPerMeter;
        public uint BiClrUsed;
        public uint BiClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfo
    {
        public BitmapInfoHeader BmiHeader;
        public uint BmiColorsRgbBlue;
        public uint BmiColorsRgbGreen;
        public uint BmiColorsRgbRed;
        public uint BmiColorsRgbReserved;
    }

    public delegate IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    public delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hwnd);

    // The desktop window manager, which owns the rounded corners a frameless window is
    // given from Windows 11 onwards.
    [DllImport("dwmapi.dll", SetLastError = true)]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClass(ref WndClassW classAttributes);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowEx(
        uint exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")]
    public static extern int GetMessage(out Msg message, IntPtr hwnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref Msg message);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage(ref Msg message);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clipRect, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateDC(string? driver, string? device, string? output, IntPtr initData);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr handle);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr handle);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int srcX, int srcY, int rasterOperation);

    [DllImport("gdi32.dll")]
    public static extern int GetDeviceCaps(IntPtr hdc, int index);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WndClassW
    {
        public uint Style;
        public WindowProc WindowProc;
        public int ClsExtra;
        public int WndExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr BackgroundBrush;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? MenuName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string ClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PointX;
        public int PointY;
    }

    /// <summary>Opens the registry key the Run entry lives in, creating it if needed.</summary>
    /// <summary>Raises the last Win32 error, so a silently ignored failure cannot hide.</summary>
    internal static void ThrowIfFailed(string operation)
    {
        var error = Marshal.GetLastWin32Error();
        if (error != 0)
            throw new Win32Exception(error, $"{operation} failed with Win32 error {error}.");
    }

    // Registry. Declared by hand instead of using Microsoft.Win32.Registry so this project
    // stays on plain net9.0 and does not need the Windows Compatibility pack.
    public static readonly IntPtr HKeyCurrentUser = new(unchecked((int)0x80000001));

    public const uint KeyAllAccess = 0x0002003F;
    public const uint KeyWow64_64 = 0x00000100;

    public const uint RegSz = 1;

    public const uint RrfRtRegSz = 0x00000002;

    public const int ErrorSuccess = 0;

    public delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int RegOpenKeyEx(
        IntPtr key,
        string subKey,
        uint options,
        uint desiredAccess,
        out IntPtr result);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int RegSetValueEx(
        IntPtr key,
        string? valueName,
        int reserved,
        uint type,
        [MarshalAs(UnmanagedType.LPWStr)] string? data,
        int dataLength);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int RegDeleteValue(IntPtr key, string? valueName);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int RegGetValue(
        IntPtr key,
        string? subKey,
        string? value,
        uint flags,
        out uint type,
        StringBuilder? data,
        ref int size);

    [DllImport("advapi32.dll")]
    public static extern int RegCloseKey(IntPtr key);

    // Window enumeration, used to notice that a slideshow went full screen.
    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr window, StringBuilder buffer, int maxLength);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
