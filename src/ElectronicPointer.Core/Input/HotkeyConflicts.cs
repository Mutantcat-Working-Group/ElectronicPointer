namespace Mutantcat.ElectronicPointer.Core.Input;

/// <summary>
/// The built-in gestures the host refused, phrased the way a person reads them. An OS
/// answers "no" when a combination is already somebody else's, and both hosts that can
/// refuse do exactly that: Windows declines a reserved or taken gesture outright and X11
/// answers AlreadyGrabbed. Those refusals used to be dropped in silence, which left the user
/// pressing a dead shortcut with nothing on screen to say why. They are collected as they
/// happen and read once here, so the palette's status line and the settings' capability
/// row cannot drift apart about the same list.
/// </summary>
public static class HotkeyConflicts
{
    /// <summary>
    /// "Alt+P（画笔）、Ctrl+Q（退出）" for the gestures handed over, or an empty string when
    /// there were none. A gesture nobody labelled reads as just its text, and a lone one
    /// carries no separator, because a list of one is a name rather than a list.
    /// </summary>
    public static string Describe(IEnumerable<(Hotkey Hotkey, string Description)> refused)
    {
        ArgumentNullException.ThrowIfNull(refused);

        var items = new List<string>();
        foreach (var (hotkey, description) in refused)
        {
            items.Add(string.IsNullOrWhiteSpace(description)
                ? hotkey.Text
                : $"{hotkey.Text}（{description}）");
        }

        return string.Join("、", items);
    }
}
