using System.Runtime.InteropServices;
using Mutantcat.ElectronicPointer.Platform.Capture;
using Mutantcat.ElectronicPointer.Platform.Displays;
using Mutantcat.ElectronicPointer.Platform.Linux.Native;

namespace Mutantcat.ElectronicPointer.Platform.Linux;

/// <summary>
/// Grabs the pixels behind the overlay on X11 with <c>XGetImage</c>. The image the server
/// returns is in whatever pixel layout the display was started with, so rows are read through
/// the mask triple the XImage reports rather than assumed to be BGRA: a 16 depth panel, a
/// 5-6-5 display and a big endian server all produce different bytes for the same colour, and
/// guessing would freeze a wrong-coloured screen.
///
/// Monitors come from Xinerama when the compositor exposes it, because a X screen is one
/// rectangular area that several physical monitors are wired into; without it only the single
/// screen rectangle is reported.
///
/// Native Wayland is deliberately not covered: there is no portable server side read left,
/// and the documented replacement is the org.freedesktop.portal.Desktop screenshot portal,
/// which needs a DBus client and puts a confirmation dialog in front of the user every time.
/// The app reports freeze-screen as unavailable there instead of half-working.
/// </summary>
public sealed class X11ScreenCaptureService : IScreenCaptureService
{
    private readonly List<DisplayInfo> _displays = new();

    private bool _queried;

    public bool IsSupported => LinuxSession.IsX11;

    public IReadOnlyList<DisplayInfo> Displays
    {
        get
        {
            if (!_queried)
            {
                _queried = true;
                _displays.AddRange(QueryDisplays());
            }

            return _displays;
        }
    }

    public CapturedScreen? Capture(DisplayInfo display)
    {
        if (!LinuxSession.IsX11)
            return null;

        if (!X11Runtime.EnsureThreadsInitialized())
            return null;

        // Its own connection, opened and closed per grab: the capture is a rare, one shot
        // operation, so a short lived connection costs nothing and never competes with the
        // overlay or the hotkey loop for the same one.
        var connection = X11NativeMethods.XOpenDisplay(LinuxSession.DisplayName);
        if (connection == 0)
            return null;

        try
        {
            return Grab(connection, display);
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            _ = X11NativeMethods.XCloseDisplay(connection);
        }
    }

    private static CapturedScreen? Grab(nint connection, DisplayInfo display)
    {
        var root = X11NativeMethods.XDefaultRootWindow(connection);
        if (root == 0)
            return null;

        // A grab of the root window returns the whole visible desktop, other windows included,
        // which is what a freeze is meant to keep.
        var image = X11NativeMethods.XGetImage(
            connection,
            root,
            0,
            0,
            (uint)display.Width,
            (uint)display.Height,
            X11NativeMethods.AllPlanes,
            X11NativeMethods.ZPixmap);

        if (image == 0)
            return null;

        try
        {
            var header = Marshal.PtrToStructure<X11NativeMethods.XImageHeader>(image);
            if (header.Data == 0 || header.Width <= 0 || header.Height <= 0 || header.BytesPerLine <= 0)
                return null;

            return new CapturedScreen(
                ConvertRows(header),
                header.Width,
                header.Height,
                display.ScaleFactor,
                display.Name);
        }
        finally
        {
            _ = X11NativeMethods.XDestroyImage(image);
        }
    }

    /// <summary>Rewrites the server bitmap into the BGRA layout the board expects.</summary>
    private static byte[] ConvertRows(X11NativeMethods.XImageHeader header)
    {
        var pixels = new byte[header.Width * header.Height * 4];
        var bytesPerPixel = Math.Max(1, (header.BitsPerPixel + 7) / 8);

        unsafe
        {
            var source = (byte*)header.Data;

            for (var y = 0; y < header.Height; y++)
            {
                var row = source + (y * header.BytesPerLine);

                for (var x = 0; x < header.Width; x++)
                {
                    var pixel = row + (x * bytesPerPixel);
                    var value = ReadPixel(pixel, bytesPerPixel, header.ByteOrder);

                    var target = ((y * header.Width) + x) * 4;
                    pixels[target + 0] = ReadChannel(value, header.BlueMask);
                    pixels[target + 1] = ReadChannel(value, header.GreenMask);
                    pixels[target + 2] = ReadChannel(value, header.RedMask);
                    pixels[target + 3] = 0xFF;
                }
            }
        }

        return pixels;
    }

    /// <summary>
    /// Reads one pixel honouring the byte order the server reports. Depth 16 and depth 24 with
    /// three byte pixels are the two cases where reading a whole machine word would run off
    /// the end of the buffer.
    /// </summary>
    private static unsafe int ReadPixel(byte* pixel, int bytesPerPixel, int byteOrder)
    {
        var value = 0;

        for (var i = 0; i < bytesPerPixel; i++)
        {
            // LSBFirst is 0 and MSBFirst is 1; on a big endian server the bytes reverse.
            var index = byteOrder == 1 ? bytesPerPixel - 1 - i : i;
            value |= pixel[index] << (8 * i);
        }

        return value;
    }

