using Mutantcat.ElectronicPointer.Platform.Windows;
using Xunit;

namespace Mutantcat.ElectronicPointer.Platform.Windows.Tests;

/// <summary>
/// The corner preferences a window is asked for, pinned against the values the host's own
/// header gives them. The numbering of DWM_WINDOW_CORNER_PREFERENCE is not the order the
/// names arrive in, and an asking for the host to keep its hands off written with the
/// number for asking it to round instead hands the host the opposite instruction: a window
/// that already drew its own corners gets a second radius of the host's painted over them,
/// which is exactly the disagreement these values exist to avoid.
/// </summary>
public sealed class Win32CornerPreferenceTests
{
    [Fact]
    public void ThePreferenceValueKeptItsOwnName()
    {
        // 0 default, 1 do not round, 2 round, 3 round small.
        Assert.Equal(0, NativeMethods.DwmwcpDefault);
        Assert.Equal(1, NativeMethods.DwmwcpDoNotRound);
        Assert.Equal(2, NativeMethods.DwmwcpRound);
        Assert.Equal(3, NativeMethods.DwmwcpRoundSmall);
    }

    [Fact]
    public void KeepingTheHostsHandsOffIsNotTheSameAsAskingItForACorner()
    {
        // The hands-off instruction and the two that ask for a corner must not collapse
        // into one: every window here draws its own shape, so the only preference any of
        // them wants is the host's hands off.
        Assert.NotEqual(NativeMethods.DwmwcpDoNotRound, NativeMethods.DwmwcpRoundSmall);
        Assert.NotEqual(NativeMethods.DwmwcpDoNotRound, NativeMethods.DwmwcpRound);
    }

    [Fact]
    public void ReassertingTheShapeWithNoCompanionSaysItDidNothing()
    {
        // No companion is bound yet, so there is no handle for the preference to land on.
        // Re-asserting has to report that it did nothing rather than hand an asking to a
        // zero handle: the guard that repeats the asking runs before the lift on the opening
        // frame just as it runs after every later move, activation and composition.
        var chrome = new Win32OverlayChrome();
        Assert.False(chrome.ReassertCompanionShape());

        // The same answer after a detach: the companion the chrome used to hold is gone, so
        // there is nothing left to hand the preference to and nothing to pretend otherwise.
        chrome.DetachCompanion();
        Assert.False(chrome.ReassertCompanionShape());
    }
}
