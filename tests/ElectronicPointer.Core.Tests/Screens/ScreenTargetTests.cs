using Mutantcat.ElectronicPointer.Core.Screens;
using Xunit;

namespace Mutantcat.ElectronicPointer.Core.Tests.Screens;

public class ScreenTargetTests
{
    private static readonly ScreenPlacement Left = new(0, 0, 1920, 1080);

    private static readonly ScreenPlacement Right = new(1920, 0, 1920, 1080);

    private static readonly ScreenPlacement[] TwoIdenticalMonitors = [Left, Right];

    [Fact]
    public void IndexFor_TellsTwoIdenticalMonitorsApart()
    {
        Assert.Equal(1, ScreenTarget.IndexFor(TwoIdenticalMonitors, Right, 1));
        Assert.Equal(0, ScreenTarget.IndexFor(TwoIdenticalMonitors, Left, 0));
    }

    [Fact]
    public void IndexFor_PrefersTheRectangleOverTheStoredIndex()
    {
        // The stored index names the right monitor while the grabbed display sits at the
        // left edge. The rectangle decides, because an index is only a guess that two
        // independent enumerations happened to agree on an order.
        Assert.Equal(0, ScreenTarget.IndexFor(TwoIdenticalMonitors, Left, 1));
    }

    [Fact]
    public void IndexFor_FallsBackToTheStoredIndexWhenNoRectangleAgrees()
    {
        // A capture backend that reports from a different origin never matches. The
        // display the user picked still beats a size that matches both screens at once.
        var elsewhere = new ScreenPlacement(9999, 9999, 1920, 1080);

        Assert.Equal(1, ScreenTarget.IndexFor(TwoIdenticalMonitors, elsewhere, 1));
    }

    [Fact]
    public void IndexFor_FallsBackToSizeWhenTheStoredIndexIsOutOfRange()
    {
        // A screen was unplugged since the choice was stored, so the index points past
        // the end. Size is the last thing left to go on.
        Assert.Equal(1, ScreenTarget.IndexFor(TwoIdenticalMonitors, Right, 7));
    }

    [Fact]
    public void IndexFor_SeparatesMonitorsOfDifferentSizes()
    {
        var screens = new[] { Left, new ScreenPlacement(0, 1080, 1920, 1200) };

        Assert.Equal(1, ScreenTarget.IndexFor(screens, new ScreenPlacement(0, 1080, 1920, 1200), 0));
    }

    [Fact]
    public void IndexFor_ReportsNothingWhenNeitherTheRectangleNorTheSizeFits()
    {
        Assert.Equal(-1, ScreenTarget.IndexFor(Array.Empty<ScreenPlacement>(), Left, 0));
        Assert.Equal(-1, ScreenTarget.IndexFor(TwoIdenticalMonitors, new ScreenPlacement(0, 0, 3840, 2160), 9));
    }

    [Fact]
    public void SameSize_IgnoresPosition()
    {
        Assert.True(Left.SameSize(new ScreenPlacement(4096, 4096, 1920, 1080)));
        Assert.False(Left.SameSize(new ScreenPlacement(0, 0, 1920, 1200)));
    }
}
