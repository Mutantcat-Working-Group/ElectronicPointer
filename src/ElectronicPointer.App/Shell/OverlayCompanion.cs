using Avalonia.Controls;
using Mutantcat.ElectronicPointer.Platform.Overlay;
using Mutantcat.ElectronicPointer.Platform.Services;

namespace Mutantcat.ElectronicPointer.App.Shell;

/// <summary>
/// One of the app's own windows that has to travel above the ink: the tool palette, and the
/// settings dialog while it is open. A canvas covers a whole display and floats above
/// ordinary windows, so a companion left at the ordinary level is buried under ink that
/// would swallow the click meant for its buttons, and the user is left with nothing at all
/// to press.
///
/// The window handling itself is <see cref="IOverlayChrome"/>'s work. What this adds is the
/// part that would otherwise be written out twice: where the native handle comes from, and
/// letting the binding go when the window closes. Lifting is idempotent, because canvases
/// are rebuilt whenever the display list changes and each new canvas asks to be on top
/// again, which is exactly what buries a companion that was placed before it.
/// </summary>
internal sealed class OverlayCompanion
{
    private readonly IPlatformServices _platform;

    private IOverlayChrome? _chrome;

    public OverlayCompanion(IPlatformServices platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        _platform = platform;
    }

    /// <summary>
    /// Lifts <paramref name="window"/> above every canvas. Called again after a rebuild, so
    /// it does nothing at all until the window has a native handle to hand over.
    /// </summary>
    public void Lift(Window window, CompanionRole role)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window.TryGetPlatformHandle() is not { } handle || handle.Handle == nint.Zero)
            return;

        // One chrome per companion window: a shared instance would have its native target
        // replaced by the next companion, and whichever window closed first would then be
        // lifting a window it never belonged to.
        _chrome ??= _platform.CreateOverlayChrome();
        _chrome.AttachCompanion(new AvaloniaHandle(handle.Handle), role);
    }

    /// <summary>Lets the binding go, now that the window it belonged to is leaving.</summary>
    public void Release()
    {
        if (_chrome is null)
            return;

        _chrome.DetachCompanion();
        _chrome = null;
    }
}
