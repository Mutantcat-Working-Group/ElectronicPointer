namespace Mutantcat.ElectronicPointer.Core.Screens;

/// <summary>
/// Answers the one question a freeze has to get right: which screen does the display about
/// to be grabbed actually live on. The grab itself reads pixels from the capture backend,
/// but the picture has to be stamped at the right place on the board, and that place comes
/// from the windowing toolkit, so the two have to be tied together.
/// </summary>
public static class ScreenTarget
{
    /// <summary>
    /// The index into <paramref name="screens"/> of the screen <paramref name="display"/>
    /// lives on, or -1 when none of them can be it.
    /// </summary>
    /// <param name="screens">Screens as the windowing toolkit reports them.</param>
    /// <param name="display">The display the next grab reads.</param>
    /// <param name="storedIndex">The display the user picked, as an index into the desk.</param>
    public static int IndexFor(
        IReadOnlyList<ScreenPlacement> screens,
        ScreenPlacement display,
        int storedIndex)
    {
        ArgumentNullException.ThrowIfNull(screens);

        // Position and size together are the display's identity. Two monitors of one
        // resolution are the ordinary desk rather than the exotic one, and a size on its own
        // points at the left hand one every time, which would stamp a grab taken on the
        // right monitor onto the left one's rectangle and leave the ink over nothing.
        for (var i = 0; i < screens.Count; i++)
            if (screens[i] == display)
                return i;

        // A toolkit that counts the desk from a different corner never matches the
        // rectangle. The stored index is the next best answer and it is the display the
        // user picked, so it outranks a size that matches two screens at once.
        if (storedIndex >= 0 && storedIndex < screens.Count)
            return storedIndex;

        for (var i = 0; i < screens.Count; i++)
            if (screens[i].SameSize(display))
                return i;

        return -1;
    }
}
