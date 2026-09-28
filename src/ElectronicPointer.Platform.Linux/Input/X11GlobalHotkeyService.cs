using System.Runtime.InteropServices;
using Mutantcat.ElectronicPointer.Core.Input;
using Mutantcat.ElectronicPointer.Platform.Input;
using Mutantcat.ElectronicPointer.Platform.Linux.Native;

namespace Mutantcat.ElectronicPointer.Platform.Linux;

/// <summary>
/// Global shortcuts through <c>XGrabKey</c>. Registered keys are delivered to the grabbing
/// client whatever application is focused, which is what lets the toolbar swap tools while a
/// slideshow owns the screen.
///
/// Two details are what make it actually work rather than work on a good day. Caps Lock and
/// Num Lock are separate modifier bits, so a combination registered while a lock is off does
/// not fire when it is on: every grab is registered in all four lock states. And the loop
/// blocks in <c>poll</c> over both the X socket and a pipe, so shutdown wakes the thread
/// instead of leaving it parked inside X forever.
///
/// Events arrive on the loop thread, so <see cref="Pressed"/> is raised there; the app marshals
/// onto the UI thread before touching any board state.
/// </summary>
public sealed class X11GlobalHotkeyService : IGlobalHotkeyService, IDisposable
{
    private const int KeyPress = 2;

    private readonly HashSet<Hotkey> _registered = new();

    private readonly object Gate = new();

    private readonly int[] _wakePipe = { -1, -1 };

    private nint _display;

    private nint _root;

    private Thread? _loop;

    private bool _running;

    private bool _disposed;

    public bool IsSupported => LinuxSession.IsX11;

    public event EventHandler<Hotkey>? Pressed;

    public bool Register(Hotkey hotkey)
    {
        if (!LinuxSession.IsX11)
            return false;

        if (LinuxKeyCode.ToXKeyCode(hotkey.Key) == 0)
            return false;

        lock (Gate)
        {
            if (_disposed)
                return false;

            StartLoop();
            if (_display == 0)
                return false;

            return Grab(hotkey);
        }
    }

    public bool Unregister(Hotkey hotkey)
    {
        lock (Gate)
        {
            if (_display == 0 || _root == 0)
                return false;

            var keyCode = LinuxKeyCode.ToXKeyCode(hotkey.Key);
            var mask = LinuxKeyCode.ToModifierMask(hotkey.Modifiers);

            // A key with no evdev number can never have been grabbed, so refuse it here
            // rather than reporting success for a key that will not fire.
            if (keyCode == 0)
                return false;

            foreach (var state in LinuxKeyCode.ExpandModifierStates(mask))
                _ = X11NativeMethods.XUngrabKey(_display, keyCode, state, _root);

            _registered.Remove(hotkey);
            X11NativeMethods.XSync(_display, false);
            return true;
        }
    }

    public void UnregisterAll()
    {
        lock (Gate)
        {
            if (_display == 0 || _root == 0)
                return;

            foreach (var hotkey in _registered.ToList())
            {
                var keyCode = LinuxKeyCode.ToXKeyCode(hotkey.Key);
                var mask = LinuxKeyCode.ToModifierMask(hotkey.Modifiers);

                if (keyCode == 0)
                    continue;

                foreach (var state in LinuxKeyCode.ExpandModifierStates(mask))
                    _ = X11NativeMethods.XUngrabKey(_display, keyCode, state, _root);
            }

            _registered.Clear();

            // A blanket ungrab does not exist for keys, so the keyboard itself is released
            // too: without it, a cancelled grab can keep eating the modifier keys elsewhere.
            _ = X11NativeMethods.XUngrabKeyboard(_display, _root);
            X11NativeMethods.XSync(_display, false);
        }
    }

    public void Dispose()
    {
        lock (Gate)
        {
            if (_disposed)
                return;

        _disposed = true;
        }

        // StopLoop reads the pipe handle and closes the display, both of which are safe to do
        // outside the lock: the loop thread only touches the connection through the loop.
        StopLoop();
    }

