using Mutantcat.ElectronicPointer.Core.Input;
using Mutantcat.ElectronicPointer.Core.Geometry;
using Mutantcat.ElectronicPointer.Platform.Capture;
using Mutantcat.ElectronicPointer.Platform.Displays;
using Mutantcat.ElectronicPointer.Platform.Input;
using Mutantcat.ElectronicPointer.Platform.Overlay;
using Mutantcat.ElectronicPointer.Platform.Presentation;
using Mutantcat.ElectronicPointer.Platform.Recognition;
using Mutantcat.ElectronicPointer.Platform.Startup;

namespace Mutantcat.ElectronicPointer.Platform.Services;

/// <summary>
/// Do-nothing implementations used when a capability cannot run on the current platform, or
/// when no platform assembly matched at all. They let the app stay usable: the board, the
/// tools and the undo history all keep working, and only the affected feature is switched off.
/// </summary>
public sealed class UnsupportedOverlayChrome : IOverlayChrome
{
    public bool IsSupported => false;

    public bool Attach(IOverlayWindowTarget target) => false;

    public void Detach()
    {
    }

    public bool SetClickThrough(bool enabled) => false;

    public bool SetAlwaysOnTop(bool enabled) => false;

    public bool SetHiddenFromSwitcher(bool hidden) => false;
}

public sealed class UnsupportedGlobalHotkeyService : IGlobalHotkeyService
{
    public bool IsSupported => false;

    // Explicit accessors: the subscription is legal, it just never fires.
    public event EventHandler<Hotkey>? Pressed
    {
        add
        {
        }

        remove
        {
        }
    }

    public bool Register(Hotkey hotkey) => false;

    public bool Unregister(Hotkey hotkey) => false;

    public void UnregisterAll()
    {
    }
}

public sealed class UnsupportedScreenCaptureService : IScreenCaptureService
{
    public bool IsSupported => false;

    public IReadOnlyList<DisplayInfo> Displays => Array.Empty<DisplayInfo>();

    public CapturedScreen? Capture(DisplayInfo display) => null;
}

public sealed class UnsupportedPresentationBridge : IPresentationBridge
{
    public bool IsSupported => false;

    public bool IsPresentationActive => false;

    public event EventHandler<bool>? PresentationActiveChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    public void Start()
    {
    }

    public void Stop()
    {
    }
}

public sealed class UnsupportedInkRecognizer : IInkRecognizer
{
    public bool IsSupported => false;

    public Task<RecognitionReport> RecognizeAsync(
        IReadOnlyList<IReadOnlyList<Vec2>> strokes,
        CancellationToken cancellationToken)
        => Task.FromResult(RecognitionReport.Nothing);
}

public sealed class UnsupportedAutoStartService : IAutoStartService
{
    public bool IsSupported => false;

    public bool IsEnabled => false;

    public void SetEnabled(bool enabled)
    {
    }
}

/// <summary>A platform bundle that reports no capability at all.</summary>
public sealed class UnsupportedPlatformServices : IPlatformServices
{
    public UnsupportedPlatformServices(string platformName, PlatformCapabilities? capabilities = null)
    {
        PlatformName = platformName;
        Capabilities = capabilities ?? PlatformCapabilities.None;
    }

    public PlatformCapabilities Capabilities { get; }

    public string PlatformName { get; }

    public IOverlayChrome Overlay { get; } = new UnsupportedOverlayChrome();

    public IGlobalHotkeyService Hotkeys { get; } = new UnsupportedGlobalHotkeyService();

    public IScreenCaptureService ScreenCapture { get; } = new UnsupportedScreenCaptureService();

    public IPresentationBridge Presentation { get; } = new UnsupportedPresentationBridge();

    public IInkRecognizer Recognizer { get; } = new UnsupportedInkRecognizer();

    public IAutoStartService AutoStart { get; } = new UnsupportedAutoStartService();

    public IReadOnlyList<DisplayInfo> Displays => Array.Empty<DisplayInfo>();

    public void Dispose()
    {
    }
}
