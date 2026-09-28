using Mutantcat.ElectronicPointer.Core.Geometry;
using Mutantcat.ElectronicPointer.Core.Ink;
using Mutantcat.ElectronicPointer.Core.Selection;
using Xunit;

namespace Mutantcat.ElectronicPointer.Core.Tests.Selection;

public class LassoTests
{
    private static readonly Vec2[] SquareLoop = new[]
    {
        new Vec2(0, 0),
        new Vec2(100, 0),
        new Vec2(100, 100),
        new Vec2(0, 100),
    };

    private static Stroke StrokeFrom(params (double X, double Y)[] points)
    {
        var stroke = new Stroke();
        foreach (var point in points)
            stroke.Append(new Vec2(point.X, point.Y), 0.5);
        stroke.Complete();
        return stroke;
    }

    [Fact]
    public void Contains_PointInsideTheLoop_IsTrue()
    {
        Assert.True(Lasso.Contains(SquareLoop, new Vec2(50, 50)));
        Assert.True(Lasso.Contains(SquareLoop, new Vec2(0.5, 0.5)));
    }

    [Fact]
    public void Contains_PointOutsideTheLoop_IsFalse()
    {
        Assert.False(Lasso.Contains(SquareLoop, new Vec2(150, 50)));
        Assert.False(Lasso.Contains(SquareLoop, new Vec2(-1, 50)));
        Assert.False(Lasso.Contains(SquareLoop, new Vec2(50, 101)));
    }

    [Fact]
    public void Contains_ConcaveLoop_ExcludesTheNotch()
    {
        // An L shaped loop: the missing fourth quadrant is outside even though it lies
        // inside the loop's bounding box.
        var notch = new[]
        {
            new Vec2(0, 0),
            new Vec2(100, 0),
            new Vec2(100, 50),
            new Vec2(50, 50),
            new Vec2(50, 100),
            new Vec2(0, 100),
        };

        Assert.True(Lasso.Contains(notch, new Vec2(25, 25)));
        Assert.False(Lasso.Contains(notch, new Vec2(75, 75)));
    }

    [Fact]
    public void Selects_StrokeInsideTheLoop_IsTrue()
    {
        var stroke = StrokeFrom((20, 20), (40, 30), (80, 90));
        Assert.True(Lasso.Selects(SquareLoop, stroke));
    }

    [Fact]
    public void Selects_StrokeOutsideTheLoop_IsFalse()
    {
        var stroke = StrokeFrom((200, 20), (240, 30), (280, 90));
        Assert.False(Lasso.Selects(SquareLoop, stroke));
    }

    [Fact]
    public void Selects_StrokeCrossingTheLoopEdge_IsTrue()
    {
        // Starts outside, ends inside: only the crossing makes it a hit.
        var stroke = StrokeFrom((-50, 50), (50, 50));
        Assert.True(Lasso.Selects(SquareLoop, stroke));
    }

    [Fact]
    public void Selects_LoopWithFewerThanThreePoints_IsFalse()
    {
        var stroke = StrokeFrom((10, 10), (20, 20));
        Assert.False(Lasso.Selects(new[] { new Vec2(0, 0), new Vec2(50, 50) }, stroke));
    }

    [Fact]
    public void Area_SquareLoop_MatchesTheShoelaceFormula()
    {
        var area = Lasso.Area(SquareLoop);
        Assert.Equal(10000, Math.Abs(area));
    }
}
