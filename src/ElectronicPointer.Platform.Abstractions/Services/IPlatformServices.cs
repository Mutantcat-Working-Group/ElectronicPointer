using Mutantcat.ElectronicPointer.Platform.Capture;
using Mutantcat.ElectronicPointer.Platform.Displays;
using Mutantcat.ElectronicPointer.Platform.Input;
using Mutantcat.ElectronicPointer.Platform.Overlay;
using Mutantcat.ElectronicPointer.Platform.Presentation;
using Mutantcat.ElectronicPointer.Platform.Recognition;
using Mutantcat.ElectronicPointer.Platform.Startup;

namespace Mutantcat.ElectronicPointer.Platform.Services;

/// <summary>
/// Everything the app needs from the host operating system. Built once at startup by the
/// platform assembly that matches the current OS, and consumed through this interface only,
/// so the shared UI code never learns which platform it is running on.
/// </summary>
public interface IPlatformServices : IDisposable
{
    PlatformCapabilities Capabilities { get; }

    string PlatformName { get; }

    /// <summary>
    /// A fresh window chrome per overlay surface. Every chrome instance holds one native
    /// window, and a desk has several canvases at once, so the platform hands them out
    /// rather than sharing a single instance whose target the next canvas would overwrite.
    /// </summary>
    IOverlayChrome CreateOverlayChrome();

    IGlobalHotkeyService Hotkeys { get; }

    IScreenCaptureService ScreenCapture { get; }

    IPresentationBridge Presentation { get; }

    IInkRecognizer Recognizer { get; }

    IAutoStartService AutoStart { get; }

    /// <summary>Best effort display list, used to place and capture overlays.</summary>
    IReadOnlyList<DisplayInfo> Displays { get; }
}
