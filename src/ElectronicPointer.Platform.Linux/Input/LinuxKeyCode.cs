using Mutantcat.ElectronicPointer.Platform.Linux.Native;
using Mutantcat.ElectronicPointer.Core.Input;

namespace Mutantcat.ElectronicPointer.Platform.Linux;

/// <summary>
/// Linux evdev key codes, which X reports shifted by eight in the keycode of a key event,
/// mapped onto the platform-neutral key set. The evdev numbering is stable across keyboards,
/// layouts and input methods, so a binding recorded on one machine still resolves on
/// another. Anything outside the table reports <see cref="KeyCode.None"/>, so a code we do
/// not know cannot silently bind to something else.
/// </summary>
internal static class LinuxKeyCode
{
    /// <summary>X keycode to evdev: the X server always adds eight to the evdev number.</summary>
    internal const int EvdevOffset = 8;

    /// <summary>The neutral keys the app can bind, as (KeyCode, evdev) pairs.</summary>
    internal static readonly (KeyCode Key, int Evdev)[] Table =
    {
        (KeyCode.Escape, 1),
        (KeyCode.D1, 2),
        (KeyCode.D2, 3),
        (KeyCode.D3, 4),
        (KeyCode.D4, 5),
        (KeyCode.D5, 6),
        (KeyCode.D6, 7),
        (KeyCode.D7, 8),
        (KeyCode.D8, 9),
        (KeyCode.D9, 10),
        (KeyCode.D0, 11),
        (KeyCode.Minus, 12),
        (KeyCode.Plus, 13),
        (KeyCode.Back, 14),
        (KeyCode.Tab, 15),
        (KeyCode.Q, 16),
        (KeyCode.W, 17),
        (KeyCode.E, 18),
        (KeyCode.R, 19),
        (KeyCode.T, 20),
        (KeyCode.Y, 21),
        (KeyCode.U, 22),
        (KeyCode.I, 23),
        (KeyCode.O, 24),
        (KeyCode.P, 25),
        (KeyCode.Enter, 28),
        (KeyCode.A, 30),
        (KeyCode.S, 31),
        (KeyCode.D, 32),
        (KeyCode.F, 33),
        (KeyCode.G, 34),
        (KeyCode.H, 35),
        (KeyCode.J, 36),
        (KeyCode.K, 37),
        (KeyCode.L, 38),
        (KeyCode.Z, 44),
        (KeyCode.X, 45),
        (KeyCode.C, 46),
        (KeyCode.V, 47),
        (KeyCode.B, 48),
        (KeyCode.N, 49),
        (KeyCode.M, 50),
        (KeyCode.Comma, 51),
        (KeyCode.Period, 52),
        (KeyCode.Space, 57),
        (KeyCode.F1, 59),
        (KeyCode.F2, 60),
        (KeyCode.F3, 61),
        (KeyCode.F4, 62),
        (KeyCode.F5, 63),
        (KeyCode.F6, 64),
        (KeyCode.F7, 65),
        (KeyCode.F8, 66),
        (KeyCode.F9, 67),
        (KeyCode.F10, 68),
        (KeyCode.F11, 87),
        (KeyCode.F12, 88),
        (KeyCode.Home, 102),
        (KeyCode.Up, 103),
        (KeyCode.PageUp, 104),
        (KeyCode.Left, 105),
        (KeyCode.Right, 106),
        (KeyCode.End, 107),
        (KeyCode.Down, 108),
        (KeyCode.PageDown, 109),
        (KeyCode.Insert, 110),
        (KeyCode.Delete, 111),
        (KeyCode.NumPad0, 82),
        (KeyCode.NumPad1, 79),
        (KeyCode.NumPad2, 80),
        (KeyCode.NumPad3, 81),
        (KeyCode.NumPad4, 75),
        (KeyCode.NumPad5, 76),
        (KeyCode.NumPad6, 77),
        (KeyCode.NumPad7, 71),
        (KeyCode.NumPad8, 72),
        (KeyCode.NumPad9, 73),
    };

    /// <summary>Translates the keycode carried by an X key press event.</summary>
    public static KeyCode FromXKeyCode(uint keyCode)
    {
        if (keyCode <= EvdevOffset)
            return KeyCode.None;

        return FromEvdev((int)keyCode - EvdevOffset);
    }

    /// <summary>Translates a neutral key back into the X keycode needed to grab it.</summary>
    public static int ToXKeyCode(KeyCode key)
    {
        foreach (var entry in Table)
        {
            if (entry.Key == key)
                return entry.Evdev + EvdevOffset;
        }

        return 0;
    }

    public static KeyCode FromEvdev(int evdev)
    {
        foreach (var entry in Table)
        {
            if (entry.Evdev == evdev)
                return entry.Key;
        }

        return KeyCode.None;
    }

    /// <summary>
    /// The X modifier mask for a neutral modifier. Command sits on Mod4 (the Super key) so
    /// the same "primary" modifier the other two platforms use still resolves here, and
    /// Windows maps to the same mask because there is no separate Meta key on X11.
    /// </summary>
    public static uint ToModifierMask(HotkeyModifiers modifiers)
    {
        uint mask = 0;

        // A binding with no modifiers is a legitimate grab target, so zero is a valid result.
        if ((modifiers & HotkeyModifiers.Shift) != 0)
            mask |= X11NativeMethods.ShiftMask;

        if ((modifiers & HotkeyModifiers.Control) != 0)
            mask |= X11NativeMethods.ControlMask;

        if ((modifiers & HotkeyModifiers.Alt) != 0)
            mask |= X11NativeMethods.Mod1Mask;

        if ((modifiers & (HotkeyModifiers.Command | HotkeyModifiers.Windows)) != 0)
            mask |= X11NativeMethods.Mod4Mask;

        return mask;
    }

    /// <summary>
    /// X refuses a grab that does not also cover every lock state, so the modifier has to be
    /// registered with Caps Lock and Num Lock on, off, and both.
    /// </summary>
    public static uint[] ExpandModifierStates(uint mask) => new[]
    {
        mask,
        mask | X11NativeMethods.LockMask,
        mask | X11NativeMethods.Mod2Mask,
        mask | X11NativeMethods.LockMask | X11NativeMethods.Mod2Mask,
    };

    /// <summary>
    /// Reads the modifiers X reports in a key event back into the neutral form. X11 carries
    /// the classic Ctrl/Shift/Alt masks and puts Super on Mod4.
    /// </summary>
    public static HotkeyModifiers FromEventState(uint state)
    {
        var modifiers = HotkeyModifiers.None;

        // A bare key with no modifiers is never a hotkey, so none simply stays clear.
        if ((state & X11NativeMethods.ShiftMask) != 0)
            modifiers |= HotkeyModifiers.Shift;

        if ((state & X11NativeMethods.ControlMask) != 0)
            modifiers |= HotkeyModifiers.Control;

        if ((state & X11NativeMethods.Mod1Mask) != 0)
            modifiers |= HotkeyModifiers.Alt;

        if ((state & X11NativeMethods.Mod4Mask) != 0)
            modifiers |= HotkeyModifiers.Command;

        return modifiers;
    }
}
