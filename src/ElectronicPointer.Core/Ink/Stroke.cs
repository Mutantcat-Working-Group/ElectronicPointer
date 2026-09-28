using Mutantcat.ElectronicPointer.Core.Geometry;

namespace Mutantcat.ElectronicPointer.Core.Ink;

/// <summary>
/// A single stroke in progress or finished. The outline is derived, never stored, so a
/// style change can be applied without re-collecting input.
/// </summary>
public sealed class Stroke
{
    private int _revision;

    public Stroke()
    {
    }

    public Stroke(StrokeStyle style) => Style = style;

    public Guid Id { get; init; } = Guid.NewGuid();

    public StrokeStyle Style { get; private set; } = StrokeStyle.DefaultPen();

    public List<StrokeSample> Samples { get; } = new();

    public bool IsComplete { get; private set; }

    /// <summary>Bumped on every mutation so renderers can cache per stroke.</summary>
    public int Revision => _revision;

    public double Length => Samples.Count > 0 ? Samples[^1].Point.DistanceTo(Samples[0].Point) : 0;

    public void Append(Vec2 point, double? pressure)
    {
        Samples.Add(StrokeSample.Create(point, pressure));
        _revision++;
    }

    public void AppendRange(IEnumerable<StrokeSample> samples)
    {
        Samples.AddRange(samples);
        _revision++;
    }

    public void Complete()
    {
        if (IsComplete)
            return;

        IsComplete = true;
        _revision++;
    }

    /// <summary>
    /// Slides every sample by the same offset. Used by the select tool when a grabbed
    /// bunch of ink is dragged elsewhere on the board.
    /// </summary>
    public void Translate(Vec2 offset)
    {
        if (offset == Vec2.Zero)
            return;

        for (var i = 0; i < Samples.Count; i++)
        {
            var sample = Samples[i];
            Samples[i] = StrokeSample.Create(sample.Point + offset, sample.Pressure);
        }

        _revision++;
    }

    public void SetStyle(StrokeStyle style)
    {
        Style = style;
        _revision++;
    }

    /// <summary>Generates the filled polygon for the current samples and style.</summary>
    public IReadOnlyList<Vec2> BuildOutline()
    {
        var options = Style.ToFreehandOptions(IsComplete);
        var points = Freehand.GetStrokePoints(Samples, options);
        return Freehand.GetStrokeOutlinePoints(points, options);
    }
}

/// <summary>
/// Hit testing for the stroke eraser. The eraser removes a whole stroke when it touches
/// any part of it, which is how the original floating bar behaves.
/// </summary>
public static class StrokeHitTester
{
    public static bool Intersects(Stroke stroke, Vec2 point, double eraserRadius)
    {
        var samples = stroke.Samples;
        if (samples.Count == 0)
            return false;

        var limit = eraserRadius + Math.Max(1, stroke.Style.Size / 2);
        if (samples.Count == 1)
            return point.DistanceTo(samples[0].Point) <= limit;

        for (var i = 1; i < samples.Count; i++)
        {
            if (Vec2.DistanceToSegment(point, samples[i - 1].Point, samples[i].Point) <= limit)
                return true;
        }

        return false;
    }
}
