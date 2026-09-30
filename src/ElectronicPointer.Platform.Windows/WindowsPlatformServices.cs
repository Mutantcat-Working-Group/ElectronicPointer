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
/// - ink recognition runs the built in shape engine, the same one the other desktops use,
///   because it was written to be portable from the start rather than depending on the
///   Windows Ink analyser the old build was tied to.
/// </summary>
public sealed class WindowsPlatformServices : IPlatformServices
{
    /// <summary>Why one button stays disabled, shown to the user in Chinese.</summary>
    public const string MissingCapabilitiesNote =
        "内置墨迹引擎可把手写笔迹整理成规范图形（直线、箭头、矩形、三角形、椭圆），暂时不能把手写内容转写成文字。";

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

    private readonly Win32ScreenCaptureService _capture = new();
    private readonly Win32GlobalHotkeyService _hotkeys = new();
    private readonly Win32AutoStartService _autoStart = new();
    private readonly Win32PresentationBridge _presentation = new();
    private readonly BuiltInShapeRecognizer _recognizer = new();

    public WindowsPlatformServices()
    {
        Capabilities = new PlatformCapabilities(SupportedFeatures, MissingCapabilitiesNote);
    }

    public PlatformCapabilities Capabilities { get; }

    public string PlatformName => "Windows";

    /// <summary>A chrome per window: each one holds the window it was attached to.</summary>
    public IOverlayChrome CreateOverlayChrome() => new Win32OverlayChrome();

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
}
