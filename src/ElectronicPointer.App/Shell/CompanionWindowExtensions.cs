using Avalonia.Controls;

namespace Mutantcat.ElectronicPointer.App.Shell;

/// <summary>
/// Window setup that has to happen before the window manager ever sees the window rather
/// than after. Windows 11 settles the shape of a frameless window the first time it composes
/// one and afterwards leaves a corner it has already decided alone, so chrome handed over
/// once the window is open can arrive too late to apply evenly: a palette that draws its own
/// radius ends up with three rounded corners and one square one.
///
/// There is exactly one stretch of time where the native handle exists and the window is
/// still hidden: Avalonia creates the handle while showing the window, runs a layout pass,
/// and only then tells the platform to make the window visible. <see cref="Window.Resized"/>
/// is raised inside that stretch, once, which makes it the earliest place this work can be
/// done. A window whose size has not changed by then never raises it and keeps the ordinary
/// post-show path, which leaves it no worse off than before.
/// </summary>
internal static class CompanionWindowExtensions
{
    /// <summary>
    /// Runs <paramref name="apply"/> while the window has a native handle but has not been
    /// made visible yet. Safe on every platform: the hosts either ignore what runs here
    /// until the window is mapped or apply it harmlessly, and the same work is repeated once
    /// the window is open, so nothing depends on the call succeeding on its own.
    /// </summary>
    public static void ApplyChromeBeforeFirstShow(this Window window, Action apply)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(apply);

        void OnResized(object? sender, WindowResizedEventArgs e)
        {
            // Unsubscribed before the work runs: a handler that is still attached when the
            // window is later resized would hand the same chrome over a second time, and one
            // that throws would leave itself attached for the reason it threw.
            window.Resized -= OnResized;
            apply();
        }

        window.Resized += OnResized;
    }
}
