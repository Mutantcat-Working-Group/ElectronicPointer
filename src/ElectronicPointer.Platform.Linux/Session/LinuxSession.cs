namespace Mutantcat.ElectronicPointer.Platform.Linux;

/// <summary>Which display server the current seat is actually talking to.</summary>
public enum LinuxSessionKind
{
    /// <summary>No graphical session at all: a console login or a headless container.</summary>
    Headless,

    /// <summary>X11, where global key grabs, window placement and server side reads still work.</summary>
    X11,

    /// <summary>
    /// A native Wayland compositor. Those deliberately withhold global key grabs, arbitrary
    /// window placement and server side screen reads, so the app still runs but cannot do
    /// the overlay work; the user is told instead of being left with silent failures.
    /// </summary>
    Wayland,
}

/// <summary>
/// Session probing from the environment, which is the only portable way to tell the two
/// display servers apart. XDG_SESSION_TYPE is read before DISPLAY, because a modern Wayland
/// session usually sets DISPLAY as well for XWayland, so checking DISPLAY first would
/// mistake a native Wayland seat for X11 and every overlay feature would fail later with
/// no explanation for the user.
/// </summary>
public static class LinuxSession
{
    public static LinuxSessionKind Current { get; } = Detect();

    /// <summary>True when the session can still be driven like an X11 desktop.</summary>
    public static bool IsX11 => Current == LinuxSessionKind.X11;

    /// <summary>True when nothing graphical is reachable at all.</summary>
    public static bool IsHeadless => Current == LinuxSessionKind.Headless;

    /// <summary>The name of the X display in use, for example ":0". Null when there is none.</summary>
    public static string? DisplayName { get; } = Environment.GetEnvironmentVariable("DISPLAY");

    private static LinuxSessionKind Detect()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase))
            return LinuxSessionKind.Wayland;

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
            return LinuxSessionKind.X11;

        return string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
            ? LinuxSessionKind.Headless
            : LinuxSessionKind.Wayland;
    }
}
