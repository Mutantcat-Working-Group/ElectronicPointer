using Avalonia.Controls;

namespace Mutantcat.ElectronicPointer.App.Shell;

/// <summary>
/// Hands the window manager the instructions a window needs for as long as the window is
/// willing to change its mind about them.
///
/// Windows 11 settles the shape of a frameless window the moment it composes one, and
/// settles that shape again on every re-composition that follows. A window that is resized,
/// re-styled or re-ordered is re-composed, and a window that sizes itself to its content is
/// resized as soon as it opens: the palette asks the platform to grow to the height its
/// content needs, and that growth is one more re-composition. A preference handed over
/// before one of those is answered with rather than carried out by it, so handing it over
/// once leaves a window caught in between with corners that disagree, which for a palette
/// that draws an eight pixel radius of its own is three round corners and one square one.
///
/// There is exactly one stretch of time where the native handle exists and the window is
/// still hidden: Avalonia creates the handle while showing the window, runs a layout pass,
/// and only then tells the platform to make the window visible. <see cref="Window.Resized"/>
/// is raised inside that stretch, which makes it the earliest place this work can be done,
/// and it is the first of the resizes rather than the only one: every later resize raises
/// it again, which is what keeps the preference handed over after the window has stopped
/// changing size too. A window whose size never changes keeps the ordinary post-show path,
/// which leaves it no worse off than before.
/// </summary>
internal static class CompanionWindowExtensions
{
    /// <summary>
    /// Runs <paramref name="apply"/> before the window manager ever sees the window, and
    /// again after every later re-composition that settles the window's shape. Safe on
    /// every platform: the hosts either ignore what runs here until the window is mapped or
    /// apply it harmlessly, and the work is the same every time it runs.
    /// </summary>
    public static void KeepChromeCurrent(this Window window, Action apply)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(apply);

        // Deliberately never unsubscribed. Every resize re-composes the window, and a
        // re-composition is a moment the window manager settles the corner shape again, so
        // a window that sizes itself to its content goes through exactly that as soon as it
        // opens, which is where an early handover used to be undone. A handler that stops
        // after the first resize would be reporting work as done while the window manager
        // was still deciding.
        window.Resized += (_, _) => apply();
    }
}
