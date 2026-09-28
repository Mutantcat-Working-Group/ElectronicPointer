using Mutantcat.ElectronicPointer.Core.Geometry;
using Mutantcat.ElectronicPointer.Core.Ink;
using Xunit;

namespace Mutantcat.ElectronicPointer.Core.Tests.Ink;

public class FreehandTests
{
    private static List<StrokeSample> Line(double x0, double x1, double y, double pressure, int steps)
    {
        var samples = new List<StrokeSample>(steps);
        for (var i = 0; i < steps; i++)
        {
            var t = (double)i / (steps - 1);
            samples.Add(new StrokeSample(new Vec2(x0 + ((x1 - x0) * t), y), pressure));
        }

        return samples;
    }

    [Fact]
    public void GetStrokeOutlinePoints_NoSamples_ReturnsEmpty()
    {
        var outline = Freehand.GetStrokeOutlinePoints(Freehand.GetStrokePoints(Array.Empty<StrokeSample>(), new StrokeOptions()), new StrokeOptions());
        Assert.Empty(outline);
    }

    [Fact]
    public void GetStrokeOutlinePoints_StraightLine_ProducesClosedPolygon()
    {
        var options = new StrokeOptions(Size: 16, SimulatePressure: false);
        var outline = Freehand.GetStrokeOutlinePoints(Freehand.GetStrokePoints(Line(0, 300, 0, 0.5, 12), options), options);

        Assert.True(outline.Count > 8, $"expected a real outline, got {outline.Count} points");

        // A horizontal stroke must extend vertically by roughly its diameter and stay
        // vertically symmetric about the centre line.
        var minY = double.MaxValue;
        var maxY = double.MinValue;
        double sumY = 0;
        foreach (var p in outline)
        {
            minY = Math.Min(minY, p.Y);
            maxY = Math.Max(maxY, p.Y);
            sumY += p.Y;
        }

        Assert.True(maxY - minY > 12, $"outline is too thin: {maxY - minY}");
        Assert.True(maxY - minY < 25, $"outline is too thick: {maxY - minY}");
        Assert.True(Math.Abs(sumY / outline.Count) < 1.0, "outline is not centred on the stroke");
    }

    [Fact]
    public void OutlineWidth_FollowsPressure_WhenThinningEnabled()
    {
        // This is the regression test for the original port, where the pressure branch
        // was unreachable and every stroke rendered at a constant width.
        var light = new StrokeOptions(Size: 16, Thinning: 0.5, SimulatePressure: false);
        var heavy = light;

        var lightSpan = VerticalSpan(Line(0, 300, 0, 0.05, 12), light);
        var heavySpan = VerticalSpan(Line(0, 300, 0, 0.95, 12), heavy);

        Assert.True(heavySpan > lightSpan * 1.5, $"expected a wide stroke for high pressure, got {heavySpan} vs {lightSpan}");
    }

    [Fact]
    public void OutlineWidth_IgnoresPressure_WhenThinningDisabled()
    {
        // A highlighter keeps a constant width no matter how hard the pen is pressed.
        var light = new StrokeOptions(Size: 20, Thinning: 0, SimulatePressure: false);
        var heavy = light;

        var lightSpan = VerticalSpan(Line(0, 300, 0, 0.05, 12), light);
        var heavySpan = VerticalSpan(Line(0, 300, 0, 0.95, 12), heavy);

        Assert.Equal(lightSpan, heavySpan, 3);
    }

    [Fact]
    public void Outline_SingleSample_RendersDot()
    {
        var options = new StrokeOptions(Size: 24, Thinning: 0);
        var outline = Freehand.GetStrokeOutlinePoints(
            Freehand.GetStrokePoints(new List<StrokeSample> { new(new Vec2(100, 100), 0.5) }, options),
            options);

        Assert.NotEmpty(outline);
        Assert.True(outline.Count > 8);

        double radius = 0;
        foreach (var p in outline)
            radius = Math.Max(radius, p.DistanceTo(new Vec2(100, 100)));

        Assert.True(radius > 8 && radius < 24, $"dot radius out of range: {radius}");
    }

    [Fact]
    public void Outline_TwoSamples_DoesNotThrow()
    {
        var options = new StrokeOptions(Size: 12);
        var samples = new List<StrokeSample> { new(new Vec2(0, 0), 0.5), new(new Vec2(40, 30), 0.5) };

        var outline = Freehand.GetStrokeOutlinePoints(Freehand.GetStrokePoints(samples, options), options);

        Assert.NotEmpty(outline);
    }

