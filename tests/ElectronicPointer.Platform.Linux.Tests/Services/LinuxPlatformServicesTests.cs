using Mutantcat.ElectronicPointer.Core.Input;
using Mutantcat.ElectronicPointer.Platform;
using Mutantcat.ElectronicPointer.Platform.Input;
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
    public void Capabilities_NeverClaimHandwritingRecognition()
    {
        using var services = new LinuxPlatformServices();

        // No Linux API recognises handwriting, so the button has to stay off until an OCR
        // extension answers for it.
        Assert.False(services.Capabilities.Supports(PlatformFeature.HandwritingRecognition));
        Assert.False(services.Recognizer.IsSupported);
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
        Assert.False(services.Capabilities.Supports(PlatformFeature.HandwritingRecognition));
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
    public async Task Recognizer_WithNoEngine_GivesBackNothingRatherThanThrowing()
    {
        using var services = new LinuxPlatformServices();

        var task = services.Recognizer.RecognizeAsync(Array.Empty<IReadOnlyList<Mutantcat.ElectronicPointer.Core.Geometry.Vec2>>(), CancellationToken.None);

        Assert.Equal(string.Empty, await task);
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
}
