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

    bool SetClickThrough(bool enabled);

    bool SetAlwaysOnTop(bool enabled);

    bool SetHiddenFromSwitcher(bool hidden);
}
