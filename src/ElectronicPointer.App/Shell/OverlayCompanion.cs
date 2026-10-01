using Avalonia.Controls;
using Avalonia.Threading;
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

    /// <summary>
    /// Hands the keep-the-hands-off corner preference to a bound companion again, without
    /// re-issuing the lift. Safe whether or not a companion is bound yet: the host answers
    /// the lift and the asking as two separate requests, so the guard below runs before the
    /// lift on the opening frame exactly as it runs after it.
    /// </summary>
    public void ReassertShape() => _chrome?.ReassertCompanionShape();

    /// <summary>
    /// Follows a freshly opened companion through the moments the host settles the corner
    /// shape again and asks it once more to keep its hands off. A host that decides a
    /// frameless window's shape every time it composes one decides it again when the window
    /// is moved to its final place, when it is activated, and once more when the final
    /// composition of the opening frame settles; a preference handed over only at the resize
    /// is answered with by each of those, which is what leaves a card that drew an eight
    /// pixel radius on all four corners wearing three corners of the host's instead. The
    /// preference is re-asserted at each of those moments and on a short one-shot timer that
    /// rides out the composition no event announces, and the timer stops itself so a
    /// long-lived window carries no timer and one closed an instant later leaves nothing
    /// running.
    /// </summary>
    public void GuardAgainstLateCornerRounding(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        ReassertShape();

        // Deliberately never unsubscribed. These events belong to the companion window
        // itself, which holds this object in a field for exactly as long as it is open, so
        // there is nothing to leak: a move or an activation that settles the corners again
        // happens on any later frame, not only the opening one.
        window.PositionChanged += (_, _) => ReassertShape();
        window.Activated += (_, _) => ReassertShape();
        window.Deactivated += (_, _) => ReassertShape();

        var beats = 0;
        var settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        settle.Tick += (_, _) =>
        {
            ReassertShape();

            // Roughly a second and a half of beats: long enough to ride out the host's
            // asynchronous final composition of the opening frame, short enough that a
            // window is not followed for the rest of the session.
            if (++beats >= 12)
                settle.Stop();
        };

        settle.Start();
    }
}
