using Mutantcat.ElectronicPointer.Core.Geometry;
using Xunit;

namespace Mutantcat.ElectronicPointer.Core.Tests.Geometry;

public class Vec2Tests
{
    [Fact]
    public void DistanceToSegment_InteriorPoint_UsesProjection()
    {
        var distance = Vec2.DistanceToSegment(new Vec2(5, 4), new Vec2(0, 0), new Vec2(10, 0));
        Assert.Equal(4, distance, 6);
    }

    [Fact]
    public void DistanceToSegment_BeyondEndpoint_ClampsToEndpoint()
    {
        var distance = Vec2.DistanceToSegment(new Vec2(15, 0), new Vec2(0, 0), new Vec2(10, 0));
        Assert.Equal(5, distance, 6);
    }

    [Fact]
    public void DistanceToSegment_DegenerateSegment_MeasuresPointDistance()
    {
        var distance = Vec2.DistanceToSegment(new Vec2(3, 4), new Vec2(0, 0), new Vec2(0, 0));
        Assert.Equal(5, distance, 6);
    }

    [Fact]
    public void Perp_RotatesNinetyDegreesClockwise()
    {
        Assert.Equal(new Vec2(0, 1), new Vec2(-1, 0).Perp());
    }

    [Fact]
    public void RotateAround_QuarterTurn()
    {
        var result = new Vec2(1, 0).RotateAround(Vec2.Zero, Math.PI / 2);
        Assert.Equal(0, result.X, 6);
        Assert.Equal(1, result.Y, 6);
    }

    [Fact]
    public void Normalize_ZeroVector_StaysZeroInsteadOfNaN()
    {
        Assert.Equal(Vec2.Zero, Vec2.Zero.Normalize());
    }

    [Fact]
    public void NearlyEquals_IsAxisAlignedWithSmallEpsilon()
    {
        Assert.True(new Vec2(0, 0).NearlyEquals(new Vec2(0.005, 0)));
    }
}
