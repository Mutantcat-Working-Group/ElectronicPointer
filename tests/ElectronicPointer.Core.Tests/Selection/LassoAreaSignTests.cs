using Mutantcat.ElectronicPointer.Core.Geometry;
using Mutantcat.ElectronicPointer.Core.Selection;
using Xunit;

namespace Mutantcat.ElectronicPointer.Core.Tests.Selection;

/// <summary>
/// <see cref="Lasso.Area"/> keeps the sign of the shoelace formula, so the winding of a loop
/// survives a round trip through the selection maths. The toolbar uses it to tell a loop
/// drawn forwards from the very same loop drawn backwards, which matters when a loop is used
/// as a shape rather than only as a containment test.
/// </summary>
public class LassoAreaSignTests
{
    private static readonly Vec2[] CounterClockwiseSquare = new[]
    {
        new Vec2(0, 0),
        new Vec2(10, 0),
        new Vec2(10, 10),
        new Vec2(0, 10),
    };

    [Fact]
    public void Area_CounterClockwiseLoop_IsPositive()
    {
        Assert.Equal(100, Lasso.Area(CounterClockwiseSquare), 6);
    }

    [Fact]
    public void Area_ClockwiseLoop_IsNegative()
    {
        var clockwise = new[]
        {
            new Vec2(0, 0),
            new Vec2(0, 10),
            new Vec2(10, 10),
            new Vec2(10, 0),
        };

        Assert.Equal(-100, Lasso.Area(clockwise), 6);
    }

    [Fact]
    public void Area_ReversingTheLoop_FlipsTheSignOnly()
    {
        var reversed = CounterClockwiseSquare.Reverse().ToArray();

        Assert.Equal(-Lasso.Area(CounterClockwiseSquare), Lasso.Area(reversed), 6);
    }

    [Fact]
    public void Area_Triangle_IsHalfTheBoundingBox()
    {
        var triangle = new[]
        {
            new Vec2(0, 0),
            new Vec2(8, 0),
            new Vec2(0, 6),
        };

        Assert.Equal(24, Lasso.Area(triangle), 6);
    }

    [Fact]
    public void Area_DegenerateLoop_IsZero()
    {
        var collapsed = new[]
        {
            new Vec2(5, 5),
            new Vec2(5, 5),
            new Vec2(5, 5),
        };

        Assert.Equal(0, Lasso.Area(collapsed), 6);
    }
}
