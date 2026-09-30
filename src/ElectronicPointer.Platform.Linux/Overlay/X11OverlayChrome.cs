using Mutantcat.ElectronicPointer.Platform.Linux.Native;
using Mutantcat.ElectronicPointer.Platform.Overlay;

namespace Mutantcat.ElectronicPointer.Platform.Linux;

/// <summary>
/// Turns an Avalonia window into a screen-annotation overlay on X11. Avalonia hands over the
/// X window id in its platform handle, and the three overlay behaviours are expressed the way
/// any X11 client has to express them:
///
/// - click-through is the SHAPE extension's input region: clearing it makes the whole window
/// let clicks through, restoring it paints one rectangle the size of the window. X has no
/// per-window "ignore mouse" flag, so this is the only server side answer;
/// - always-on-top and staying out of the task switcher are <c>_NET_WM_STATE</c> client
///   messages to the root window, which is how a client asks an EWMH compliant window
///   manager to move a window into one of those states;
/// - placement is a direct <c>XMoveWindow</c> plus <c>XResizeWindow</c>.
///
/// A window manager is free to ignore an EWMH hint, so a member reports whether the request
/// went out rather than whether the manager complied; that is the most a client can honestly
/// know. Native Wayland gets none of this, and reports the feature as unavailable instead.
/// </summary>
public sealed class X11OverlayChrome : IOverlayChrome
{
    private const int ClientMessage = 33;

    private const string NetWmState = "_NET_WM_STATE";

    private const string NetWmStateAbove = "_NET_WM_STATE_ABOVE";

    private const string NetWmStateSkipTaskbar = "_NET_WM_STATE_SKIP_TASKBAR";

    private const string NetWmStateSkipPager = "_NET_WM_STATE_SKIP_PAGER";

    // The two _NET_WM_STATE actions: 0 removes the state, 1 adds it.
    private const nint StateRemove = 0;

    private const nint StateAdd = 1;

    /// <summary>1 marks the request as coming from a normal application, not from a pager.</summary>
    private const nint SourceApplication = 1;

    private nint _display;

    private nint _window;

    private nint _companion;

    private int _width;

    private int _height;

    private bool _shapeAvailable;

    private bool _clickThrough;

    public bool IsSupported => LinuxSession.IsX11;

    public bool Attach(IOverlayWindowTarget target)
    {
        if (target is not { Handle: not 0 } || !LinuxSession.IsX11)
            return false;

        // Xlib must be told about the other threads before a connection is opened here,
        // because the shortcut service opens its own connection on its own thread.
        if (!X11Runtime.EnsureThreadsInitialized())
            return false;

        _display = X11NativeMethods.XOpenDisplay(LinuxSession.DisplayName);
        if (_display == 0)
            return false;

        _window = target.Handle;

        // Probed without mutating anything, so a compositor without the SHAPE extension
        // simply reports click-through as unavailable for the whole session.
        _shapeAvailable = X11NativeMethods.XQueryExtension(_display, "SHAPE", out _, out _, out _);
        RememberGeometry();

        SendState(StateAdd, NetWmStateAbove);
        return true;
    }

    /// <summary>
    /// A companion, asked to stay above the canvases. A window manager is free to ignore
    /// an EWMH hint, so the request simply goes out; the companion is re-raised whenever a
    /// canvas is rebuilt, because a manager that honours the hint honours it for whichever
    /// window asked last.
    ///
    /// The role is not consulted: the request is a set of window states, and X draws no
    /// distinction between a palette that must not take focus and a dialog that must.
    /// </summary>
    public bool AttachCompanion(IOverlayWindowTarget target, CompanionRole role)
    {
        if (!IsSupported || target is not { Handle: not 0 })
            return false;

        // The companion owns its own chrome instance, so it is the one that has to open the
        // connection rather than borrow the canvas's.
        if (!EnsureDisplay())
            return false;

        _companion = target.Handle;
        SendState(StateAdd, NetWmStateAbove, _companion);
        SendState(StateAdd, NetWmStateSkipTaskbar, _companion);
        SendState(StateAdd, NetWmStateSkipPager, _companion);
        return true;
    }

    public void DetachCompanion()
    {
        if (_companion == 0)
            return;

        if (_display != 0)
        {
            SendState(StateRemove, NetWmStateAbove, _companion);
            SendState(StateRemove, NetWmStateSkipTaskbar, _companion);
            SendState(StateRemove, NetWmStateSkipPager, _companion);
        }

        _companion = 0;

        // A companion owns no canvas, so nothing else is ever going to close the connection
        // it opened. Closing it here is what keeps a dialog that is opened and closed again
        // and again from leaving one display connection behind every time.
        if (_window == 0 && _display != 0)
        {
            _ = X11NativeMethods.XCloseDisplay(_display);
            _display = 0;
        }
    }

