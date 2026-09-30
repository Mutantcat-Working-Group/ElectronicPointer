using Mutantcat.ElectronicPointer.Core.Input;
using Xunit;

namespace Mutantcat.ElectronicPointer.Core.Tests.Input;

/// <summary>
/// The hint a user is shown has to be the gesture the host will actually run. The shared
/// table is written with Control, and macOS turns that into Command on the way in, so the
/// text has to follow the same translation or a Mac user is promised a Ctrl+Q nobody can
/// press.
/// </summary>
public class HotkeyPlatformTextTests
{
    [Fact]
    public void OnThisPlatform_TranslatesControlToCommandOnMacOsOnly()
    {
        var gesture = DefaultHotkeys.Undo.OnThisPlatform();

        if (OperatingSystem.IsMacOS())
        {
            Assert.Equal("Cmd+Z", gesture.Text);
            return;
        }

        Assert.Equal(DefaultHotkeys.Undo, gesture);
        Assert.Equal("Ctrl+Z", gesture.Text);
    }

    [Fact]
    public void OnThisPlatform_LeavesAGestureItCannotImproveAlone()
    {
        var pen = DefaultHotkeys.Pen;

        // Alt is Alt everywhere, and a gesture already carrying Command has nothing to
        // translate: the answer has to be the same struct it started from.
        Assert.Equal(pen, pen.OnThisPlatform());
    }

    [Fact]
    public void OnThisPlatform_AlwaysLeavesExactlyOnePrimaryModifier()
    {
        var quit = DefaultHotkeys.Quit.OnThisPlatform();
        var control = (quit.Modifiers & HotkeyModifiers.Control) != 0;
        var command = (quit.Modifiers & HotkeyModifiers.Command) != 0;

        // Whichever one survives has to be the one this host treats as primary, and never
        // both: a gesture with two primaries is one the user cannot press.
        Assert.Equal(Hotkey.PlatformPrimary == HotkeyModifiers.Command, command);
        Assert.Equal(Hotkey.PlatformPrimary == HotkeyModifiers.Control, control);
        Assert.NotEqual(control, command);
    }
}
