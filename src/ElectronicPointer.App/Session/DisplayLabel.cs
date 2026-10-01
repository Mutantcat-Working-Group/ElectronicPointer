using Mutantcat.ElectronicPointer.Platform.Displays;

namespace Mutantcat.ElectronicPointer.App.Session;

/// <summary>
/// What a display is called wherever the app has to name one. The raw names are device
/// handles, "\\.\DISPLAY2" and friends, which say nothing to the person looking at the
/// desk, so every list and tooltip that names a display comes through here instead: a
/// number, the resolution and the primary marker, which reads the same on all three
/// platforms whatever the host calls the monitor.
/// </summary>
internal static class DisplayLabel
{
    public static string Of(DisplayInfo display) =>
        Of(new[] { display }, display);

    /// <param name="displays">The whole desk, so the display can be placed on it by name.</param>
    public static string Of(IReadOnlyList<DisplayInfo> displays, DisplayInfo display)
    {
        var side = SideOf(displays, display);

        return $"显示器 {display.Index + 1}{side} · {display.Width}x{display.Height}" +
            (display.IsPrimary ? "（主显示器）" : string.Empty);
    }

    /// <summary>
    /// Where on the desk the display sits, as a word. Two monitors of one resolution look
    /// identical in a list of numbers, and a second screen of the same size is the ordinary
    /// arrangement, so the one thing left to tell them apart is the side of the room.
    /// </summary>
    private static string SideOf(IReadOnlyList<DisplayInfo> displays, DisplayInfo display)
    {
        if (displays.Count < 2)
            return string.Empty;

        if (displays.Select(candidate => candidate.X).Distinct().Count() < 2)
            return string.Empty;

        if (display.X == displays.Min(candidate => candidate.X))
            return "（左侧）";

        return display.X == displays.Max(candidate => candidate.X) ? "（右侧）" : "（中间）";
    }
}
