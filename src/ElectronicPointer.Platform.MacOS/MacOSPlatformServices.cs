using Mutantcat.ElectronicPointer.Platform;
using Mutantcat.ElectronicPointer.Platform.Capture;
using Mutantcat.ElectronicPointer.Platform.Displays;
using Mutantcat.ElectronicPointer.Platform.Input;
using Mutantcat.ElectronicPointer.Platform.Overlay;
using Mutantcat.ElectronicPointer.Platform.Presentation;
using Mutantcat.ElectronicPointer.Platform.Recognition;
using Mutantcat.ElectronicPointer.Platform.Services;
using Mutantcat.ElectronicPointer.Platform.Startup;

namespace Mutantcat.ElectronicPointer.Platform.MacOS;

/// <summary>
/// The macOS bundle. Click-through, always-on-top and hiding from the Dock all come from
/// AppKit window state; the global shortcut rides an event tap; screen freeze reads pixels
/// from CoreGraphics; auto start writes a launch agent. Slideshow detection is the one piece
/// that cannot match Windows, so it is reported as unavailable rather than half-working;
/// ink recognition runs the built in shape engine and needs nothing from the OS at all.
/// </summary>
public sealed class MacOSPlatformServices : IPlatformServices
{
    /// <summary>What the user has to do before the disabled buttons come back.</summary>
    public const string MissingCapabilitiesNote =
        "macOS 上暂不能联动放映中的演示文稿；"
        + "内置墨迹引擎可把手写笔迹整理成规范图形，暂时不能把手写内容转写成文字；"
        + "首次使用屏幕冻结需要授权系统设置中的「屏幕录制」权限。";

    private static readonly PlatformFeature[] SupportedFeatures =
    {
        PlatformFeature.ClickThroughOverlay,
        PlatformFeature.AlwaysOnTopOverlay,
        PlatformFeature.HideFromTaskSwitcher,
        PlatformFeature.GlobalHotkey,
        PlatformFeature.ScreenCapture,
        PlatformFeature.PerDisplayPlacement,
        PlatformFeature.PresentationDetection,
        PlatformFeature.AutoStart,
        PlatformFeature.HandwritingRecognition,
    };

    private readonly MacOSGlobalHotkeyService _hotkeys = new();
    private readonly MacOSScreenCaptureService _capture = new();
    private readonly MacOSAutoStartService _autoStart = new();
    private readonly MacOSPresentationBridge _presentation = new();
    private readonly BuiltInShapeRecognizer _recognizer = new();

    public MacOSPlatformServices()
    {
        Capabilities = BuildCapabilities();
    }

    public PlatformCapabilities Capabilities { get; }

    public string PlatformName => "macOS";

    /// <summary>A chrome per window: each one holds the window it was attached to.</summary>
    public IOverlayChrome CreateOverlayChrome() => new MacOSOverlayChrome();

    public IGlobalHotkeyService Hotkeys => _hotkeys;

    public IScreenCaptureService ScreenCapture => _capture;

    public IPresentationBridge Presentation => _presentation;

    public IInkRecognizer Recognizer => _recognizer;

    public IAutoStartService AutoStart => _autoStart;

    public IReadOnlyList<DisplayInfo> Displays => _capture.Displays;

    public void Dispose()
    {
        _presentation.Dispose();
        _hotkeys.Dispose();
    }

    /// <summary>
    /// Capabilities are filtered by what the running system actually grants: an event tap
    /// without the Accessibility permission never fires, and a screen grab without Screen
    /// Recording comes back black. Reporting those as missing keeps the app honest about what
    /// will work before the user tries it.
    /// </summary>
    private PlatformCapabilities BuildCapabilities()
    {
        var capabilities = new PlatformCapabilities(SupportedFeatures.Where(f =>
        {
            if (f == PlatformFeature.GlobalHotkey)
                return OperatingSystem.IsMacOS() && _hotkeys.IsSupported;

            if (f == PlatformFeature.ScreenCapture)
                return OperatingSystem.IsMacOS() && _capture.IsSupported;

            return true;
        }), MissingCapabilitiesNote);

        return capabilities;
    }
}
