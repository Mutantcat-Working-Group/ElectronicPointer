using Mutantcat.ElectronicPointer.Platform.MacOS.Native;
using Mutantcat.ElectronicPointer.Platform.Overlay;

namespace Mutantcat.ElectronicPointer.Platform.MacOS;

/// <summary>
/// Turns an Avalonia window into a screen-annotation overlay through the AppKit window it
/// owns. Avalonia hands over the <c>NSWindow</c> pointer in its platform handle, and the
/// three behaviours an overlay needs are set on that window directly:
///
/// - click-through is <c>setIgnoresMouseEvents:</c>;
/// - staying on top is <c>setLevel:</c> at the floating level, which covers a full screen
///   presentation without dropping behind the Dock;
/// - staying out of the Dock and the window switcher is <c>setCollectionBehavior:</c> with
///   the "ignore cycle" bit, plus <c>orderFrontRegardless</c> so the overlay paints even
///   while another app is frontmost.
///
/// AppKit is main-thread only, so callers must be on the UI thread. Every member reports
/// whether it applied, which is how the UI learns the window refused a change.
/// </summary>
public sealed class MacOSOverlayChrome : IOverlayChrome
{
    /// <summary>NSWindowStyleMaskNonactivatingPanel, so the overlay never steals focus.</summary>
    private const nint NonactivatingPanelMask = 1 << 7;

    private nint _window;

    public bool IsSupported => OperatingSystem.IsMacOS();

    public bool Attach(IOverlayWindowTarget target)
    {
        if (target is not { Handle: not 0 })
            return false;

        _window = target.Handle;
        IgnoreMouseEvents(false);
        MacOSNativeMethods.ObjcSendLong(_window, Sel("setLevel:"), MacOSNativeMethods.WindowLevelFloating);

        var behaviour = MacOSNativeMethods.CollectionBehaviorCanJoinAllSpaces
            | MacOSNativeMethods.CollectionBehaviorStationary
            | MacOSNativeMethods.CollectionBehaviorIgnoresCycle;

        MacOSNativeMethods.ObjcSendUlong(_window, Sel("setCollectionBehavior:"), (nuint)behaviour);
        MacOSNativeMethods.ObjcSendUlong(_window, Sel("setStyleMask:"), (nuint)NonactivatingPanelMask);
        MacOSNativeMethods.ObjcSendBool(_window, Sel("setMovableByWindowBackground:"), false);
        OrderFrontRegardless();
        return true;
    }

    public void Detach()
    {
        if (_window == 0)
            return;

        IgnoreMouseEvents(false);
        MacOSNativeMethods.ObjcSendLong(_window, Sel("setLevel:"), 0);
        MacOSNativeMethods.ObjcSendUlong(_window, Sel("setCollectionBehavior:"), 0);
        _window = 0;
    }

    public bool SetClickThrough(bool enabled)
    {
        if (_window == 0)
            return false;

        IgnoreMouseEvents(enabled);
        return true;
    }

    public bool SetAlwaysOnTop(bool enabled)
    {
        if (_window == 0)
            return false;

        MacOSNativeMethods.ObjcSendLong(
            _window,
            Sel("setLevel:"),
            enabled ? MacOSNativeMethods.WindowLevelFloating : 0);

        OrderFrontRegardless();
        return true;
    }

    public bool SetHiddenFromSwitcher(bool hidden)
    {
        if (_window == 0)
            return false;

        var behaviour = MacOSNativeMethods.CollectionBehaviorCanJoinAllSpaces
            | MacOSNativeMethods.CollectionBehaviorStationary;

        if (hidden)
            behaviour |= MacOSNativeMethods.CollectionBehaviorIgnoresCycle;

        MacOSNativeMethods.ObjcSendUlong(_window, Sel("setCollectionBehavior:"), (nuint)behaviour);
        return true;
    }

    private void IgnoreMouseEvents(bool ignore)
        => MacOSNativeMethods.ObjcSendBool(_window, Sel("setIgnoresMouseEvents:"), ignore);

    private void OrderFrontRegardless()
    {
        var selector = Sel("orderFrontRegardless");
        if (MacOSNativeMethods.ObjcRespondsToSelector(_window, selector))
            _ = MacOSNativeMethods.ObjcSendNoArgs(_window, selector);
    }

    private static nint Sel(string name) => MacOSNativeMethods.sel_registerName(name);
}
