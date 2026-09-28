using Mutantcat.ElectronicPointer.Platform;
using Mutantcat.ElectronicPointer.Platform.Displays;
using Mutantcat.ElectronicPointer.Platform.Capture;
using Mutantcat.ElectronicPointer.Platform.Presentation;
using Mutantcat.ElectronicPointer.Platform.Recognition;
using Mutantcat.ElectronicPointer.Platform.Services;
using Mutantcat.ElectronicPointer.Platform.Startup;
using Mutantcat.ElectronicPointer.Platform.Input;
using Mutantcat.ElectronicPointer.Platform.Overlay;

namespace Mutantcat.ElectronicPointer.Platform.Windows;

/// <summary>
/// The Windows bundle. Windows is the platform the old product was built on, so it is the
/// one place where almost every capability is available:
///
/// - the overlay is click-through, top-most and hidden from Alt+Tab;
/// - global hotkeys come from RegisterHotKey on a dedicated message window;
/// - screen freeze reads pixels through GDI;
/// - a slideshow is spotted by its window class;
/// - auto start is the HKCU Run value;
/// - ink recognition stays off, because the Windows Ink text analyser the old build used
///   is not portable and the replacement is an OCR plugin.
/// </summary>
public sealed class WindowsPlatformServices : IPlatformServices
{
    /// <summary>Why one button stays disabled, shown to the user in Chinese.</summary>
    public const string MissingCapabilitiesNote =
        "手写识别暂未内置，将通过 org.mutantcat.electronicpointer.plugin.ocr 扩展提供。";

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

    private readonly Win32ScreenCaptureService _capture = new();
    private readonly Win32GlobalHotkeyService _hotkeys = new();
    private readonly Win32OverlayChrome _overlay = new();
    private readonly Win32AutoStartService _autoStart = new();
    private readonly Win32PresentationBridge _presentation = new();
    private readonly UnsupportedHandwritingRecognizer _recognizer = new();

    public WindowsPlatformServices()
    {
        Capabilities = new PlatformCapabilities(SupportedFeatures, MissingCapabilitiesNote);
    }

    public PlatformCapabilities Capabilities { get; }

    public string PlatformName => "Windows";

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
}
