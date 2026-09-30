using Mutantcat.ElectronicPointer.Core.Input;
using Mutantcat.ElectronicPointer.Platform.Input;
using System.Collections.Concurrent;

namespace Mutantcat.ElectronicPointer.Platform.Windows;

/// <summary>
/// A message only window that owns the global hotkeys. Windows only delivers WM_HOTKEY to
/// the message queue of the thread that created the registered window, so the sink runs its
/// own small message loop on a dedicated thread. That keeps the shortcut working while
/// PowerPoint or a browser has focus, without borrowing Avalonia's message loop.
/// </summary>
internal sealed class HotkeySink : IDisposable
{
    // Windows keeps one window class per name, and the class carries the window
    // procedure that every window built from it is driven by. Two sinks sharing a name
    // would leave the second one's window answered by the first one's procedure, so the
    // queue nobody drains and a registration that waits out its timeout. A counter per
    // process gives every sink a class of its own.
    private static int _classCounter;

    private readonly string _className =
        $"ElectronicPointerHotkeySink{Interlocked.Increment(ref _classCounter)}";


    // WM_CLOSE asks the window to go away, and the private message parks a draining
    // pass over the queue: GetMessage sleeps until something arrives, so work that
    // nothing announced would otherwise wait for the next keypress to be noticed.
    private const uint WmClose = 0x0010;
    private const uint WmRunQueuedWork = 0x8001; // WM_APP + 1

    // A bound on the wait for the sink thread's answer, so a wedged sink cannot hang
    // the caller for good. The window is up before anything is queued, so an answer is
    // a message round trip away rather than a second of waiting.
    private static readonly TimeSpan RoundTripTimeout = TimeSpan.FromSeconds(5);

    private enum WorkKind
    {
        Register,
        Unregister,
        UnregisterAll,
    }

    private readonly record struct QueuedWork(WorkKind Kind, Hotkey Hotkey, TaskCompletionSource<bool> Completion);

    private readonly Lock _gate = new();
    private readonly Dictionary<int, Hotkey> _registrations = new();
    private readonly Action<Hotkey> _onPressed;
    private readonly NativeMethods.WindowProc _windowProc;

    // RegisterHotKey, UnregisterHotKey and DestroyWindow all refuse a window another
    // thread created, which ends in ERROR_WINDOW_OF_OTHER_THREAD and a gesture that
    // looks owned when nothing else holds it. Everything touching the window therefore
    // runs on the sink thread through this queue and is answered by a task.
    private readonly ConcurrentQueue<QueuedWork> _queue = new();
    private Thread? _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private IntPtr _hwnd;
    private int _sinkThread = -1;
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
        if (!WaitForSink())
            return false;

        // A caller already on the sink thread has to answer on the spot: the loop that
        // would run the queue is the one calling, and waiting for it waits for itself.
        if (OnSinkThread)
            return RegisterCore(hotkey);

        return Ask(WorkKind.Register, hotkey);
    }

    public bool Unregister(Hotkey hotkey)
    {
        if (!WaitForSink())
            return false;

        if (OnSinkThread)
            return UnregisterCore(hotkey);

        return Ask(WorkKind.Unregister, hotkey);
    }

    public void UnregisterAll()
    {
        if (!WaitForSink())
            return;

        if (OnSinkThread)
        {
            UnregisterAllCore();
            return;
        }

        _ = Ask(WorkKind.UnregisterAll, default);
    }

    /// <summary>Queues work for the sink thread and waits for the answer it owes.</summary>
    private bool Ask(WorkKind kind, Hotkey hotkey)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Enqueue(new QueuedWork(kind, hotkey, completion));
        Wake();
        return completion.Task.Wait(RoundTripTimeout) && completion.Task.Result;
    }

    private void Wake()
    {
        var handle = _hwnd;
        if (handle != IntPtr.Zero)
            NativeMethods.PostMessage(handle, WmRunQueuedWork, IntPtr.Zero, IntPtr.Zero);
    }

    private bool WaitForSink() => IsAlive && !_disposed;

    private bool OnSinkThread => Environment.CurrentManagedThreadId == _sinkThread;

    private bool RegisterCore(Hotkey hotkey)
    {
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

    private bool UnregisterCore(Hotkey hotkey)
    {
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

    private bool UnregisterAllCore()
    {
        lock (_gate)
        {
            foreach (var id in _registrations.Keys)
                NativeMethods.UnregisterHotKey(_hwnd, id);

            _registrations.Clear();
            return true;
        }
    }

    /// <summary>
    /// Runs whatever the queue holds. On the sink thread from the message loop: once at
    /// start up for work queued while the window was coming up, and on the wake message
    /// after that.
    /// </summary>
    private void RunQueuedWork()
    {
        while (_queue.TryDequeue(out var work))
        {
            var result = work.Kind switch
            {
                WorkKind.Register => RegisterCore(work.Hotkey),
                WorkKind.Unregister => UnregisterCore(work.Hotkey),
                _ => UnregisterAllCore(),
            };

            work.Completion.TrySetResult(result);
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
            ClassName = _className,
        };

        NativeMethods.RegisterClass(ref attributes);

        _hwnd = NativeMethods.CreateWindowEx(
            0,
            _className,
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

        // The sink names its own thread before anything is allowed to wait on the
        // window, so a registration from anywhere else can be told where it travels.
        _sinkThread = Environment.CurrentManagedThreadId;
        _ready.Set();

        if (_hwnd == IntPtr.Zero)
            return;

        // Work queued while the window was coming up had no window to wake a loop that
        // had not parked in GetMessage yet, so the queue is drained once here first.
        RunQueuedWork();

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

        if (message == WmRunQueuedWork)
        {
            RunQueuedWork();
            return IntPtr.Zero;
        }

        if (message == WmClose)
        {
            // The unregistering and the destroying belong to this thread, so a close
            // request is answered here rather than wherever it was posted from.
            UnregisterAllCore();
            NativeMethods.DestroyWindow(hwnd);
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

        // The thread, not this caller, owns the window: the close message asks it to
        // unregister and destroy, and the join waits for that answer with a bound,
        // because the process is on its way out either way. A DestroyWindow from here
        // would be refused with the same cross thread error the hotkey calls hit.
        _disposed = true;

        var handle = _hwnd;
        if (handle != IntPtr.Zero)
        {
            NativeMethods.PostMessage(handle, WmClose, IntPtr.Zero, IntPtr.Zero);
            _thread?.Join(TimeSpan.FromSeconds(2));
        }

        _ready.Dispose();
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
