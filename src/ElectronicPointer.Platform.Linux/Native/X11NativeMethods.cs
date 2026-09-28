using System.Runtime.InteropServices;

namespace Mutantcat.ElectronicPointer.Platform.Linux.Native;

/// <summary>
/// The slice of Xlib and the shape extension the overlay needs. Declared with plain
/// <see cref="DllImportAttribute"/> rather than the source generated LibraryImport on purpose:
/// the hotkey loop has to hand a 192 byte XEvent union across as a ref struct, which the
/// generator does not marshal, and the call rate here is a keystroke at most. None of these
/// are hot paths.
///
/// Everything is called only after <see cref="OperatingSystem.IsLinux"/> has been confirmed,
/// and a missing optional library (libXext, libXinerama) raises only when the function is
/// actually invoked, so each call site wraps it and reports "not supported" instead.
/// </summary>
internal static class X11NativeMethods
{
    internal const string LibX11 = "libX11.so.6";

    internal const string LibXext = "libXext.so.6";

    internal const string LibXinerama = "libXinerama.so.1";

    internal const string LibC = "libc";

    // ------------------------------------------------------------------------ atoms

    [DllImport(LibX11, CharSet = CharSet.Ansi)]
    internal static extern nint XInternAtom(nint display, [MarshalAs(UnmanagedType.LPStr)] string name, bool onlyIfExists);

    // ---------------------------------------------------------------------- events

    [DllImport(LibX11)]
    internal static extern int XPending(nint display);

    [DllImport(LibX11)]
    internal static extern void XNextEvent(nint display, ref XEvent eventBuffer);

    /// <summary>Sends a client message, used for the _NET_WM_STATE hints.</summary>
    [DllImport(LibX11)]
    internal static extern int XSendEvent(nint display, nint window, bool propagate, nint eventMask, ref XClientMessageEvent sendEvent);

    // --------------------------------------------------------------------- windows

    [DllImport(LibX11)]
    internal static extern int XMoveWindow(nint display, nint window, int x, int y);

    [DllImport(LibX11)]
    internal static extern int XResizeWindow(nint display, nint window, uint width, uint height);

    [DllImport(LibX11)]
    internal static extern int XSync(nint display, bool discard);

    [DllImport(LibX11)]
    internal static extern int XSelectInput(nint display, nint window, nint eventMask);

