using Mutantcat.ElectronicPointer.Platform.Overlay;
using System.Runtime.InteropServices;
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
/// Windows 11 settles the corners of a frameless window on its own schedule. It rounds a
/// window whether anyone asked, answers an asking late, and settles the shape again on
/// every re-composition that follows, which is how a palette that draws an eight pixel
/// radius of its own ends up with corners that disagree with each other. A companion is
/// therefore asked for the host's own small radius, which lands inside the corner the
/// card has already left transparent, so the hand the host has in it and the one the card
/// draws say the same thing rather than two different ones.
///
/// A preference is still only a preference, and a host that answers one on its own
/// schedule is how a palette ends up with corners that disagree with each other. A
/// companion is therefore also cut to the shape its card draws, which is the one
/// instruction about a window's corners that is carried out rather than answered.
/// </summary>
public sealed class Win32OverlayChrome : IOverlayChrome
{
    // Either corner preference is handed over again after a frame change, since a frame
    // change makes the window manager settle the corner shape from scratch and a
    // preference given before one is the one that gets answered with.
    private IOverlayWindowTarget? _target;

    private IOverlayWindowTarget? _companion;

    private int _originalStyle;

    public bool IsSupported => true;

    public bool Attach(IOverlayWindowTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        _target = target;
        _originalStyle = NativeMethods.GetWindowLong(target.Handle, NativeMethods.GwlExStyle);
        // The canvas covers a whole display edge to edge, so a host rounding it would eat
        // a corner of ink that belongs there. It is the companion that wants a host's
        // radius, because a companion draws a radius of its own.
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
        AskForSmallRounding(handle);
        // The preference asks the host for the corner it already has; the cut puts the
        // shape in place whatever the host made of that asking.
        RoundWindowShape(handle);
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
    /// <summary>
    /// Asks the host to leave the corners alone. Only the canvas is asked this: it covers a
    /// whole display edge to edge, so a host rounding it would eat a corner of ink that
    /// belongs there. Versions of Windows without the preference answer with a failure,
    /// which is why nothing is made of the answer.
    /// </summary>
    private static void DisableSystemRounding(IntPtr handle)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return;

        Ask(handle, NativeMethods.DwmwcpDoNotRound);
    }

    /// <summary>
    /// Asks the host for its own small corner radius. A companion card draws a radius wider
    /// than the host's small one, so a host that grants the asking cuts air inside a corner
    /// the card has already left transparent, and the two agree instead of disagreeing.
    /// Asking is worth it on its own: a host that has been asked for a corner is a host
    /// that has decided there is nothing to add of its own on top, which is the difference
    /// between four corners of the application's and four of nobody's in particular.
    /// </summary>
    private static void AskForSmallRounding(IntPtr handle)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return;

        Ask(handle, NativeMethods.DwmwcpRoundSmall);
    }

    private static void Ask(IntPtr handle, int preference)
    {
        var value = preference;
        _ = NativeMethods.DwmSetWindowAttribute(
            handle,
            NativeMethods.DwmwaWindowCornerPreference,
            ref value,
            sizeof(int));
    }

    /// <summary>
    /// Cuts the window to the round rectangle the card inside it draws. Every other part of
    /// a frameless window's corners is a request: Windows 11 rounds them whether anyone
    /// asked or not, answers the asking not to on its own schedule, and settles the shape
    /// again on every re-composition, which is how a window ends up with three corners of
    /// the application's and one of the host's. A region is carried out rather than
    /// answered, so it is applied on top of the request and settles all four corners at
    /// once, whatever the host had made of the card underneath them.
    ///
    /// The cut is a pixel wider than the card on purpose. The region is a hard edge and
    /// the card's corner is anti-aliased, so a region cut to the card exactly would shave
    /// the card's own anti-aliasing away and leave every corner a fraction sharper than
    /// the application drew it. Asked for one device pixel more it sits outside the card
    /// and clips nothing but the corners a host added.
    /// </summary>
    private static void RoundWindowShape(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !NativeMethods.IsWindow(handle))
            return;

        if (!NativeMethods.GetClientRect(handle, out var client))
            return;

        var width = client.Right - client.Left;
        var height = client.Bottom - client.Top;
        if (width <= 0 || height <= 0)
            return;

        var radius = ShapeRadius(
            CompanionShape.CornerRadius,
            NativeMethods.GetDpiForWindow(handle),
            width,
            height);

        if (radius <= 0)
            return;

        var region = NativeMethods.CreateRoundRectRgn(0, 0, width, height, radius * 2, radius * 2);
        if (region == IntPtr.Zero)
            return;

        // The window owns the region once it has taken it, so the handle is released only
        // when the window refused it. A window that had no region of its own answers with
        // nothing at all and no error, which is the ordinary answer rather than a failure.
        if (NativeMethods.SetWindowRgn(handle, region, true) == 0 &&
            Marshal.GetLastWin32Error() != 0)
        {
            _ = NativeMethods.DeleteObject(region);
        }
    }

    /// <summary>
    /// The corner radius the shape is cut at, in the device pixels a region is measured in.
    /// The card draws in the window's layout units and the window's own scale is the only
    /// bridge between the two, so the scale is crossed here rather than assumed: a desk at
    /// 125% gives the card a quarter again as wide a corner, and a region cut at the layout
    /// number would leave a corner square on one desk and twice over on another.
    /// </summary>
    internal static int ShapeRadius(int cornerRadius, uint dpi, int width, int height)
    {
        if (cornerRadius <= 0)
            return 0;

        var scale = dpi > 0 ? dpi / 96d : 1d;
        var radius = (int)Math.Round(cornerRadius * scale) + 1;

        // A window too small for the radius is cut to half of its shorter side rather than
        // to nothing, and a window shorter than two pixels still gets a corner at all.
        return Math.Clamp(radius, 1, Math.Max(1, Math.Min(width, height) / 2));
    }
}
