using System.Runtime.InteropServices;

namespace Mutantcat.ElectronicPointer.Platform.MacOS.Native;

/// <summary>
/// The slice of CoreGraphics, CoreFoundation and the Objective-C runtime that the overlay
/// needs. Only stable, documented entry points are declared: nothing here reaches for a
/// private symbol, so the bindings keep working across macOS releases.
/// </summary>
internal static partial class MacOSNativeMethods
{
    internal const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    internal const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    internal const string ObjectiveCRuntime = "/usr/lib/libobjc.dylib";

    // CGEventTapLocation
    internal const uint HidEventTap = 0;

    // CGEventTapPlacement
    internal const uint HeadInsertEventTap = 0;

    // CGEventTapOptions
    internal const uint EventTapOptionDefault = 0;

    // CGEventType
    internal const uint EventTypeKeyDown = 10;

    // CGKeyboardEventField
    internal const int KeyboardEventKeycode = 9;

    // CGEventFlags
    internal const ulong EventFlagMaskControl = 1u << 16;

    internal const ulong EventFlagMaskShift = 1u << 17;

    internal const ulong EventFlagMaskAlternate = 1u << 19;

    internal const ulong EventFlagMaskCommand = 1u << 20;

    internal const ulong EventFlagMaskSecondaryFn = 1u << 23;

    // CGEventMask: every keyboard event, key down and key up.
    internal const ulong KeyboardEventMask =
        (1u << 10) | (1u << 11) | (1u << 12);

    // NSWindow levels
    internal const nint WindowLevelFloating = 3;

    /// <summary>NSStatusWindowLevel: above every overlay canvas, below the screen saver.</summary>
    internal const nint WindowLevelStatus = 25;

    // NSWindow collection behaviour bits, ORed together in a single call.
    internal const nint CollectionBehaviorCanJoinAllSpaces = 1 << 0;

    internal const nint CollectionBehaviorStationary = 1 << 4;

    internal const nint CollectionBehaviorIgnoresCycle = 1 << 6;

    [StructLayout(LayoutKind.Sequential)]
    internal struct CGRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    /// <summary>Event tap callback: proxy, event type, the event itself, user info.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal unsafe delegate nint CGEventTapCallBack(nint proxy, uint type, nint @event, nint userInfo);

    // ------------------------------------------------------------------ displays

    [LibraryImport(CoreGraphics)]
    internal static partial int CGGetActiveDisplayList(int maxDisplays, [Out] uint[]? activeDisplays, out int displayCount);

    [LibraryImport(CoreGraphics)]
    internal static partial uint CGMainDisplayID();

    /// <summary>Display rectangle in the global desktop coordinate space.</summary>
    [LibraryImport(CoreGraphics, EntryPoint = "CGDisplayBounds")]
    internal static partial CGRect CGDisplayBounds(uint display);

    [LibraryImport(CoreGraphics)]
    internal static partial nint CGDisplayPixelsWide(uint display);

    [LibraryImport(CoreGraphics)]
    internal static partial nint CGDisplayPixelsHigh(uint display);

    // ------------------------------------------------------------------- capture

    [LibraryImport(CoreGraphics)]
    internal static partial nint CGDisplayCreateImageForRect(uint display, CGRect rect);

    [LibraryImport(CoreGraphics)]
    internal static partial nint CGImageGetWidth(nint image);

    [LibraryImport(CoreGraphics)]
    internal static partial nint CGImageGetHeight(nint image);

    [LibraryImport(CoreGraphics)]
    internal static partial nint CGImageGetBitsPerComponent(nint image);

    [LibraryImport(CoreGraphics)]
    internal static partial nint CGImageGetBytesPerRow(nint image);

    [LibraryImport(CoreGraphics)]
    internal static partial nint CGImageGetBitmapInfo(nint image);

    /// <summary>The image owns its provider, so the provider must not be released separately.</summary>
    [LibraryImport(CoreGraphics)]
    internal static partial nint CGImageGetDataProvider(nint image);

    // ------------------------------------------------------------- CoreFoundation

    [LibraryImport(CoreFoundation)]
    internal static partial nint CGDataProviderCopyData(nint provider);

    /// <summary>Pointer to the first byte of the data; still owned by the CFData object.</summary>
    [LibraryImport(CoreFoundation)]
    internal static partial nint CFDataGetBytePtr(nint data);

    [LibraryImport(CoreFoundation)]
    internal static partial nint CFDataGetLength(nint data);

    [LibraryImport(CoreFoundation)]
    internal static partial void CFRelease(nint handle);

    [LibraryImport(CoreFoundation)]
    internal static partial nint CFMachPortCreateRunLoopSource(nint allocator, nint port, nint order);

    [LibraryImport(CoreFoundation)]
    internal static partial void CFRunLoopAddSource(nint runLoop, nint source, nint mode);

    [LibraryImport(CoreFoundation)]
    internal static partial nint CFRunLoopGetCurrent();

    [LibraryImport(CoreFoundation)]
    internal static partial void CFRunLoopRun();

    [LibraryImport(CoreFoundation)]
    internal static partial void CFRunLoopStop(nint runLoop);

    // --------------------------------------------------------- event tap / hotkey

    [LibraryImport(CoreGraphics)]
    internal static partial nint CGEventTapCreate(
    // tap
        uint tap,
        uint place,
        uint options,
        ulong eventsOfInterest,
        nint callBack,
        nint userInfo);

    [LibraryImport(CoreGraphics)]
    internal static partial void CGEventTapEnable(nint tap, [MarshalAs(UnmanagedType.Bool)] bool enable);

    [LibraryImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CGEventTapIsEnabled(nint tap);

    [LibraryImport(CoreGraphics)]
    internal static partial ulong CGEventGetFlags(nint @event);

    [LibraryImport(CoreGraphics)]
    internal static partial nint CGEventGetIntegerValueField(nint @event, int field);

    // --------------------------------------------------------------- Objective-C

    [LibraryImport(ObjectiveCRuntime, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint objc_getClass(string name);

    [LibraryImport(ObjectiveCRuntime, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint sel_registerName(string name);

    [LibraryImport(ObjectiveCRuntime, EntryPoint = "objc_msgSend")]
    internal static partial nint ObjcSendNoArgs(nint receiver, nint selector);

    [LibraryImport(ObjectiveCRuntime, EntryPoint = "objc_msgSend")]
    internal static partial void ObjcSendBool(nint receiver, nint selector, [MarshalAs(UnmanagedType.Bool)] bool value);

    [LibraryImport(ObjectiveCRuntime, EntryPoint = "objc_msgSend")]
    internal static partial void ObjcSendLong(nint receiver, nint selector, nint value);

    [LibraryImport(ObjectiveCRuntime, EntryPoint = "objc_msgSend")]
    internal static partial void ObjcSendUlong(nint receiver, nint selector, nuint value);

    [LibraryImport(ObjectiveCRuntime, EntryPoint = "objc_msgSend")]
    internal static partial void ObjcSendRect(nint receiver, nint selector, CGRect value);

    [LibraryImport(ObjectiveCRuntime, EntryPoint = "objc_msgSend")]
    internal static partial void ObjcSendFourLongs(nint receiver, nint selector, nint a, nint b, nint c, nint d);

    [LibraryImport(ObjectiveCRuntime, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ObjcRespondsToSelector(nint receiver, nint selector);
}
