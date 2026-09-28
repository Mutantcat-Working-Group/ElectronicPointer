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

    IOverlayChrome Overlay { get; }

    IGlobalHotkeyService Hotkeys { get; }

    IScreenCaptureService ScreenCapture { get; }

    IPresentationBridge Presentation { get; }

    IHandwritingRecognizer Recognizer { get; }

    IAutoStartService AutoStart { get; }

    /// <summary>Best effort display list, used to place and capture overlays.</summary>
    IReadOnlyList<DisplayInfo> Displays { get; }
}
