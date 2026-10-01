namespace Mutantcat.ElectronicPointer.Platform.Overlay;

/// <summary>
/// The native window the overlay lives in. The platform implementation coerces this to
/// whatever the host OS needs: an HWND, an NSWindow, or an X11/Wayland surface.
/// </summary>
public interface IOverlayWindowTarget
{
    nint Handle { get; }
}

/// <summary>
/// The part a window that travels with the overlay plays above the canvases. The palette
/// never takes focus, so a host that needs a non-activating panel for that may be asked for
/// one; a dialog has to keep the activation it has, because reading it and reaching its
/// controls with the keyboard both depend on it becoming the key window.
/// </summary>
public enum CompanionRole
{
    /// <summary>The tool palette: reachable at all times, never the focus of the desk.</summary>
    Palette,

    /// <summary>An ordinary window that only has to sit above the canvases while open.</summary>
    Dialog,
}

/// <summary>
/// Window behaviours a transparent overlay needs that no UI toolkit offers cross platform:
/// letting clicks fall through to the desktop behind, staying above a full screen
/// presentation, and staying out of the dock, task bar and window switcher.
///
/// Every member reports whether the change was applied so the UI can say so instead of
/// silently doing nothing.
/// </summary>
public interface IOverlayChrome
{
    bool IsSupported { get; }

    /// <summary>Binds to a window. Must be called before any other member.</summary>
    bool Attach(IOverlayWindowTarget target);

    void Detach();

    /// <summary>
    /// Binds to a window that travels with the overlay rather than being one of its
    /// canvases: the tool palette, and the settings dialog while it is open. An overlay
    /// surface covers a whole display and floats above ordinary windows, so a companion
    /// has to be lifted above the surfaces it drives or its buttons end up buried under
    /// ink that eats the click.
    /// </summary>
    bool AttachCompanion(IOverlayWindowTarget target, CompanionRole role);

    void DetachCompanion();

    bool SetClickThrough(bool enabled);

    bool SetAlwaysOnTop(bool enabled);

    bool SetHiddenFromSwitcher(bool hidden);

    /// <summary>
    /// Hands the keep-the-hands-off corner preference to a bound companion once more, without
    /// re-issuing the lift. A host that settles a frameless window's shape on every
    /// composition settles it again after the window is moved to its final place, is
    /// activated, or finishes its opening layout, and each of those would answer a
    /// preference handed over only at the resize with a corner of its own, which leaves a
    /// card that drew its radius on all four corners with three of the host's and one of its
    /// own. Reports whether a bound companion was there to be asked, so a host that never
    /// rounds a frameless window says so instead of pretending otherwise.
    /// </summary>
    bool ReassertCompanionShape();
}

/// <summary>
/// The shape a companion window draws around itself, in the layout units the window is
/// built in. A rounded card over a transparent window is the whole shape, which is why it
/// looks the same on every platform: there is no native frame under it to disagree with
/// and no host that is asked to cut anything. A host that rounds a frameless window of
/// its own accord is asked to keep its hands off instead, because a corner the
/// application drew itself has no second opinion to reconcile.
/// </summary>
public static class CompanionShape
{
    /// <summary>
    /// The radius every companion window draws on all four of its corners. The card and
    /// the window itself are settled from this one number so the two cannot drift apart: a
    /// radius that appears in one companion and not another leaves windows that disagree.
    /// </summary>
    public const int CornerRadius = 8;
}