    /// <summary>
    /// Pulls one colour channel out of a pixel and widens it to a full byte, so a six bit
    /// channel still spans 0..255 instead of capping at 63.
    /// </summary>
    private static byte ReadChannel(int pixel, ulong mask)
    {
        if (mask == 0)
            return 0;

        var shift = 0;
        var working = mask;
        while ((working & 1UL) == 0)
        {
            working >>= 1;
            shift++;
        }

        var bits = 0;
        while ((working & 1UL) == 1)
        {
            working >>= 1;
            bits++;
        }

        if (bits <= 0 || bits > 32)
            return 0;

        var value = (pixel & unchecked((int)mask)) >> shift;

        if (bits >= 8)
            return (byte)(value >> (bits - 8));

        var maximum = (1 << bits) - 1;
        return (byte)((value * 0xFF) / maximum);
    }

    private static List<DisplayInfo> QueryDisplays()
    {
        var screens = new List<DisplayInfo>();

        if (!LinuxSession.IsX11)
            return screens;

        if (!X11Runtime.EnsureThreadsInitialized())
            return screens;

        var connection = X11NativeMethods.XOpenDisplay(LinuxSession.DisplayName);
        if (connection == 0)
            return screens;

        try
        {
            var screenNumber = X11NativeMethods.XDefaultScreen(connection);
            var scale = ReadScaleFactor(connection, screenNumber);

            if (TryQueryXinerama(connection, scale) is { Count: > 0 } monitors)
                return monitors;

            var width = X11NativeMethods.XDisplayWidth(connection, screenNumber);
            var height = X11NativeMethods.XDisplayHeight(connection, screenNumber);

            if (width > 0 && height > 0)
            {
                screens.Add(new DisplayInfo(
                    0,
                    LinuxSession.DisplayName ?? "X11",
                    0,
                    0,
                    width,
                    height,
                    scale,
                    true));
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
        finally
        {
            _ = X11NativeMethods.XCloseDisplay(connection);
        }

        return screens;
    }

    /// <summary>
    /// Reads the Xinerama monitor list, or null when the compositor does not expose it.
    /// Monitors are returned in left to right order, which is the order they are labelled in.
    /// </summary>
    private static List<DisplayInfo>? TryQueryXinerama(nint connection, double scale)
    {
        var list = X11NativeMethods.XineramaQueryScreens(connection, out var count);
        if (list == 0 || count <= 0)
            return null;

        try
        {
            var stride = Marshal.SizeOf<X11NativeMethods.XineramaScreenInfo>();
            var found = new List<DisplayInfo>();

            for (var i = 0; i < count; i++)
            {
                var monitor = Marshal.PtrToStructure<X11NativeMethods.XineramaScreenInfo>(list + (i * stride));
                if (monitor.Width <= 0 || monitor.Height <= 0)
                    continue;

                found.Add(new DisplayInfo(
                    found.Count,
                    $"显示器 {found.Count + 1}",
                    monitor.XOrg,
                    monitor.YOrg,
                    monitor.Width,
                    monitor.Height,
                    scale,
                    monitor.ScreenNumber == 0));
            }

            if (found.Count == 0)
                return null;

            // The physical position on the desk decides the order, not the wiring number,
            // so a monitor plugged into the second port but sitting on the left is not
            // mislabelled as "monitor 2".
            return found
                .OrderBy(monitor => monitor.X)
                .ThenBy(monitor => monitor.Y)
                .Select((monitor, index) => new DisplayInfo(
                    index,
                    monitor.Name,
                    monitor.X,
                    monitor.Y,
                    monitor.Width,
                    monitor.Height,
                    monitor.ScaleFactor,
                    index == 0))
                .ToList();
        }
        finally
        {
            _ = X11NativeMethods.XFree(list);
        }
    }

    /// <summary>
    /// Reads the DPI the server was started with and turns it into a device pixel ratio. The
    /// size in millimetres is advisory, so anything that cannot produce a sane number falls
    /// back to 1 rather than a double resolution canvas.
    /// </summary>
    private static double ReadScaleFactor(nint connection, int screenNumber)
    {
        var widthMm = X11NativeMethods.XDisplayWidthMM(connection, screenNumber);
        var widthPx = X11NativeMethods.XDisplayWidth(connection, screenNumber);

        if (widthMm <= 0 || widthPx <= 0)
            return 1;

        var dpi = (widthPx * 25.4) / widthMm;
        if (dpi < 48 || dpi > 480)
            return 1;

        return Math.Clamp(dpi / 96.0, 1.0, 4.0);
    }
}
