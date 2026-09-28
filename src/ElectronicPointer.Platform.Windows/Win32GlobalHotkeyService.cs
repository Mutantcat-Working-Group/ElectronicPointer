using Mutantcat.ElectronicPointer.Core.Input;
using Mutantcat.ElectronicPointer.Platform.Input;

namespace Mutantcat.ElectronicPointer.Platform.Windows;

/// <summary>
/// A message only window that owns the global hotkeys. Windows only delivers WM_HOTKEY to
/// the message queue of the thread that created the registered window, so the sink runs its
/// own small message loop on a dedicated thread. That keeps the shortcut working while
/// PowerPoint or a browser has focus, without borrowing Avalonia's message loop.
/// </summary>
internal sealed class HotkeySink : IDisposable
{
    private const string ClassName = "ElectronicPointerHotkeySink";
    private readonly Lock _gate = new();
    private readonly Dictionary<int, Hotkey> _registrations = new();
    private readonly Action<Hotkey> _onPressed;
    private readonly NativeMethods.WindowProc _windowProc;
    private Thread? _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private IntPtr _hwnd;
    private int _nextId = 1;
    private bool _disposed;

    public HotkeySink(Action<Hotkey> onPressed)
    {
        _onPressed = onPressed;
        _windowProc = WndProc;
    }

    public IntPtr Handle
    {
        get
        {
            _ready.Wait(TimeSpan.FromSeconds(2));
            return _hwnd;
        }
    }

    public bool IsAlive => Handle != IntPtr.Zero;

    /// <summary>Registers a gesture. Returns false when the OS already granted it away.</summary>
    public bool Register(Hotkey hotkey)
    {
        if (!IsAlive)
            return false;

        var virtualKey = ToVirtualKey(hotkey.Key);
        if (virtualKey == 0)
            return false;

        lock (_gate)
        {
            foreach (var pair in _registrations)
            {
                if (pair.Value == hotkey)
                    return true;
            }

            var id = _nextId++;
            if (!NativeMethods.RegisterHotKey(_hwnd, id, ToModifiers(hotkey.Modifiers), virtualKey))
                return false;

            _registrations[id] = hotkey;
            return true;
        }
    }

    public bool Unregister(Hotkey hotkey)
    {
        if (!IsAlive)
            return false;

        lock (_gate)
        {
            var match = _registrations.FirstOrDefault(pair => pair.Value == hotkey).Key;
            if (match == 0)
                return false;

            if (!NativeMethods.UnregisterHotKey(_hwnd, match))
                return false;

            _registrations.Remove(match);
            return true;
        }
    }

    public void UnregisterAll()
    {
        if (!IsAlive)
            return;

        lock (_gate)
        {
            foreach (var id in _registrations.Keys)
                NativeMethods.UnregisterHotKey(_hwnd, id);

            _registrations.Clear();
        }
    }

    public void Start()
    {
        _thread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "ElectronicPointer hotkey sink",
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ = WaitForWindow();
    }

    private bool WaitForWindow()
    {
        if (!_ready.Wait(TimeSpan.FromSeconds(2)))
            return false;

        return _hwnd != IntPtr.Zero;
    }

