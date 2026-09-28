using System.Runtime.InteropServices;
using Mutantcat.ElectronicPointer.Core.Input;
using Mutantcat.ElectronicPointer.Platform.Input;
using Mutantcat.ElectronicPointer.Platform.MacOS.Native;

namespace Mutantcat.ElectronicPointer.Platform.MacOS;

/// <summary>
/// System-wide shortcuts through a CoreGraphics event tap on a dedicated run loop thread.
/// The tap is passive, so the key still reaches whichever app the user is presenting in: the
/// event is observed, matched against the registry, raised as <see cref="Pressed"/>, and
/// passed on untouched.
///
/// macOS gates HID taps behind the Accessibility permission. Without it the tap can be
/// created but never receives events, which looks like a silent failure, so
/// <see cref="RequiresAccessibilityPermission"/> lets the UI ask for it in a way the user
/// can act on.
/// </summary>
public sealed class MacOSGlobalHotkeyService : IGlobalHotkeyService, IDisposable
{
    private readonly Lock _sync = new();
    private readonly HashSet<Hotkey> _registered = new();
    private readonly nint _callback;
    private readonly GCHandle _handle;
    private int _started;
    private nint _tap;
    private bool _disposed;

    public MacOSGlobalHotkeyService()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        _handle = GCHandle.Alloc(this);
        _callback = Marshal.GetFunctionPointerForDelegate(new MacOSNativeMethods.CGEventTapCallBack(OnEvent));
    }

    public bool IsSupported => _callback != 0;

    /// <summary>True when the Accessibility permission gate on this Mac applies.</summary>
    public bool RequiresAccessibilityPermission => IsSupported && OperatingSystem.IsMacOSVersionAtLeast(10, 14);

    /// <summary>True once the tap thread is up; false while permission is missing.</summary>
    public bool IsTapEnabled
    {
        get
        {
            if (_tap == 0)
                return false;

            try
            {
                return MacOSNativeMethods.CGEventTapIsEnabled(_tap);
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }
    }

    public event EventHandler<Hotkey>? Pressed;

    public bool Register(Hotkey hotkey)
    {
        if (!IsSupported)
            return false;

        bool start;
        lock (_sync)
        {
            if (!_registered.Add(hotkey))
                return true;

            start = Interlocked.Exchange(ref _started, 1) == 0;
        }

        if (start)
            StartTap();

        return true;
    }

    public bool Unregister(Hotkey hotkey)
    {
        lock (_sync)
            return _registered.Remove(hotkey);
    }

    public void UnregisterAll()
    {
        lock (_sync)
            _registered.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopTap();
    }

    private void StartTap()
    {
        var thread = new Thread(EventLoop)
        {
            IsBackground = true,
            Name = "ElectronicPointer.EventTap",
        };

        thread.Start();
    }

    private void EventLoop()
    {
        try
        {
            _tap = MacOSNativeMethods.CGEventTapCreate(
                MacOSNativeMethods.HidEventTap,
                MacOSNativeMethods.HeadInsertEventTap,
                MacOSNativeMethods.EventTapOptionDefault,
                MacOSNativeMethods.KeyboardEventMask,
                _callback,
                GCHandle.ToIntPtr(_handle));

            if (_tap == 0)
                return;

            MacOSNativeMethods.CGEventTapEnable(_tap, true);

            var source = MacOSNativeMethods.CFMachPortCreateRunLoopSource(0, _tap, 0);
            if (source != 0)
                MacOSNativeMethods.CFRunLoopAddSource(MacOSNativeMethods.CFRunLoopGetCurrent(), source, 0);

            MacOSNativeMethods.CFRunLoopRun();
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    private void StopTap()
    {
        if (_tap == 0)
            return;

        try
        {
            MacOSNativeMethods.CGEventTapEnable(_tap, false);
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    /// <summary>
    /// Invoked by CoreGraphics on the tap thread. Returning the event unchanged is what keeps
     /// the keystroke flowing to the application the user is presenting in.
    /// </summary>
    private static nint OnEvent(nint proxy, uint type, nint @event, nint userInfo)
    {
        try
        {
            if (type != MacOSNativeMethods.EventTypeKeyDown)
                return @event;

            var handle = GCHandle.FromIntPtr(userInfo);
            if (!handle.IsAllocated || handle.Target is not MacOSGlobalHotkeyService service)
                return @event;

            var keycode = MacOSNativeMethods.CGEventGetIntegerValueField(
                @event,
                MacOSNativeMethods.KeyboardEventKeycode);

            var match = ToHotkey((int)keycode, MacOSNativeMethods.CGEventGetFlags(@event));
            if (match is not { } hotkey)
                return @event;

            lock (service._sync)
            {
                if (service._registered.Contains(hotkey))
                    service.Pressed?.Invoke(service, hotkey);
            }
        }
        catch
        {
            // A tap that throws would be torn down by CoreGraphics, taking every shortcut with it.
        }

        return @event;
    }

    private static Hotkey? ToHotkey(int keycode, ulong flags)
    {
        var key = MacOSKeyCode.FromVirtualKeyCode(keycode);
        if (key == KeyCode.None)
            return null;

        var modifiers = HotkeyModifiers.None;
        if ((flags & MacOSNativeMethods.EventFlagMaskControl) != 0)
            modifiers |= HotkeyModifiers.Control;

        if ((flags & MacOSNativeMethods.EventFlagMaskShift) != 0)
            modifiers |= HotkeyModifiers.Shift;

        if ((flags & MacOSNativeMethods.EventFlagMaskAlternate) != 0)
            modifiers |= HotkeyModifiers.Alt;

        if ((flags & MacOSNativeMethods.EventFlagMaskCommand) != 0)
            modifiers |= HotkeyModifiers.Command;

        return new Hotkey(modifiers, key);
    }
}