    [DllImport(LibX11, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool XQueryExtension(
        nint display,
        [MarshalAs(UnmanagedType.LPStr)] string extensionName,
        out int majorOpcode,
        out int firstEvent,
        out int firstError);

    [DllImport(LibX11)]
    internal static extern int XGetGeometry(
        nint display,
        nint drawable,
        out nint root,
        out int x,
        out int y,
        out uint width,
        out uint height,
        out uint borderWidth,
        out uint depth);

    [DllImport(LibX11)]
    internal static extern nint XDefaultRootWindow(nint display);

    [DllImport(LibX11)]
    internal static extern int XDisplayWidth(nint display, int screenNumber);

    [DllImport(LibX11)]
    internal static extern int XDisplayHeight(nint display, int screenNumber);

    [DllImport(LibX11)]
    internal static extern int XDisplayWidthMM(nint display, int screenNumber);

    [DllImport(LibX11)]
    internal static extern int XDefaultScreen(nint display);

    [DllImport(LibX11)]
    internal static extern int XConnectionNumber(nint display);

    [DllImport(LibX11, CharSet = CharSet.Ansi)]
    internal static extern nint XOpenDisplay([MarshalAs(UnmanagedType.LPStr)] string? displayName);

    [DllImport(LibX11)]
    internal static extern int XCloseDisplay(nint display);

    // ----------------------------------------------------------------------- grabs

    [DllImport(LibX11, CharSet = CharSet.Ansi)]
    internal static extern int XGrabKey(nint display, int keycode, uint modifiers, nint grabWindow, bool ownerEvents, int pointerMode, int keyboardMode);

    [DllImport(LibX11)]
    internal static extern int XUngrabKey(nint display, int keycode, uint modifiers, nint grabWindow);

    [DllImport(LibX11)]
    internal static extern int XUngrabKeyboard(nint display, nint window);

    /// <summary>Must be called before any other thread touches the connection.</summary>
    [DllImport(LibX11)]
    internal static extern int XInitThreads();

    // ---------------------------------------------------------------------- capture

    /// <summary>AllPlanes and ZPixmap are passed positionally below.</summary>
    internal const ulong AllPlanes = ~0UL;

    internal const int ZPixmap = 2;

    [DllImport(LibX11)]
    internal static extern nint XGetImage(nint display, nint drawable, int x, int y, uint width, uint height, ulong planeMask, int format);

    [DllImport(LibX11)]
    internal static extern int XDestroyImage(nint image);

    // ------------------------------------------------------- X shape (click-through)

    internal const int ShapeInput = 2;

    internal const int ShapeSet = 0;

    internal const int ShapeUnsorted = 0;

    /// <summary>
    /// Sets the input region of a window. Passing an empty rectangle list replaces the input
    /// region with nothing at all, which is what makes the whole overlay click-through.
    /// </summary>
    [DllImport(LibXext)]
    internal static extern int XShapeCombineRectangles(
        nint display,
        nint destination,
        int destinationKind,
        int xOffset,
        int yOffset,
        XRectangle[]? rectangles,
        int rectangleCount,
        int operation,
        int ordering);

    /// <summary>XRectangle, 8 bytes, the unit the shape extension works in.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct XRectangle
    {
        public short X;

        public short Y;

        public ushort Width;

        public ushort Height;
    }

    // -------------------------------------------------------------------- Xinerama

    [DllImport(LibXinerama)]
    internal static extern nint XineramaQueryScreens(nint display, out int screenCount);

    [DllImport(LibX11)]
    internal static extern int XFree(nint data);

    // ----------------------------------------------------------------- X event mask

    internal const nint KeyPressMask = 1 << 0;

    internal const nint SubstructureNotifyMask = 1 << 19;

    internal const nint SubstructureRedirectMask = 1 << 20;

    internal const uint GrabModeAsync = 1;

    internal const uint ShiftMask = 1u << 0;

    internal const uint LockMask = 1u << 1;

    internal const uint ControlMask = 1u << 2;

    internal const uint Mod1Mask = 1u << 3;

    internal const uint Mod2Mask = 1u << 4;

    internal const uint Mod4Mask = 1u << 6;

    /// <summary>
    /// The XEvent union is 192 bytes on every 64 bit ABI. Only the fields the hotkey loop
    /// reads are declared; the rest of the union is just padding past the last field.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 192)]
    internal struct XEvent
    {
        public int Type;

        private ulong _serial;

        private int _sendEvent;

        private nint _display;

        public nint Window;

        public uint State;

        public uint KeyCode;
    }

    /// <summary>XClientMessageEvent, 96 bytes, laid out by hand to match the C union.</summary>
    [StructLayout(LayoutKind.Sequential, Size = 96)]
    internal struct XClientMessageEvent
    {
        public int Type;

        private ulong _serial;

        private int _sendEvent;

        private nint _display;

        public nint Window;

        public nint MessageType;

        public int Format;

        public nint Data0;

        public nint Data1;

        public nint Data2;

        public nint Data3;

        public nint Data4;
    }

    /// <summary>XImage, the 88 leading bytes of the C struct. Only the header is read.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct XImageHeader
    {
        public int Width;

        public int Height;

        private int _xOffset;

        private int _format;

        public nint Data;

        public int ByteOrder;

        private int _bitmapUnit;

        private int _bitmapBitOrder;

        private int _bitmapPad;

        private int _depth;

        public int BytesPerLine;

        public int BitsPerPixel;

        public ulong RedMask;

        public ulong GreenMask;

        public ulong BlueMask;
    }

    /// <summary>XineramaScreenInfo, 12 bytes, one rectangle per physical monitor.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct XineramaScreenInfo
    {
        public int ScreenNumber;

        public short XOrg;

        public short YOrg;

        public short Width;

        public short Height;
    }

    // ------------------------------------------------------------------ poll / pipe

    [StructLayout(LayoutKind.Sequential)]
    internal struct PollFd
    {
        public int Fd;

        public short Events;

        public short Revents;
    }

    internal const short PollIn = 0x001;

    [DllImport(LibC, SetLastError = true)]
    internal static extern unsafe int poll(PollFd* descriptors, uint count, int timeoutMilliseconds);

    [DllImport(LibC, SetLastError = true)]
    internal static extern int pipe(int[] descriptors);

    [DllImport(LibC, SetLastError = true)]
    internal static extern int read(int fd, byte[] buffer, int count);

    [DllImport(LibC, SetLastError = true)]
    internal static extern int write(int fd, byte[] buffer, int count);

    [DllImport(LibC, SetLastError = true)]
    internal static extern int close(int fd);
}