    private void RunMessageLoop()
    {
        var instance = NativeMethods.GetModuleHandle(null);
        var attributes = new NativeMethods.WndClassW
        {
            Style = 0,
            WindowProc = _windowProc,
            ClsExtra = 0,
            WndExtra = 0,
            Instance = instance,
            Icon = IntPtr.Zero,
            Cursor = IntPtr.Zero,
            BackgroundBrush = IntPtr.Zero,
            MenuName = null,
            ClassName = ClassName,
        };

        NativeMethods.RegisterClass(ref attributes);

        _hwnd = NativeMethods.CreateWindowEx(
            0,
            ClassName,
            "ElectronicPointer",
            0,
            0,
            0,
            0,
            0,
            new IntPtr(-3), // HWND_MESSAGE
            IntPtr.Zero,
            instance,
            IntPtr.Zero);

        _ready.Set();

        if (_hwnd == IntPtr.Zero)
            return;

        while (!_disposed && NativeMethods.GetMessage(out var message, _hwnd, 0, 0) != 0)
        {
            NativeMethods.TranslateMessage(ref message);
            NativeMethods.DispatchMessage(ref message);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == NativeMethods.WmHotkey)
        {
            var id = wParam.ToInt32();
            Hotkey hotkey;
            lock (_gate)
            {
                if (!_registrations.TryGetValue(id, out hotkey))
                    return IntPtr.Zero;
            }

            _onPressed(hotkey);
            return IntPtr.Zero;
        }

        if (message == NativeMethods.WmDestroy)
        {
            NativeMethods.PostQuitMessage(0);
            return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private static uint ToModifiers(HotkeyModifiers modifiers)
    {
        var result = 0u;
        if ((modifiers & HotkeyModifiers.Alt) != 0)
            result |= NativeMethods.ModAlt;
        if ((modifiers & HotkeyModifiers.Control) != 0)
            result |= NativeMethods.ModControl;
        if ((modifiers & HotkeyModifiers.Shift) != 0)
            result |= NativeMethods.ModShift;
        if ((modifiers & (HotkeyModifiers.Command | HotkeyModifiers.Windows)) != 0)
        {
            result |= NativeMethods.ModWin;
        }

        return result | NativeMethods.ModNoRepeat;
    }

    private static uint ToVirtualKey(KeyCode key) => key switch
    {
        KeyCode.A => 0x41,
        KeyCode.B => 0x42,
        KeyCode.C => 0x43,
        KeyCode.D => 0x44,
        KeyCode.E => 0x45,
        KeyCode.F => 0x46,
        KeyCode.G => 0x47,
        KeyCode.H => 0x48,
        KeyCode.I => 0x49,
        KeyCode.J => 0x4A,
        KeyCode.K => 0x4B,
        KeyCode.L => 0x4C,
        KeyCode.M => 0x4D,
        KeyCode.N => 0x4E,
        KeyCode.O => 0x4F,
        KeyCode.P => 0x50,
        KeyCode.Q => 0x51,
        KeyCode.R => 0x52,
        KeyCode.S => 0x53,
        KeyCode.T => 0x54,
        KeyCode.U => 0x55,
        KeyCode.V => 0x56,
        KeyCode.W => 0x57,
        KeyCode.X => 0x58,
        KeyCode.Y => 0x59,
        KeyCode.Z => 0x5A,
        KeyCode.D0 => 0x30,
        KeyCode.D1 => 0x31,
        KeyCode.D2 => 0x32,
        KeyCode.D3 => 0x33,
        KeyCode.D4 => 0x34,
        KeyCode.D5 => 0x35,
        KeyCode.D6 => 0x36,
        KeyCode.D7 => 0x37,
        KeyCode.D8 => 0x38,
        KeyCode.D9 => 0x39,
        KeyCode.NumPad0 => 0x60,
        KeyCode.NumPad1 => 0x61,
        KeyCode.NumPad2 => 0x62,
        KeyCode.NumPad3 => 0x63,
        KeyCode.NumPad4 => 0x64,
        KeyCode.NumPad5 => 0x65,
        KeyCode.NumPad6 => 0x66,
        KeyCode.NumPad7 => 0x67,
        KeyCode.NumPad8 => 0x68,
        KeyCode.NumPad9 => 0x69,
        KeyCode.F1 => 0x70,
        KeyCode.F2 => 0x71,
        KeyCode.F3 => 0x72,
        KeyCode.F4 => 0x73,
        KeyCode.F5 => 0x74,
        KeyCode.F6 => 0x75,
        KeyCode.F7 => 0x76,
        KeyCode.F8 => 0x77,
        KeyCode.F9 => 0x78,
        KeyCode.F10 => 0x79,
        KeyCode.F11 => 0x7A,
        KeyCode.F12 => 0x7B,
        KeyCode.Left => 0x25,
        KeyCode.Up => 0x26,
        KeyCode.Right => 0x27,
        KeyCode.Down => 0x28,
        KeyCode.Escape => 0x1B,
        KeyCode.Tab => 0x09,
        KeyCode.Back => 0x08,
        KeyCode.Delete => 0x2E,
        KeyCode.Insert => 0x2D,
        KeyCode.Home => 0x24,
        KeyCode.End => 0x23,
        KeyCode.PageUp => 0x21,
        KeyCode.PageDown => 0x22,
        KeyCode.Space => 0x20,
        KeyCode.Enter => 0x0D,
        KeyCode.Comma => 0xBC,
        KeyCode.Period => 0xBE,
        KeyCode.Minus => 0xBD,
        KeyCode.Plus => 0xBB,
        _ => 0,
    };

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        UnregisterAll();

        var handle = _hwnd;
        _hwnd = IntPtr.Zero;
        _ready.Dispose();

        if (handle != IntPtr.Zero)
            NativeMethods.DestroyWindow(handle);
    }
}

/// <summary>
/// Global hotkeys on Windows. Registration can fail for combinations Windows reserves for
/// the user or that another application already owns, so callers must check the result.
/// </summary>
public sealed class Win32GlobalHotkeyService : IGlobalHotkeyService
{
    private readonly HotkeySink _sink;

    public Win32GlobalHotkeyService()
    {
        _sink = new HotkeySink(hotkey => Pressed?.Invoke(this, hotkey));
        _sink.Start();
    }

    public bool IsSupported => _sink.IsAlive;

    public event EventHandler<Hotkey>? Pressed;

    public bool Register(Hotkey hotkey) => _sink.Register(hotkey);

    public bool Unregister(Hotkey hotkey) => _sink.Unregister(hotkey);

    public void UnregisterAll() => _sink.UnregisterAll();

    public void Dispose() => _sink.Dispose();
}
