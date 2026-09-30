using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Mutantcat.ElectronicPointer.App.Session;
using Mutantcat.ElectronicPointer.App.Shell;
using Mutantcat.ElectronicPointer;
using Mutantcat.ElectronicPointer.Core.Geometry;
using Mutantcat.ElectronicPointer.Platform.Overlay;
using Mutantcat.ElectronicPointer.Platform.Services;

namespace Mutantcat.ElectronicPointer.App.Views;

/// <summary>
/// One transparent, frameless, always-on-top window covering a single screen. Ink is drawn
/// straight onto the desktop with no whiteboard page behind it, which is what a lecturer
/// actually wants while a slideshow is running.
///
/// The window knows nothing about the host OS. Everything the OS cannot express through
/// Avalonia's own window model is pushed into <see cref="IOverlayChrome"/> once the window
/// exists and its native handle can be handed over.
/// </summary>
public sealed class OverlayWindow : Window
{
    private readonly OverlayCanvas _canvas = new();
    private IOverlayChrome? _chrome;

    public OverlayWindow(Screen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        var bounds = screen.Bounds;

        // Board coordinates are device independent pixels of the virtual desktop, so this
        // surface's own origin is its screen's top-left corner expressed in those units.
        var unitPerPixel = screen.Scaling > 0 ? screen.Scaling : 1d;
        Origin = new Vec2(bounds.X / unitPerPixel, bounds.Y / unitPerPixel);

        SystemDecorations = SystemDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Title = AppIdentity.GetDisplayName();

        Width = bounds.Size.ToSize(unitPerPixel).Width;
        Height = bounds.Size.ToSize(unitPerPixel).Height;
        Position = bounds.Position;
        Content = _canvas;
    }

    /// <summary>Where this surface's top-left corner sits on the shared board.</summary>
    public Vec2 Origin { get; }

    public OverlayCanvas Canvas => _canvas;

    public void Bind(BoardSession session) => _canvas.Bind(session, Origin);

    /// <summary>
    /// Called once the window exists and its native handle is available. Everything here
    /// needs a real window, which is why it cannot run from the constructor.
    /// </summary>
    public void AttachChrome(IPlatformServices platform, BoardSession session)
    {
        // One chrome per surface: a shared instance would have its native window replaced
        // by the next canvas, and the canvas that closed first would then detach a window
        // it never belonged to.
        _chrome = platform.CreateOverlayChrome();
        if (TryGetPlatformHandle() is not { } handle)
            return;

        if (!_chrome.Attach(new AvaloniaHandle(handle.Handle)))
            return;

        _chrome.SetAlwaysOnTop(true);
        _chrome.SetHiddenFromSwitcher(true);
        _chrome.SetClickThrough(session.PassThrough);
    }

    /// <summary>Re-reads click-through, for instance when the user toggles the pen.</summary>
    public void ApplyClickThrough(bool clickThrough) => _chrome?.SetClickThrough(clickThrough);

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _canvas.Release();
        _chrome?.Detach();
        _chrome = null;
    }
}