    [Fact]
    public void Outline_ShortStroke_DoesNotThrowOnCaps()
    {
        // Very short strokes skip most outline points; the cap code must still find the
        // first left and right point.
        var options = new StrokeOptions(Size: 16, SimulatePressure: false);
        var samples = new List<StrokeSample> { new(new Vec2(0, 0), 0.5), new(new Vec2(2, 1), 0.5) };

        var outline = Freehand.GetStrokeOutlinePoints(Freehand.GetStrokePoints(samples, options), options);

        Assert.NotEmpty(outline);
    }

    [Fact]
    public void Outline_TaperedEnds_CoverLessInkThanRoundCaps()
    {
        // Taper runs along the whole stroke (both ends shrink), so the polygon must cover
        // clearly less ink than the same stroke with rounded caps. Comparing the widest
        // span would not work: the very last sample is still emitted at full radius.
        var samples = Line(0, 300, 0, 0.5, 12);
        var capped = new StrokeOptions(Size: 16, Thinning: 0.5, SimulatePressure: false);
        var tapered = new StrokeOptions(Size: 16, Thinning: 0.5, SimulatePressure: false, Start: new StrokeCapOptions(TaperEnabled: true), End: new StrokeCapOptions(TaperEnabled: true));

        Assert.True(PolygonArea(samples, tapered) < PolygonArea(samples, capped) * 0.75, "tapered stroke should draw noticeably less ink");
    }

    [Fact]
    public void Outline_TaperedStart_IsThinAtTheTip()
    {
        var capped = new StrokeOptions(Size: 16, Thinning: 0, SimulatePressure: false);
        var tapered = new StrokeOptions(Size: 16, Thinning: 0, SimulatePressure: false, Start: new StrokeCapOptions(TaperEnabled: true));
        var samples = Line(0, 300, 0, 0.5, 12);

        // The first outline point pair straddles the start of the stroke, so their distance
        // is the width of the tip.
        Assert.True(StartThickness(samples, tapered) < StartThickness(samples, capped) * 0.5, "tapered tip should be much thinner than a capped tip");
    }

    [Fact]
    public void Outline_SharpTurn_StaysFinite()
    {
        var options = new StrokeOptions(Size: 16, SimulatePressure: false);
        var samples = new List<StrokeSample>
        {
            new(new Vec2(0, 0), 0.5),
            new(new Vec2(50, 0), 0.5),
            new(new Vec2(100, 0), 0.5),
            new(new Vec2(100, 60), 0.5),
            new(new Vec2(100, 120), 0.5),
            new(new Vec2(50, 120), 0.5),
        };

        var outline = Freehand.GetStrokeOutlinePoints(Freehand.GetStrokePoints(samples, options), options);

        Assert.NotEmpty(outline);
        foreach (var p in outline)
        {
            Assert.False(double.IsNaN(p.X) || double.IsInfinity(p.X), "outline contains a non-finite point");
        }
    }

    [Fact]
    public void StrokePoint_Vector_IsNormalised()
    {
        var options = new StrokeOptions(Size: 16, SimulatePressure: false);
        var points = Freehand.GetStrokePoints(Line(0, 300, 0, 0.5, 12), options);

        Assert.True(points.Count > 2);
        foreach (var p in points)
        {
            Assert.True(Math.Abs(p.Vector.Length - 1) < 1e-6, $"vector not normalised: {p.Vector.Length}");
        }
    }

    private static double VerticalSpan(IReadOnlyList<StrokeSample> samples, StrokeOptions options)
    {
        var outline = Freehand.GetStrokeOutlinePoints(Freehand.GetStrokePoints(samples, options), options);
        var minY = double.MaxValue;
        var maxY = double.MinValue;
        foreach (var p in outline)
        {
            minY = Math.Min(minY, p.Y);
            maxY = Math.Max(maxY, p.Y);
        }

        return maxY - minY;
    }

    private static double PolygonArea(IReadOnlyList<StrokeSample> samples, StrokeOptions options)
    {
        var outline = Freehand.GetStrokeOutlinePoints(Freehand.GetStrokePoints(samples, options), options);
        var twice = 0.0;
        for (var i = 0; i < outline.Count; i++)
        {
            var a = outline[i];
            var b = outline[(i + 1) % outline.Count];
            twice += (a.X * b.Y) - (b.X * a.Y);
        }

        return Math.Abs(twice) / 2;
    }

    /// <summary>Half-height of the outline at the very beginning of a horizontal stroke.</summary>
    private static double StartThickness(IReadOnlyList<StrokeSample> samples, StrokeOptions options)
    {
        var outline = Freehand.GetStrokeOutlinePoints(Freehand.GetStrokePoints(samples, options), options);
        var thickness = 0.0;
        foreach (var p in outline)
        {
            if (p.X < 0 || p.X > 25)
                continue;

            thickness = Math.Max(thickness, Math.Abs(p.Y));
        }

        return thickness * 2;
    }
}
