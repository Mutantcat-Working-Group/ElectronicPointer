using Mutantcat.ElectronicPointer.Platform.Windows;
using Xunit;

namespace Mutantcat.ElectronicPointer.Platform.Windows.Tests;

/// <summary>
/// Grabs a display end to end through GDI, which is the only place the freeze button is
/// ever proven. The public Capture used to resolve the display it was handed and then call
/// itself with the answer instead of the private grab behind it, so the lookup matched that
/// same display again on every pass and pressing "freeze screen" walked the stack down
/// until the runtime gave up. That failure is one no caller can catch, try or log around,
/// and the button stayed enabled and ordinary looking all the way through, so the one
/// defense against a repeat is a test that actually reaches the grab. Every assertion here
/// waits for Windows, the way the hotkey tests do.
/// </summary>
public sealed class Win32ScreenCaptureServiceTests
{
    [Fact]
    public void GrabbingADisplayAnswersWithThatDisplaysOwnPixels()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var service = new Win32ScreenCaptureService();

        // A desk the host reports nothing on is not this test's subject: there is no display
        // to ask for and no grab to prove came back.
        var display = service.Displays.Count > 0 ? service.Displays[0] : null;
        if (display is null)
            return;

        var captured = service.Capture(display);

        // A host that refuses the grab answers with null rather than throwing, and a
        // session with no display of its own may well refuse. A picture that does arrive
        // has to be the one that was asked for, because a grab that returns some other
        // display's pixels lands the annotation on a monitor the user is not looking at.
        if (captured is null)
            return;

        Assert.Equal(display.Width, captured.Width);
        Assert.Equal(display.Height, captured.Height);
        Assert.Equal(display.Width * display.Height * 4, captured.Pixels.Length);
        Assert.Equal(display.Name, captured.DisplayName);
    }
}
