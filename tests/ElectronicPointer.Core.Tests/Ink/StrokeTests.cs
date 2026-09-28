using Mutantcat.ElectronicPointer.Core.Geometry;
using Mutantcat.ElectronicPointer.Core.Ink;
using Xunit;

namespace Mutantcat.ElectronicPointer.Core.Tests.Ink;

public class StrokeTests
{
    [Fact]
    public void Highlighter_HasConstantWidthAndTransparentColour()
    {
        var style = StrokeStyle.CreateHighlighter(0x60FFE14D, 18);

        Assert.Equal(StrokeKind.Highlighter, style.Kind);
        Assert.Equal(0d, style.Thinning);
        Assert.False(style.SimulatePressure);
        Assert.Equal(0x60u, (style.Color >> 24) & 0xFFu);
    }

    [Fact]
    public void Pen_IsPressureSensitive()
    {
        var style = StrokeStyle.CreatePen(0xFF1B1B1F, 3);

        Assert.Equal(StrokeKind.Pen, style.Kind);
        Assert.True(style.Thinning > 0);
        Assert.True(style.SimulatePressure);
    }

    [Fact]
    public void Append_NormalisesMissingPressure()
    {
        var stroke = new Stroke(StrokeStyle.CreatePen(0xFF000000, 8));
        stroke.Append(new Vec2(0, 0), null);
        stroke.Append(new Vec2(10, 0), 0.9);

        Assert.Equal(StrokeSample.NeutralPressure, stroke.Samples[0].Pressure);
        Assert.Equal(0.9, stroke.Samples[1].Pressure);
    }

    [Fact]
    public void Revision_AdvancesOnEveryMutation()
    {
        var stroke = new Stroke(StrokeStyle.CreatePen(0xFF000000, 8));
        var initial = stroke.Revision;

        stroke.Append(new Vec2(0, 0), 0.5);
        Assert.Equal(initial + 1, stroke.Revision);

        stroke.Complete();
        Assert.Equal(initial + 2, stroke.Revision);

        // Completing twice must not bump the revision again.
        stroke.Complete();
        Assert.Equal(initial + 2, stroke.Revision);
    }

    [Fact]
    public void HitTester_ErasesNearbyStroke()
    {
        var stroke = new Stroke(StrokeStyle.CreatePen(0xFF000000, 8));
        for (var x = 0; x <= 100; x += 5)
            stroke.Append(new Vec2(x, 0), 0.5);
        stroke.Complete();

        Assert.True(StrokeHitTester.Intersects(stroke, new Vec2(50, 3), 4));
        Assert.False(StrokeHitTester.Intersects(stroke, new Vec2(50, 30), 4));
    }

    [Fact]
    public void HitTester_DotStroke_IsHitInsideItsDot()
    {
        var stroke = new Stroke(StrokeStyle.CreatePen(0xFF000000, 8));
        stroke.Append(new Vec2(10, 10), 0.5);
        stroke.Complete();

        Assert.True(StrokeHitTester.Intersects(stroke, new Vec2(11, 11), 2));
        Assert.False(StrokeHitTester.Intersects(stroke, new Vec2(30, 30), 2));
    }
}
