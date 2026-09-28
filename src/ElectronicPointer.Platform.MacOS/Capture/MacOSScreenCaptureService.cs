using System.Runtime.InteropServices;
using Mutantcat.ElectronicPointer.Platform.Capture;
using Mutantcat.ElectronicPointer.Platform.Displays;
using Mutantcat.ElectronicPointer.Platform.MacOS.Native;

namespace Mutantcat.ElectronicPointer.Platform.MacOS;

/// <summary>
/// Screen grab on macOS through CoreGraphics: enumerate the active displays, then pull the
/// pixels for one of them with <c>CGDisplayCreateImageForRect</c>.
///
/// The result already arrives as 32-bit little-endian BGRA, which is exactly the layout the
/// board wants, so no colour conversion happens here. Screen Recording permission is the one
/// thing that can go wrong: without it CoreGraphics hands back an all-black image instead of
/// an error, so <see cref="RequiresPermission"/> reports the state and the UI prompts for it.
/// </summary>
public sealed class MacOSScreenCaptureService : IScreenCaptureService
{
    private readonly List<DisplayInfo> _displays = new();
    private readonly bool _probeDone;
    private bool _isSupported;

    public MacOSScreenCaptureService()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        try
        {
            _isSupported = TryLoadDisplays();
        }
        catch (DllNotFoundException)
        {
            _isSupported = false;
        }
        catch (EntryPointNotFoundException)
        {
            _isSupported = false;
        }

        _probeDone = true;
    }

    public bool IsSupported => _isSupported;

    /// <summary>
    /// True when macOS 10.15 or newer, where the Screen Recording permission gate applies.
    /// A rejected permission still yields images, just black ones, so the UI must ask.
    /// </summary>
    public bool RequiresPermission =>
        _probeDone && _isSupported && OperatingSystem.IsMacOSVersionAtLeast(10, 15);

    public IReadOnlyList<DisplayInfo> Displays => _displays;

    public CapturedScreen? Capture(DisplayInfo display)
    {
        if (!_isSupported || display.Index < 0 || display.Index >= _displays.Count)
            return null;

        var id = _displays[display.Index].Name;
        if (!uint.TryParse(id, out var displayId))
            return null;

        nint image = 0;
        nint data = 0;
        try
        {
            var bounds = Bounds(display);
            image = MacOSNativeMethods.CGDisplayCreateImageForRect(displayId, bounds);
            if (image == 0)
                return null;

            var width = (int)MacOSNativeMethods.CGImageGetWidth(image);
            var height = (int)MacOSNativeMethods.CGImageGetHeight(image);
            if (width <= 0 || height <= 0)
                return null;

            var sourceStride = (int)MacOSNativeMethods.CGImageGetBytesPerRow(image);
            var provider = MacOSNativeMethods.CGImageGetDataProvider(image);
            if (provider == 0)
                return null;

            data = MacOSNativeMethods.CGDataProviderCopyData(provider);
            if (data == 0)
                return null;

            var usable = LayoutIsBgra8888(image, sourceStride, width);
            var length = (long)sourceStride * height;
            if (MacOSNativeMethods.CFDataGetLength(data) < length)
                return null;

            var pixels = new byte[(long)width * height * 4];
            var source = MacOSNativeMethods.CFDataGetBytePtr(data);
            if (source == 0)
                return null;

            for (var y = 0; y < height; y++)
                Marshal.Copy(source + (y * sourceStride), pixels, y * width * 4, width * 4);

            if (!usable)
                ForceOpaque(pixels);

            return new CapturedScreen(pixels, width, height, display.ScaleFactor, display.Name);
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
            if (data != 0)
                MacOSNativeMethods.CFRelease(data);

            if (image != 0)
                MacOSNativeMethods.CFRelease(image);
        }
    }

    private static MacOSNativeMethods.CGRect Bounds(DisplayInfo display)
    {
        var info = MacOSNativeMethods.CGDisplayBounds(DisplayId(display.Name));
        return new MacOSNativeMethods.CGRect
        {
            X = 0,
            Y = 0,
            Width = info.Width,
            Height = info.Height,
        };
    }

    private static uint DisplayId(string name) => uint.TryParse(name, out var id) ? id : 0;

    /// <summary>
    /// True when CoreGraphics already hands us BGRA in the order the board stores, so the
    /// rows can be copied straight across. Anything else falls back to forcing alpha, which
     /// keeps the picture usable even if the channel order surprises us.
    /// </summary>
    private static bool LayoutIsBgra8888(nint image, int sourceStride, int width)
    {
        if (MacOSNativeMethods.CGImageGetBitsPerComponent(image) != 8)
            return false;

        if (sourceStride < width * 4)
            return false;

        const int ByteOrder32Little = 2 << 12;
        var info = MacOSNativeMethods.CGImageGetBitmapInfo(image);
        var byteOrder = (int)(info & 0xF000);
        return byteOrder == ByteOrder32Little;
    }

    private static void ForceOpaque(byte[] pixels)
    {
        for (var i = 3; i < pixels.Length; i += 4)
            pixels[i] = 0xFF;
    }

    private bool TryLoadDisplays()
    {
        var ids = new uint[32];
        var result = MacOSNativeMethods.CGGetActiveDisplayList(ids.Length, ids, out var count);
        if (result != 0 || count <= 0)
            return false;

        var main = MacOSNativeMethods.CGMainDisplayID();
        for (var i = 0; i < count; i++)
        {
            var id = ids[i];
            var bounds = MacOSNativeMethods.CGDisplayBounds(id);
            var pixelsWide = MacOSNativeMethods.CGDisplayPixelsWide(id);
            var pixelsHigh = MacOSNativeMethods.CGDisplayPixelsHigh(id);
            var scale = bounds.Width > 0 ? pixelsWide / bounds.Width : 1d;
            var width = pixelsWide > 0 ? (int)pixelsWide : (int)bounds.Width;
            var height = pixelsHigh > 0 ? (int)pixelsHigh : (int)bounds.Height;

            _displays.Add(new DisplayInfo(i, id.ToString(), width, height, scale, id == main));
        }

        return true;
    }
}
