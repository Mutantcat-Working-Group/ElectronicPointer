using Mutantcat.ElectronicPointer.Platform.Capture;
using Mutantcat.ElectronicPointer.Platform.Displays;
using System.Runtime.InteropServices;

namespace Mutantcat.ElectronicPointer.Platform.Windows;

/// <summary>
/// Screen grabbing through GDI. Every monitor is read with its own display DC, so a mixed
/// DPI setup still yields the exact pixels of that display, and the result is BGRA, which
/// is what <see cref="CapturedScreen"/> promises on every platform.
/// </summary>
public sealed class Win32ScreenCaptureService : IScreenCaptureService
{
    private const uint MonitorInfoPrimaryFlag = 1;

    private readonly List<DisplayInfo> _displays = new();

    public Win32ScreenCaptureService()
    {
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Enumerate, IntPtr.Zero);
    }

    public bool IsSupported => _displays.Count > 0;

    public IReadOnlyList<DisplayInfo> Displays => _displays;

    public CapturedScreen? Capture(DisplayInfo display)
    {
        var entry = _displays.FirstOrDefault(candidate =>
            candidate.Name == display.Name || candidate.Index == display.Index);

        // The private one, not this one. The public method used to end by calling itself
        // with the display it had just found, and the lookup then matched that same
        // display again on every pass: pressing "freeze screen" walked the stack down
        // until the runtime gave up, which is a failure no caller can catch, try, or log
        // its way out of. Reach for the grab by the name it was given.
        return entry is null ? null : CaptureDisplay(entry);
    }

    /// <summary>Grabs the desktop behind the overlay, or returns null when it refused.</summary>
    private CapturedScreen? CaptureDisplay(DisplayInfo entry)
    {
        var device = entry.Name;
        var width = entry.Width;
        var height = entry.Height;
        if (width <= 0 || height <= 0)
            return null;

        // A DC created for one display device has its origin at that display, so the
        // blit source stays (0, 0) and no virtual screen offset has to be computed.
        var screen = NativeMethods.CreateDC(null, device, null, IntPtr.Zero);
        if (screen == IntPtr.Zero)
            return null;

        try
        {
            var memory = NativeMethods.CreateCompatibleDC(screen);
            if (memory == IntPtr.Zero)
                return null;

            try
            {
                var descriptor = new NativeMethods.BitmapInfo
                {
                    BmiHeader = new NativeMethods.BitmapInfoHeader
                    {
                        BiSize = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                        BiWidth = width,

                        // A negative height asks for a top-down bitmap, which is the row
                        // order CapturedScreen promises on every platform.
                        BiHeight = -height,
                        BiPlanes = 1,
                        BiBitCount = 32,
                        BiCompression = NativeMethods.BiRgb,
                    },
                };

                var bitmap = NativeMethods.CreateDIBSection(
                    screen,
                    ref descriptor,
                    NativeMethods.DibRgbColors,
                    out var bits,
                    IntPtr.Zero,
                    0);

                if (bitmap == IntPtr.Zero || bits == IntPtr.Zero)
                    return null;

                try
                {
                    _ = NativeMethods.SelectObject(memory, bitmap);
                    if (!NativeMethods.BitBlt(memory, 0, 0, width, height, screen, 0, 0, NativeMethods.Srccopy))
                        return null;

                    var pixels = new byte[width * height * 4];
                    Marshal.Copy(bits, pixels, 0, pixels.Length);
                    return new CapturedScreen(pixels, width, height, entry.ScaleFactor, device);
                }
                finally
                {
                    NativeMethods.DeleteObject(bitmap);
                }
            }
            finally
            {
                NativeMethods.DeleteDC(memory);
            }
        }
        finally
        {
            NativeMethods.DeleteDC(screen);
        }
    }

    private bool Enumerate(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data)
    {
        var info = new NativeMethods.MonitorInfoEx
        {
            CbSize = Marshal.SizeOf<NativeMethods.MonitorInfoEx>(),
        };

        info.Device = new char[32];
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
            return true;

        var name = new string(info.Device).TrimEnd('\0');
        var width = info.Monitor.Right - info.Monitor.Left;
        var height = info.Monitor.Bottom - info.Monitor.Top;

        _displays.Add(new DisplayInfo(
            _displays.Count,
            name,
            width,
            height,
            ReadScale(name),
            (info.Flags & MonitorInfoPrimaryFlag) != 0));

        return true;
    }

    /// <summary>
    /// Reads the dots per inch of one display. GetDeviceCaps wants a DC rather than an
    /// HMONITOR, so a short lived display DC is created for the reading.
    /// </summary>
    private static double ReadScale(string device)
    {
        var dc = NativeMethods.CreateDC(null, device, null, IntPtr.Zero);
        if (dc == IntPtr.Zero)
            return 1;

        try
        {
            var dpi = NativeMethods.GetDeviceCaps(dc, NativeMethods.LogPixelsX);
            return dpi > 0 ? dpi / 96.0 : 1;
        }
        finally
        {
            NativeMethods.DeleteDC(dc);
        }
    }
}
