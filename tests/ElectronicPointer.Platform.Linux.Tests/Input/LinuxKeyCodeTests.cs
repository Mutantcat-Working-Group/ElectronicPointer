using Mutantcat.ElectronicPointer.Core.Input;
using Mutantcat.ElectronicPointer.Platform.Linux.Native;
using Xunit;

namespace Mutantcat.ElectronicPointer.Platform.Linux.Tests.Input;

/// <summary>
/// The X11 key mapping. A binding that survives a save and reopen has to resolve to the same
/// physical key on another machine, and an unmapped code must never fall through to something
/// else. These are the two properties the hotkey service depends on, and neither of them
/// touches X, so they run everywhere.
/// </summary>
public class LinuxKeyCodeTests
{
    [Fact]
    public void FromXKeyCode_SubtractsTheEvdevOffset()
    {
        Assert.Equal(LinuxKeyCode.EvdevOffset, 8);

        // X reports evdev + 8, so the evdev number for F1 is 59 and the X keycode 67.
        Assert.Equal(KeyCode.F1, LinuxKeyCode.FromXKeyCode(67));
    }

    [Fact]
    public void FromXKeyCode_AtOrBelowTheOffset_IsNone()
    {
        // Keycodes up to the offset belong to the mouse and to keys nobody binds.
        Assert.Equal(KeyCode.None, LinuxKeyCode.FromXKeyCode(0));
        Assert.Equal(KeyCode.None, LinuxKeyCode.FromXKeyCode(8));
    }

    [Fact]
    public void FromXKeyCode_UnknownCode_IsNone()
    {
        // Nothing in the table sits this high, so a foreign keyboard key cannot alias a bound one.
        Assert.Equal(KeyCode.None, LinuxKeyCode.FromXKeyCode(240));
    }

    [Fact]
    public void ToXKeyCode_UnknownKey_IsZeroSoTheGrabIsRefused()
    {
        Assert.Equal(0, LinuxKeyCode.ToXKeyCode(KeyCode.None));
    }

    [Fact]
    public void RoundTrip_EveryEntryInTheTable_Survives()
    {
        foreach (var (key, evdev) in LinuxKeyCode.Table)
        {
            Assert.Equal(key, LinuxKeyCode.FromEvdev(evdev));
            Assert.Equal(evdev + LinuxKeyCode.EvdevOffset, LinuxKeyCode.ToXKeyCode(key));
            Assert.Equal(key, LinuxKeyCode.FromXKeyCode((uint)(evdev + LinuxKeyCode.EvdevOffset)));
        }
    }

    [Fact]
    public void Table_HasNoDuplicatedKeysOrCodes()
    {
        Assert.Equal(LinuxKeyCode.Table.Length, LinuxKeyCode.Table.Select(e => e.Evdev).Distinct().Count());
        Assert.Equal(LinuxKeyCode.Table.Length, LinuxKeyCode.Table.Select(e => e.Key).Distinct().Count());
    }

    [Fact]
    public void FromEvdev_GapsInTheNumbering_AreNone()
    {
        // 89 to 101 are unassigned in evdev; 96 for example belongs to a key nobody binds.
        Assert.Equal(KeyCode.None, LinuxKeyCode.FromEvdev(0));
        Assert.Equal(KeyCode.None, LinuxKeyCode.FromEvdev(100));
    }

    [Theory]
    [InlineData(HotkeyModifiers.None)]
    [InlineData(HotkeyModifiers.Shift)]
    [InlineData(HotkeyModifiers.Control)]
    [InlineData(HotkeyModifiers.Alt)]
    [InlineData(HotkeyModifiers.Command)]
    [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Alt)]
    [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Alt)]
    [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Command | HotkeyModifiers.Shift)]
    public void ToModifierMask_And_FromEventState_RoundTrip(HotkeyModifiers modifiers)
    {
        var mask = LinuxKeyCode.ToModifierMask(modifiers);

        Assert.Equal(modifiers, LinuxKeyCode.FromEventState(mask));
    }

    [Fact]
    public void WindowsModifier_FoldsOntoTheSuperMask()
    {
        // X has no Meta key of its own, so the Windows key is the same mask as Command. A
        // binding recorded on Windows with the Windows key still fires on Linux, which is
        // the behaviour users expect from the "primary" modifier.
        var viaWindows = LinuxKeyCode.ToModifierMask(HotkeyModifiers.Windows);

        Assert.Equal(LinuxKeyCode.ToModifierMask(HotkeyModifiers.Command), viaWindows);
        Assert.Equal(HotkeyModifiers.Command, LinuxKeyCode.FromEventState(viaWindows));
    }

    [Fact]
    public void ToModifierMask_NoModifiers_IsZero()
    {
        // A grab with no modifiers is legal, and zero has to mean exactly that.
        Assert.Equal(0u, LinuxKeyCode.ToModifierMask(HotkeyModifiers.None));
    }

    [Fact]
    public void ToModifierMask_CommandAndWindows_ShareTheSuperMask()
    {
        Assert.Equal(
            LinuxKeyCode.ToModifierMask(HotkeyModifiers.Command),
            LinuxKeyCode.ToModifierMask(HotkeyModifiers.Windows));

        Assert.Equal(0u, LinuxKeyCode.ToModifierMask(HotkeyModifiers.Command) & LinuxKeyCode.ToModifierMask(HotkeyModifiers.Control));
    }

    [Fact]
    public void ExpandModifierStates_CoversBothLocksAndNothingElse()
    {
        var states = LinuxKeyCode.ExpandModifierStates(LinuxKeyCode.ToModifierMask(HotkeyModifiers.Control));

        Assert.Equal(4, states.Length);
        Assert.Contains(LinuxKeyCode.ToModifierMask(HotkeyModifiers.Control), states);
        Assert.Contains(
            LinuxKeyCode.ToModifierMask(HotkeyModifiers.Control) | X11NativeMethods.LockMask,
            states);
        Assert.Contains(
            LinuxKeyCode.ToModifierMask(HotkeyModifiers.Control) | X11NativeMethods.Mod2Mask,
            states);
        Assert.Contains(
            LinuxKeyCode.ToModifierMask(HotkeyModifiers.Control) | X11NativeMethods.LockMask | X11NativeMethods.Mod2Mask,
            states);
        Assert.Equal(4, states.Distinct().Count());
    }

    [Fact]
    public void EveryDefaultHotkey_ResolvesToAKeyOnLinux()
    {
        // A default binding that cannot be grabbed would show up as a dead shortcut on one
        // platform only, which is the kind of thing nobody notices until a class starts.
        foreach (var (hotkey, _) in DefaultHotkeys.All)
        {
            Assert.True(
                LinuxKeyCode.ToXKeyCode(hotkey.Key) > LinuxKeyCode.EvdevOffset,
                $"{hotkey.Text} uses a key Linux cannot grab");
        }
    }
}