    private bool Grab(Hotkey hotkey)
    {
        var keyCode = LinuxKeyCode.ToXKeyCode(hotkey.Key);
        var mask = LinuxKeyCode.ToModifierMask(hotkey.Modifiers);

        foreach (var state in LinuxKeyCode.ExpandModifierStates(mask))
        {
            // GrabModeAsync on both ends, so a grabbed key is delivered to this client and
            // still reaches the application that had focus.
            var result = X11NativeMethods.XGrabKey(
                _display,
                keyCode,
                state,
                _root,
                false,
                (int)X11NativeMethods.GrabModeAsync,
                (int)X11NativeMethods.GrabModeAsync);

            if (result != 0)
                return false;
        }

        _registered.Add(hotkey);
        X11NativeMethods.XSync(_display, false);
        return true;
    }

    private void StartLoop()
    {
        if (_display != 0)
            return;

        if (!X11Runtime.EnsureThreadsInitialized())
            return;

        if (X11NativeMethods.pipe(_wakePipe) != 0)
        {
            _wakePipe[0] = -1;
            _wakePipe[1] = -1;
            return;
        }

        _display = X11NativeMethods.XOpenDisplay(LinuxSession.DisplayName);
        if (_display == 0)
        {
            ClosePipe();
            return;
        }

        _root = X11NativeMethods.XDefaultRootWindow(_display);
        if (_root == 0)
        {
            _ = X11NativeMethods.XCloseDisplay(_display);
            _display = 0;
            ClosePipe();
            return;
        }

        // Key events have to be selected on the grab window, otherwise the grab fires and the
        // event is dropped: X only reports what a client asked for.
        X11NativeMethods.XSelectInput(_display, _root, X11NativeMethods.KeyPressMask);
        X11NativeMethods.XSync(_display, false);

        _running = true;
        _loop = new Thread(Loop)
        {
            IsBackground = true,
            Name = "ElectronicPointer hotkey loop",
        };

        _loop.Start();
    }

    private void Loop()
    {
        var socket = X11NativeMethods.XConnectionNumber(_display);

        var descriptors = new[]
        {
            new X11NativeMethods.PollFd { Fd = socket, Events = X11NativeMethods.PollIn },
            new X11NativeMethods.PollFd { Fd = _wakePipe[0], Events = X11NativeMethods.PollIn },
        };

        var stop = new byte[1];

        while (Volatile.Read(ref _running))
        {
            unsafe
            {
                fixed (X11NativeMethods.PollFd* pointer = descriptors)
                {
                    var ready = X11NativeMethods.poll(pointer, 2, -1);
                    if (ready <= 0)
                        continue;
                }
            }

            // Draining the X queue before checking the pipe keeps a burst of keys in order.
            DrainEvents();

            if ((descriptors[1].Revents & X11NativeMethods.PollIn) != 0)
                break;
        }

        _ = X11NativeMethods.read(_wakePipe[0], stop, stop.Length);
    }

    private void DrainEvents()
    {
        while (X11NativeMethods.XPending(_display) > 0)
        {
            var message = default(X11NativeMethods.XEvent);
            X11NativeMethods.XNextEvent(_display, ref message);

            if (message.Type != KeyPress || message.KeyCode == 0)
                continue;

            var key = LinuxKeyCode.FromXKeyCode(message.KeyCode);
            if (key == KeyCode.None)
                continue;

            var hotkey = new Hotkey(LinuxKeyCode.FromEventState(message.State), key);

            // A modifier arriving on its own races the key that follows it, so only full
            // combinations are forwarded.
            if (_registered.Contains(hotkey))
                Pressed?.Invoke(this, hotkey);
        }
    }

    private void StopLoop()
    {
        Volatile.Write(ref _running, false);

        if (_wakePipe[1] >= 0)
        {
            var signal = new byte[] { 1 };
            _ = X11NativeMethods.write(_wakePipe[1], signal, signal.Length);
        }

        _loop?.Join(TimeSpan.FromMilliseconds(500));

        ClosePipe();

        if (_display != 0)
        {
            _ = X11NativeMethods.XCloseDisplay(_display);
            _display = 0;
            _root = 0;
        }

        _registered.Clear();
    }

    private void ClosePipe()
    {
        if (_wakePipe[0] >= 0)
            _ = X11NativeMethods.close(_wakePipe[0]);

        if (_wakePipe[1] >= 0)
            _ = X11NativeMethods.close(_wakePipe[1]);

        _wakePipe[0] = -1;
        _wakePipe[1] = -1;
    }
}
