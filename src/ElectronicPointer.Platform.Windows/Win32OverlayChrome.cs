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
/// The tool palette is a companion, not a canvas: it lives in the same top-most group but
/// is ordered after every canvas, so a palette that opened first is not buried under the
/// ink it drives.
///
/// Windows 11 rounds the corners of every frameless window on its own, while the palette
/// draws an eight pixel radius of its own. The two cannot both win, so the system's
/// rounding is switched off on both windows and the application's stays the only one. The
/// switch is followed by a frame change whenever one of these calls moves or re-styles the
/// window, and a frame change makes the window manager settle the shape again, so the
/// preference is handed over after the change as well as before it.
/// </summary>
public sealed class Win32OverlayChrome : IOverlayChrome
{
    private IOverlayWindowTarget? _target;

    private IOverlayWindowTarget? _companion;

    private int _originalStyle;

    public bool IsSupported => true;

    public bool Attach(IOverlayWindowTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        _target = target;
        _originalStyle = NativeMethods.GetWindowLong(target.Handle, NativeMethods.GwlExStyle);
        DisableSystemRounding(target.Handle);
        return NativeMethods.IsWindow(target.Handle);
    }

    /// <summary>
    /// Puts a companion above every canvas. Being top-most is not enough on its own,
    /// because the canvases are top-most too and are opened afterwards, so a companion is
    /// ordered after them and has to be re-ordered whenever a canvas is rebuilt. The call
    /// is idempotent for exactly that reason.
    ///
    /// The role is left unread on purpose. Windows expresses "above the canvases" as a
    /// z-order insertion that neither activates nor blocks activating the window, so a
    /// palette that must never steal focus and a dialog that must take it need the same
    /// call and get different outcomes from the activation they already have.
    /// </summary>
    public bool AttachCompanion(IOverlayWindowTarget target, CompanionRole role)
    {
        ArgumentNullException.ThrowIfNull(target);

        var handle = target.Handle;
        if (handle == IntPtr.Zero || !NativeMethods.IsWindow(handle))
            return false;

        _companion = target;

        var flags = NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate;
        var lifted = NativeMethods.SetWindowPos(handle, NativeMethods.HwndTopmost, 0, 0, 0, 0, flags);
        // After the lift rather than before it. Changing the z-order of a window the
        // window manager is already composing makes it redraw the frame, and a redrawn frame
        // re-settles which corners are round, so a preference handed over first is the one
        // that gets answered with instead of the one that gets carried out.
        DisableSystemRounding(handle);
        return lifted;
    }

    public void DetachCompanion() => _companion = null;

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
        var ordered = NativeMethods.SetWindowPos(target.Value, insertAfter, 0, 0, 0, 0, flags);
        DisableSystemRounding(target.Value);
        return ordered;
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
        var styled = ApplyStyles(SetWindowPosFrameChanged);
        // The style change redraws the frame, which re-settles the corner shape, and the
        // canvas reaches this path from the same opening that switched the rounding off in
        // the first place: click-through and hiding from the switcher both land after it.
        DisableSystemRounding(target.Value);
        return styled;
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

    /// <summary>
    /// Asks the window manager to leave the corners alone. Windows 11 gives a frameless
    /// window a rounded pair of corners whether anyone asked for them or not, which is what
    /// makes a palette that draws its own corners look lopsided; versions of Windows
    /// without the preference answer with a failure, which is why nothing is made of it.
    /// </summary>
    private static void DisableSystemRounding(IntPtr handle)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return;

        var preference = NativeMethods.DwmwcpDoNotRound;
        _ = NativeMethods.DwmSetWindowAttribute(
            handle,
            NativeMethods.DwmwaWindowCornerPreference,
            ref preference,
            sizeof(int));
    }
}
