using Mutantcat.ElectronicPointer.Core.Input;
using Mutantcat.ElectronicPointer.Platform;
using Mutantcat.ElectronicPointer.Platform.Input;
using Mutantcat.ElectronicPointer.Platform.Overlay;
using Xunit;

namespace Mutantcat.ElectronicPointer.Platform.Linux.Tests.Services;

/// <summary>
/// The Linux bundle reports what the running session can actually do. The tests below hold in
/// every session, because the point is not what this particular machine is but that the
/// reported capability set never claims something the compositor refuses to give.
/// </summary>
public class LinuxPlatformServicesTests
{
    [Fact]
    public void Capabilities_ClaimShapeRecognitionEverywhereButHeadless()
    {
        using var services = new LinuxPlatformServices();

        // Recognition is arithmetic over points the session already handed over, so no
        // compositor has to agree to it. The engine is present on X11, on Wayland and
        // headless alike; only the headless capability set stays empty, because a session
        // with no screen has no board to tidy.
        Assert.True(services.Recognizer.IsSupported);
        Assert.Equal(
            LinuxSession.Current != LinuxSessionKind.Headless,
            services.Capabilities.Supports(PlatformFeature.HandwritingRecognition));
    }

    [Fact]
    public void Capabilities_AlwaysExplainWhatIsMissing()
    {
        using var services = new LinuxPlatformServices();

        Assert.False(string.IsNullOrWhiteSpace(services.Capabilities.Notes));
        Assert.Contains("Wayland", services.Capabilities.Notes);
    }

    [Fact]
    public void PlatformName_MatchesTheDetectedSession()
    {
        using var services = new LinuxPlatformServices();

        var expected = LinuxSession.Current switch
        {
            LinuxSessionKind.Wayland => "Linux (Wayland)",
            LinuxSessionKind.Headless => "Linux (无图形会话)",
            _ => "Linux (X11)",
        };

        Assert.Equal(expected, services.PlatformName);
    }

    [Fact]
    public void Hotkeys_AreOnlyClaimedOnAnX11Session()
    {
        using var services = new LinuxPlatformServices();

        Assert.Equal(LinuxSession.IsX11, services.Hotkeys.IsSupported);
        Assert.Equal(LinuxSession.IsX11, services.Capabilities.Supports(PlatformFeature.GlobalHotkey));
        Assert.Equal(LinuxSession.IsX11, services.Capabilities.Supports(PlatformFeature.ScreenCapture));
    }

    [Fact]
    public void Wayland_KeepsTheFeaturesTheCompositorAllows()
    {
        using var services = new LinuxPlatformServices();

        if (LinuxSession.Current != LinuxSessionKind.Wayland)
            return;

        // Placement, slideshow detection and autostart are all outside the compositor's grip.
        Assert.True(services.Capabilities.Supports(PlatformFeature.PerDisplayPlacement));
        Assert.True(services.Capabilities.Supports(PlatformFeature.PresentationDetection));
        Assert.True(services.Capabilities.Supports(PlatformFeature.AutoStart));

        // The three that need a protocol the compositor withholds stay off.
        Assert.False(services.Capabilities.Supports(PlatformFeature.GlobalHotkey));
        Assert.False(services.Capabilities.Supports(PlatformFeature.ScreenCapture));
        Assert.False(services.Capabilities.Supports(PlatformFeature.ClickThroughOverlay));
    }

    [Fact]
    public void X11_ClaimsTheFullOverlaySet()
    {
        using var services = new LinuxPlatformServices();

        if (LinuxSession.Current != LinuxSessionKind.X11)
            return;

        Assert.True(services.Capabilities.Supports(PlatformFeature.ClickThroughOverlay));
        Assert.True(services.Capabilities.Supports(PlatformFeature.AlwaysOnTopOverlay));
        Assert.True(services.Capabilities.Supports(PlatformFeature.HideFromTaskSwitcher));
        Assert.True(services.Capabilities.Supports(PlatformFeature.GlobalHotkey));
        Assert.True(services.Capabilities.Supports(PlatformFeature.ScreenCapture));
        Assert.True(services.Capabilities.Supports(PlatformFeature.PerDisplayPlacement));
        Assert.True(services.Capabilities.Supports(PlatformFeature.PresentationDetection));
        Assert.True(services.Capabilities.Supports(PlatformFeature.AutoStart));
        Assert.True(services.Capabilities.Supports(PlatformFeature.HandwritingRecognition));
    }

    [Fact]
    public void Headless_ClaimsNothingAtAll()
    {
        using var services = new LinuxPlatformServices();

        if (LinuxSession.Current != LinuxSessionKind.Headless)
            return;

        Assert.Empty(services.Capabilities.Features);
    }

    [Fact]
    public async Task Recognizer_HandedBackNothing_AnswersRatherThanThrowing()
    {
        using var services = new LinuxPlatformServices();

        var report = await services.Recognizer.RecognizeAsync(
            Array.Empty<IReadOnlyList<Mutantcat.ElectronicPointer.Core.Geometry.Vec2>>(),
            CancellationToken.None);

        // An empty run is a normal answer, not an error: the toolbar reports the summary and
        // nothing on the board moves.
        Assert.Equal(0, report.Count);
        Assert.False(string.IsNullOrWhiteSpace(report.Summary));
    }

    [Fact]
    public void HotkeyRegistration_NeedsBothAnX11SessionAndAKnownKey()
    {
        using var services = new LinuxPlatformServices();

        // Registering has to be safe to call even where nothing can happen: it is the same
        // code path the app runs at startup on every platform.
        Assert.False(services.Hotkeys.Register(new Hotkey(HotkeyModifiers.Control, KeyCode.None)));
    }

    [Fact]
    public void Dispose_IsSafeToCallTwice()
    {
        var services = new LinuxPlatformServices();

        services.Dispose();
        services.Dispose();
    }

    [Fact]
    public void OverlayChrome_IsHandedOutPerWindow()
    {
        using var services = new LinuxPlatformServices();

        // Every canvas owns its native window, so a shared chrome would have its target
        // replaced by the next canvas and detached by whichever canvas closed first.
        var first = services.CreateOverlayChrome();
        var second = services.CreateOverlayChrome();

        Assert.NotSame(first, second);
        Assert.Equal(LinuxSession.IsX11, first.IsSupported);
        Assert.Equal(LinuxSession.IsX11, second.IsSupported);
    }

    [Fact]
    public void Companion_IsRefusedWhereNoCanvasCanBeHosted()
    {
        using var services = new LinuxPlatformServices();
        var chrome = services.CreateOverlayChrome();

        // A session that cannot host a canvas cannot host the palette that drives one
        // either, and the answer has to be the same refusal rather than a half applied
        // state on a window that was never bound. The supported path needs a live canvas
        // in a real session, which is what a unit test cannot honestly provide.
        if (chrome.IsSupported)
            return;

        Assert.False(chrome.AttachCompanion(new StubWindow(0x1000)));
        chrome.DetachCompanion();
    }

    private sealed class StubWindow : IOverlayWindowTarget
    {
        public StubWindow(nint handle) => Handle = handle;

        public nint Handle { get; }
    }
}
