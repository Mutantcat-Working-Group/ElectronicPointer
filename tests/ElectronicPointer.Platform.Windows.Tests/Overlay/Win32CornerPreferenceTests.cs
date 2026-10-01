using Mutantcat.ElectronicPointer.Platform.Windows;
using Xunit;

namespace Mutantcat.ElectronicPointer.Platform.Windows.Tests;

/// <summary>
/// The corner preferences a window is asked for, pinned against the values the host's own
/// header gives them. The numbering of DWM_WINDOW_CORNER_PREFERENCE is not the order the
/// names arrive in, and an asking for the host to keep its hands off written with the
/// number for asking it to round instead hands the host the opposite instruction. A host
/// that has been handed the opposite one adds a radius of its own to a window that already
/// drew one, which is exactly the disagreement these values exist to avoid.
/// </summary>
public sealed class Win32CornerPreferenceTests
{
    [Fact]
    public void ThePreferenceValueKeptItsOwnName()
    {
        // 0 default, 1 round, 2 do not round, 3 round small.
        Assert.Equal(0, NativeMethods.DwmwcpDefault);
        Assert.Equal(1, NativeMethods.DwmwcpRound);
        Assert.Equal(2, NativeMethods.DwmwcpDoNotRound);
        Assert.Equal(3, NativeMethods.DwmwcpRoundSmall);
    }

    [Fact]
    public void KeepingTheHostsHandsOffIsNotTheSameAsAskingItForACorner()
    {
        // The two instructions a companion window hands over must not collapse into one:
        // one asks for a corner that lands inside the card's own, the other asks for no
        // corner at all, and the canvas wants the second while the palette wants the first.
        Assert.NotEqual(NativeMethods.DwmwcpDoNotRound, NativeMethods.DwmwcpRoundSmall);
        Assert.NotEqual(NativeMethods.DwmwcpDoNotRound, NativeMethods.DwmwcpRound);
    }
}
