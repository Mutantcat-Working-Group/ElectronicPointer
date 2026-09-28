using Mutantcat.ElectronicPointer.Platform;
using Mutantcat.ElectronicPointer.Platform.Capture;
using Mutantcat.ElectronicPointer.Platform.Displays;
using Mutantcat.ElectronicPointer.Platform.Input;
using Mutantcat.ElectronicPointer.Platform.Overlay;
using Mutantcat.ElectronicPointer.Platform.Presentation;
using Mutantcat.ElectronicPointer.Platform.Recognition;
using Mutantcat.ElectronicPointer.Platform.Services;
using Mutantcat.ElectronicPointer.Platform.Startup;

namespace Mutantcat.ElectronicPointer.Platform.Linux;

/// <summary>
/// The Linux bundle. Linux is not one desktop but a family of them, so capabilities are
/// filtered by what the running session actually exposes rather than by the kernel.
///
/// On an X11 session almost everything works: the overlay is click-through through the SHAPE
/// extension, hotkeys come from XGrabKey, and a freeze reads pixels with XGetImage. On a
/// native Wayland session the compositor is designed to refuse global key grabs, arbitrary
/// window placement and server side screen reads, so those three report as unavailable and
/// the board still runs: ink, tools, pages and undo are all platform independent and keep
/// working either way.
///
/// Slideshow detection is a /proc scan, because there is no add-in model on Linux; the old
/// Windows behaviour cannot be reached from outside the office suite. Handwriting recognition
/// has no portable OS API at all and is left to the OCR extension.
/// </summary>
public sealed class LinuxPlatformServices : IPlatformServices
{
    /// <summary>What the user has to do before the disabled buttons come back.</summary>
    public const string MissingCapabilitiesNote =
        "Wayland 会话下全局快捷键、始终置顶与冻结屏幕不可用（协议不向客户端开放），请改用 X11 或 Xorg 会话；"
        + "手写识别将由 org.mutantcat.electronicpointer.plugin.ocr 扩展提供，演示文稿联动依赖进程识别。";

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
    };

    private readonly X11OverlayChrome _overlay = new();
    private readonly X11GlobalHotkeyService _hotkeys = new();
    private readonly X11ScreenCaptureService _capture = new();
    private readonly LinuxAutoStartService _autoStart = new();
    private readonly LinuxPresentationBridge _presentation = new();
    private readonly UnsupportedHandwritingRecognizer _recognizer = new();

    private readonly PlatformCapabilities _capabilities;

    public LinuxPlatformServices()
    {
        _capabilities = BuildCapabilities();
    }

    public PlatformCapabilities Capabilities => _capabilities;

    public string PlatformName => LinuxSession.Current switch
    {
        LinuxSessionKind.Wayland => "Linux (Wayland)",
        LinuxSessionKind.Headless => "Linux (无图形会话)",
        _ => "Linux (X11)",
    };

    public IOverlayChrome Overlay => _overlay;

    public IGlobalHotkeyService Hotkeys => _hotkeys;

    public IScreenCaptureService ScreenCapture => _capture;

    public IPresentationBridge Presentation => _presentation;

    public IHandwritingRecognizer Recognizer => _recognizer;

    public IAutoStartService AutoStart => _autoStart;

    public IReadOnlyList<DisplayInfo> Displays => _capture.Displays;

    public void Dispose()
    {
        _presentation.Dispose();
        _hotkeys.Dispose();
    }

    /// <summary>
    /// Capabilities are filtered by session, not by kernel: a Wayland compositor refuses the
    /// three overlay features at the protocol level, and reporting them as available would
    /// just produce silent failures when the user pressed the button.
    /// </summary>
    private PlatformCapabilities BuildCapabilities()
    {
        var sessionLimited = LinuxSession.Current switch
        {
            LinuxSessionKind.X11 => SupportedFeatures,
            LinuxSessionKind.Headless => Array.Empty<PlatformFeature>(),
            _ => new[]
            {
                // Placement is possible through the compositor, the rest is not.
                PlatformFeature.PerDisplayPlacement,
                PlatformFeature.PresentationDetection,
                PlatformFeature.AutoStart,
            },
        };

        return new PlatformCapabilities(sessionLimited, MissingCapabilitiesNote);
    }
}
