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
/// Windows that travel with the canvases are companions rather than canvases: the tool
/// palette and the settings dialog are put at the status window level, one step above the
/// floating canvases, because AppKit stacks strictly by level and a companion left at the
/// ordinary level would sit under ink that cannot be clicked.
///
/// AppKit is main-thread only, so callers must be on the UI thread. Every member reports
/// whether it applied, which is how the UI learns the window refused a change.
/// </summary>
public sealed class MacOSOverlayChrome : IOverlayChrome
{
    /// <summary>NSWindowStyleMaskNonactivatingPanel, so the overlay never steals focus.</summary>
    private const nint NonactivatingPanelMask = 1 << 7;

    private nint _window;

    private nint _companion;

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

    /// <summary>
    /// A companion, lifted one window level above every canvas. AppKit stacks by level
    /// first and only then by order, so a companion left at the ordinary level stays
    /// underneath the floating canvases no matter how often it is ordered to the front,
    /// and the buttons that offer a way out of the app cannot be clicked at all. The
    /// settings dialog needs the same lift for the same reason: it opens with ink over
    /// every display, and an ordinary-level window would be painted under all of it.
    ///
    /// Only the palette is turned into a non-activating panel. A dialog is left
    /// activatable on purpose: it has sentences to read and controls to reach with the
    /// keyboard, and a window that cannot become the key window gives up both.
    /// </summary>
    public bool AttachCompanion(IOverlayWindowTarget target, CompanionRole role)
    {
        if (!IsSupported || target is not { Handle: not 0 })
            return false;

        _companion = target.Handle;

        MacOSNativeMethods.ObjcSendLong(
            _companion,
            Sel("setLevel:"),
            MacOSNativeMethods.WindowLevelStatus);

        MacOSNativeMethods.ObjcSendUlong(
            _companion,
            Sel("setCollectionBehavior:"),
            (nuint)CompanionBehaviour());

        if (role == CompanionRole.Palette)
        {
            // The existing style mask is read back rather than replaced, so the frameless,
            // non-activating bits Avalonia already chose survive this addition.
            var style = MacOSNativeMethods.ObjcSendNoArgs(_companion, Sel("styleMask"));
            MacOSNativeMethods.ObjcSendUlong(
                _companion,
                Sel("setStyleMask:"),
                (nuint)(style | NonactivatingPanelMask));

            MacOSNativeMethods.ObjcSendBool(_companion, Sel("setMovableByWindowBackground:"), false);
        }

        OrderFrontRegardless(_companion);
        return true;
    }

    public void DetachCompanion()
    {
        if (_companion == 0)
            return;

        MacOSNativeMethods.ObjcSendLong(_companion, Sel("setLevel:"), 0);
        MacOSNativeMethods.ObjcSendUlong(_companion, Sel("setCollectionBehavior:"), 0);
        _companion = 0;
    }

    /// <summary>
    /// Nothing to do. AppKit does not round a frameless, transparent window on its own the
    /// way a newer Windows does, so a companion card that drew its radius on all four
    /// corners keeps exactly that after the window is moved or activated, and there is no
    /// host preference to hand over again. Reports that it did nothing, which is the truth.
    /// </summary>
    public bool ReassertCompanionShape() => false;

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
        OrderFrontRegardless(_window);
    }

    private void OrderFrontRegardless(nint window)
    {
        if (window == 0)
            return;

        var selector = Sel("orderFrontRegardless");
        if (MacOSNativeMethods.ObjcRespondsToSelector(window, selector))
            _ = MacOSNativeMethods.ObjcSendNoArgs(window, selector);
    }

    /// <summary>
    /// The palette shares the canvases' spaces but not their cycle: it has to follow the
    /// user to whatever desktop the presentation is on and stay out of the window switcher,
    /// because it is a tool, not an application window.
    /// </summary>
    private static nint CompanionBehaviour() => MacOSNativeMethods.CollectionBehaviorCanJoinAllSpaces
        | MacOSNativeMethods.CollectionBehaviorStationary
        | MacOSNativeMethods.CollectionBehaviorIgnoresCycle;

    private static nint Sel(string name) => MacOSNativeMethods.sel_registerName(name);
}
