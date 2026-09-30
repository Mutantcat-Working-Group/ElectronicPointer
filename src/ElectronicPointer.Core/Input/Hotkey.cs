namespace Mutantcat.ElectronicPointer.Core.Input;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Command = 8,
    Windows = 16,
}

/// <summary>
/// A key plus modifier combination. Parsed from the "Ctrl+Shift+Z" notation used by the
/// settings file so bindings survive a round trip to disk.
/// </summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, KeyCode Key)
{
    /// <summary>
    /// The modifier that acts as "primary" on the current platform: Command on macOS,
    /// Control everywhere else. Use it for the shared shortcuts so muscle memory matches
    /// the host OS.
    /// </summary>
    public static HotkeyModifiers PlatformPrimary
    {
        get
        {
            return OperatingSystem.IsMacOS() ? HotkeyModifiers.Command : HotkeyModifiers.Control;
        }
    }

    /// <summary>
    /// The gesture as the running host actually executes it: Control becomes Command on
    /// macOS. Hints built from the raw struct would otherwise promise a Ctrl+Q a Mac user
    /// can never press, so the shell registers and the toolbar shows what this returns.
    /// </summary>
    public Hotkey OnThisPlatform()
    {
        if (!OperatingSystem.IsMacOS() || (Modifiers & HotkeyModifiers.Control) == 0)
            return this;

        var modifiers = (Modifiers & ~HotkeyModifiers.Control) | HotkeyModifiers.Command;
        return new Hotkey(modifiers, Key);
    }

    public string Text
    {
        get
        {
            var sb = new System.Text.StringBuilder();
            if ((Modifiers & HotkeyModifiers.Control) != 0) sb.Append("Ctrl+");
            if ((Modifiers & HotkeyModifiers.Alt) != 0) sb.Append("Alt+");
            if ((Modifiers & HotkeyModifiers.Shift) != 0) sb.Append("Shift+");
            if ((Modifiers & HotkeyModifiers.Command) != 0) sb.Append("Cmd+");
            if ((Modifiers & HotkeyModifiers.Windows) != 0) sb.Append("Win+");
            sb.Append(Key);
            return sb.ToString();
        }
    }

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var modifiers = HotkeyModifiers.None;
        var key = KeyCode.None;

        foreach (var rawToken in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var token = rawToken.ToLowerInvariant();
            switch (token)
            {
                case "ctrl":
                case "control":
                    modifiers |= HotkeyModifiers.Control;
                    break;
                case "alt":
                case "option":
                case "opt":
                    modifiers |= HotkeyModifiers.Alt;
                    break;
                case "shift":
                    modifiers |= HotkeyModifiers.Shift;
                    break;
                case "cmd":
                case "command":
                case "meta":
                case "super":
                case "win":
                case "windows":
                    modifiers |= HotkeyModifiers.Command;
                    break;
                default:
                    if (!TryParseKey(token, out key))
                        return false;
                    break;
            }
        }

        if (key == KeyCode.None)
            return false;

        hotkey = new Hotkey(modifiers, key);
        return true;
    }

    public static Hotkey Parse(string? text) =>
        TryParse(text, out var hotkey) ? hotkey : throw new FormatException($"\"{text}\" is not a valid hotkey gesture.");

    public bool Matches(KeyCode key, HotkeyModifiers modifiers) => Key == key && Modifiers == modifiers;

    public override string ToString() => Text;

    private static bool TryParseKey(string token, out KeyCode key)
    {
        key = token switch
        {
            "esc" or "escape" => KeyCode.Escape,
            "tab" => KeyCode.Tab,
            "backspace" => KeyCode.Back,
            "del" or "delete" => KeyCode.Delete,
            "ins" or "insert" => KeyCode.Insert,
            "home" => KeyCode.Home,
            "end" => KeyCode.End,
            "pgup" or "pageup" => KeyCode.PageUp,
            "pgdn" or "pagedown" => KeyCode.PageDown,
            "space" or " " => KeyCode.Space,
            "enter" or "return" => KeyCode.Enter,
            "left" => KeyCode.Left,
            "up" => KeyCode.Up,
            "right" => KeyCode.Right,
            "down" => KeyCode.Down,
            "comma" or "," => KeyCode.Comma,
            "period" or "." => KeyCode.Period,
            "minus" or "-" => KeyCode.Minus,
            "plus" or "=" or "+" => KeyCode.Plus,
            _ => Enum.TryParse(token, ignoreCase: true, out KeyCode parsed) ? parsed : KeyCode.None,
        };

        return key != KeyCode.None;
    }
}

/// <summary>The built-in gestures, kept in one place so the settings view can list them.</summary>
public static class DefaultHotkeys
{
    public static readonly Hotkey TogglePassThrough = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, KeyCode.T);

    public static readonly Hotkey Pen = new(HotkeyModifiers.Alt, KeyCode.P);

    public static readonly Hotkey Highlighter = new(HotkeyModifiers.Alt, KeyCode.H);

    public static readonly Hotkey Eraser = new(HotkeyModifiers.Alt, KeyCode.E);

    public static readonly Hotkey Select = new(HotkeyModifiers.Alt, KeyCode.S);

    public static readonly Hotkey Undo = new(HotkeyModifiers.Control, KeyCode.Z);

    public static readonly Hotkey Redo = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, KeyCode.Z);

    public static readonly Hotkey RedoAlternate = new(HotkeyModifiers.Control, KeyCode.Y);

    public static readonly Hotkey Quit = new(HotkeyModifiers.Control, KeyCode.Q);

    public static readonly Hotkey ClearPage = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, KeyCode.Delete);

    public static readonly Hotkey NewPage = new(HotkeyModifiers.Control, KeyCode.N);

    public static readonly Hotkey NextPage = new(HotkeyModifiers.Control, KeyCode.PageDown);

    public static readonly Hotkey PreviousPage = new(HotkeyModifiers.Control, KeyCode.PageUp);

    public static readonly Hotkey ToggleToolbar = new(HotkeyModifiers.Control, KeyCode.Tab);

    public static IReadOnlyList<(Hotkey Hotkey, string Description)> All { get; } = new (Hotkey, string)[]
    {
        (Pen, "画笔"),
        (Highlighter, "荧光笔"),
        (Eraser, "橡皮"),
        (Select, "选择"),
        (TogglePassThrough, "切换穿透模式"),
        (Undo, "撤销"),
        (Redo, "重做"),
        (ClearPage, "清空当前页"),
        (NewPage, "新建页面"),
        (NextPage, "下一页"),
        (PreviousPage, "上一页"),
        (ToggleToolbar, "显示/隐藏工具栏"),
        (Quit, "退出"),
    };
}