    public void Detach()
    {
        if (_display == 0)
            return;

        if (IsSupported)
        {
            SendState(StateRemove, NetWmStateAbove);
            SendState(StateRemove, NetWmStateSkipTaskbar);
            SendState(StateRemove, NetWmStateSkipPager);
            ApplyClickThrough(false);
        }

        _window = 0;
        _ = X11NativeMethods.XCloseDisplay(_display);
        _display = 0;
    }

    public bool SetClickThrough(bool enabled)
    {
        if (_display == 0 || !_shapeAvailable)
            return false;

        ApplyClickThrough(enabled);
        return true;
    }

    public bool SetAlwaysOnTop(bool enabled)
    {
        if (_display == 0)
            return false;

        SendState(enabled ? StateAdd : StateRemove, NetWmStateAbove);
        return true;
    }

    public bool SetHiddenFromSwitcher(bool hidden)
    {
        if (_display == 0)
            return false;

        var action = hidden ? StateAdd : StateRemove;
        SendState(action, NetWmStateSkipTaskbar);
        SendState(action, NetWmStateSkipPager);
        return true;
    }

    /// <summary>
    /// Moves and resizes the overlay to cover one display exactly. Used on multi-monitor
    /// setups, where a window manager can otherwise offset a maximised window by the size of
    /// a decoration it chose to add.
    /// </summary>
    public bool Place(int x, int y, int width, int height)
    {
        if (_display == 0 || _window == 0)
            return false;

        _width = Math.Max(1, width);
        _height = Math.Max(1, height);

        X11NativeMethods.XMoveWindow(_display, _window, x, y);
        X11NativeMethods.XResizeWindow(_display, _window, (uint)_width, (uint)_height);
        X11NativeMethods.XSync(_display, false);

        // The input region has to follow the new geometry or click-through stops matching.
        ApplyClickThrough(_clickThrough);
        return true;
    }

    /// <summary>
    /// Clears the input region so clicks fall through to what is behind the overlay, or
    /// restores a region matching the whole window when the pen is back in play.
    /// </summary>
    private void ApplyClickThrough(bool clickThrough)
    {
        _clickThrough = clickThrough;

        if (!_shapeAvailable || _display == 0 || _window == 0)
            return;

        X11NativeMethods.XRectangle[]? rectangles = null;
        var count = 0;

        if (!clickThrough && _width > 0 && _height > 0)
        {
            rectangles = new[]
            {
                new X11NativeMethods.XRectangle
                {
                    X = 0,
                    Y = 0,
                    Width = (ushort)Math.Min(_width, ushort.MaxValue),
                    Height = (ushort)Math.Min(_height, ushort.MaxValue),
                },
            };
            count = 1;
        }

        // A null list with a count of zero clears the region entirely, which is what makes
        // the overlay transparent to the pointer.
        X11NativeMethods.XShapeCombineRectangles(
            _display,
            _window,
            X11NativeMethods.ShapeInput,
            0,
            0,
            rectangles,
            count,
            X11NativeMethods.ShapeSet,
            X11NativeMethods.ShapeUnsorted);

        X11NativeMethods.XSync(_display, false);
    }

    private void RememberGeometry()
    {
        if (_display == 0 || _window == 0)
            return;

        X11NativeMethods.XGetGeometry(
            _display,
            _window,
            out _,
            out _,
            out _,
            out var width,
            out var height,
            out _,
            out _);

        _width = (int)Math.Min(width, int.MaxValue);
        _height = (int)Math.Min(height, int.MaxValue);
    }

    /// <summary>
    /// Opens the connection this instance needs when it does not have one yet. A companion
    /// is bound to a chrome of its own, so it cannot rely on a canvas having opened the
    /// display first.
    /// </summary>
    private bool EnsureDisplay()
    {
        if (_display != 0)
            return true;

        if (!X11Runtime.EnsureThreadsInitialized())
            return false;

        _display = X11NativeMethods.XOpenDisplay(LinuxSession.DisplayName);
        return _display != 0;
    }

    private void SendState(nint action, string atomName)
    {
        SendState(action, atomName, _window);
    }

    private void SendState(nint action, string atomName, nint window)
    {
        if (_display == 0 || window == 0)
            return;

        var messageType = X11NativeMethods.XInternAtom(_display, NetWmState, false);
        var atom = X11NativeMethods.XInternAtom(_display, atomName, false);
        if (messageType == 0 || atom == 0)
            return;

        var message = new X11NativeMethods.XClientMessageEvent
        {
            Type = ClientMessage,
            Window = window,
            MessageType = messageType,
            Format = 32,
            Data0 = action,
            Data1 = atom,
            Data2 = 0,
            Data3 = SourceApplication,
            Data4 = 0,
        };

        var mask = X11NativeMethods.SubstructureNotifyMask | X11NativeMethods.SubstructureRedirectMask;
        X11NativeMethods.XSendEvent(_display, X11NativeMethods.XDefaultRootWindow(_display), false, mask, ref message);
        X11NativeMethods.XSync(_display, false);
    }
}
