using Mutantcat.ElectronicPointer.Core.Input;
using Xunit;

namespace Mutantcat.ElectronicPointer.Core.Tests.Input;

/// <summary>
/// The list of refused gestures is the only thing that tells a user why a shortcut they
/// press does nothing, so the sentence has to name every one of them and nothing else.
/// An empty list must read as empty rather than as a stray separator, and a gesture
/// without an action label must not grow brackets around nothing.
/// </summary>
public class HotkeyConflictsTests
{
    [Fact]
    public void Describe_SaysNothingWhenEveryGestureCameUp()
    {
        Assert.Equal(string.Empty, HotkeyConflicts.Describe(Array.Empty<(Hotkey, string)>()));
    }

    [Fact]
    public void Describe_NamesOneGestureAboveItsAction()
    {
        var refused = new[] { (Hotkey: DefaultHotkeys.Pen, Description: "画笔") };

        Assert.Equal("Alt+P（画笔）", HotkeyConflicts.Describe(refused));
    }

    [Fact]
    public void Describe_SeparatesSeveralWithTheListingComma()
    {
        var refused = new[]
        {
            (Hotkey: DefaultHotkeys.Pen, Description: "画笔"),
            (Hotkey: DefaultHotkeys.Quit, Description: "退出"),
        };

        Assert.Equal("Alt+P（画笔）、Ctrl+Q（退出）", HotkeyConflicts.Describe(refused));
    }

    [Fact]
    public void Describe_ReadsAnUnlabelledGestureAsJustItsText()
    {
        var refused = new[] { (Hotkey: DefaultHotkeys.ToggleToolbar, Description: string.Empty) };

        Assert.Equal("Ctrl+Tab", HotkeyConflicts.Describe(refused));
    }
}
