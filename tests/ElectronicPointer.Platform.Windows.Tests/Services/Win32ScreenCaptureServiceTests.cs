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

    [Fact]
    public void DisplaysAreNumberedFromTheLeftAndCarryTheirPlaceOnTheDesk()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var displays = new Win32ScreenCaptureService().Displays;
        if (displays.Count == 0)
            return;

        Assert.Equal(Enumerable.Range(0, displays.Count), displays.Select(display => display.Index));

        // Left to right, top to bottom. The order the driver hands the monitors over in is
        // the order of the ports, which on a desk with two identical screens says nothing
        // about which one is on the left, and the number is all the user has to go on.
        for (var i = 1; i < displays.Count; i++)
        {
            var previous = displays[i - 1];
            var current = displays[i];

            Assert.True(
                (previous.X, previous.Y).CompareTo((current.X, current.Y)) < 0,
                $"displays are out of order: {previous.X},{previous.Y} then {current.X},{current.Y}");
        }

        foreach (var display in displays)
        {
            Assert.True(display.Width > 0 && display.Height > 0, "a display without a size");
            Assert.True(display.X >= 0 && display.Y >= 0, $"a display off the desk: {display.X},{display.Y}");
        }
    }

    [Fact]
    public void NoTwoDisplaysCarryTheSameRectangle()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var displays = new Win32ScreenCaptureService().Displays;

        // The rectangle is the identity the freeze path matches on. Two entries sharing one
        // would make that match a coin toss between them, which is the whole bug this
        // exists to keep out.
        Assert.Equal(
            displays.Count,
            displays.Select(display => (display.X, display.Y, display.Width, display.Height)).Distinct().Count());
    }
}
