using Mutantcat.ElectronicPointer.Platform.Overlay;

namespace Mutantcat.ElectronicPointer.App.Shell;

/// <summary>
/// Borrows a window's native handle for the platform layer. Avalonia owns the window and
/// its lifetime; handing the handle over is what lets the overlay logic on Windows, macOS
/// and Linux coerce the same Avalonia window into whatever the host OS needs, without the
/// UI ever learning which one it is talking to.
/// </summary>
public readonly struct AvaloniaHandle : IOverlayWindowTarget
{
    public AvaloniaHandle(nint handle)
    {
        Handle = handle;
    }

    public nint Handle { get; }
}
