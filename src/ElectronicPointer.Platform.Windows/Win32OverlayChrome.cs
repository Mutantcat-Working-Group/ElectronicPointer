using Mutantcat.ElectronicPointer.Platform.Overlay;
using System.ComponentModel;

namespace Mutantcat.ElectronicPointer.Platform.Windows;

/// <summary>
/// Window behaviours for the overlay on Windows. The overlay is a normal top level window
/// owned by Avalonia; this class only adds the extended styles the OS needs:
///
/// - <see cref="WsExTransparent"/> lets pointer input fall through to the desktop behind.
/// - <see cref="WsExTopmost"/> keeps the ink above other applications, including a running
///   slideshow.
/// - <see cref="WsExToolWindow"/> keeps the overlay out of the task bar and Alt+Tab.
///
/// The toolbar lives inside the same window, so pass-through mode also hides the toolbar:
/// a click-through window cannot be clicked, buttons included.
/// </summary>
public sealed class Win32OverlayChrome : IOverlayChrome
{
    private IOverlayWindowTarget? _target;
    private int _originalStyle;

    public bool IsSupported => true;

    public bool Attach(IOverlayWindowTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        _target = target;
        _originalStyle = NativeMethods.GetWindowLong(target.Handle, NativeMethods.GwlExStyle);
        return NativeMethods.IsWindow(target.Handle);
    }

    public void Detach()
    {
        if (_target is not null && _target.Handle != IntPtr.Zero && NativeMethods.IsWindow(_target.Handle))
        {
            try
            {
                NativeMethods.SetWindowLong(_target.Handle, NativeMethods.GwlExStyle, _originalStyle);
            ApplyStyles(SetWindowPosFrameChanged);
            }
            catch (Win32Exception)
            {
                // The window is gone; nothing to restore.
            }
        }

        _target = null;
    }

    public bool SetClickThrough(bool enabled) => Toggle(NativeMethods.WsExTransparent, enabled);

    public bool SetAlwaysOnTop(bool enabled)
    {
        var target = ValidTarget();
        if (target is null)
            return false;

        var flags = NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate;
        var insertAfter = enabled ? NativeMethods.HwndTopmost : NativeMethods.HwndNoTopmost;
        return NativeMethods.SetWindowPos(target.Value, insertAfter, 0, 0, 0, 0, flags);
    }

    public bool SetHiddenFromSwitcher(bool hidden) => Toggle(NativeMethods.WsExToolWindow, hidden);

    private const uint SetWindowPosFrameChanged = NativeMethods.SwpNoMove
        | NativeMethods.SwpNoSize
        | NativeMethods.SwpNoZOrder
        | NativeMethods.SwpNoActivate
        | NativeMethods.SwpFrameChanged;

    private bool Toggle(int style, bool enabled)
    {
        var target = ValidTarget();
        if (target is null)
            return false;

        var current = NativeMethods.GetWindowLong(target.Value, NativeMethods.GwlExStyle);
        var updated = enabled ? (current | style) : (current & ~style);
        if (updated == current)
            return true;

        NativeMethods.SetWindowLong(target.Value, NativeMethods.GwlExStyle, updated);
        return ApplyStyles(SetWindowPosFrameChanged);
    }

    private bool ApplyStyles(uint flags)
    {
        var target = ValidTarget();
        if (target is null)
            return false;

        return NativeMethods.SetWindowPos(target.Value, IntPtr.Zero, 0, 0, 0, 0, flags);
    }

    private IntPtr? ValidTarget()
    {
        var handle = _target?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero || !NativeMethods.IsWindow(handle))
            return null;

        return handle;
    }
}
