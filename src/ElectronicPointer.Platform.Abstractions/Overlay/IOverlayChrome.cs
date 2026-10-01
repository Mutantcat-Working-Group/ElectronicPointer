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
}

/// <summary>
/// The shape a companion window draws around itself, in the layout units the window is
/// built in. A host that decides the corners of a frameless window on its own cannot be
/// argued with by drawing: a native handle answers no question about what the
/// application painted inside it, so the only way to stop a host from rounding a corner
/// the application drew square, or squaring one it drew round, is to tell the host the
/// radius and let it cut the same window the application drew.
/// </summary>
public static class CompanionShape
{
    /// <summary>
    /// The radius every companion window draws on all four of its corners. The card and
    /// the host are cut from this one number so the two cannot drift apart: a radius that
    /// appears in the card and not here leaves a corner the host cut to a shape the
    /// application never drew.
    /// </summary>
    public const int CornerRadius = 8;
}
