using Mutantcat.ElectronicPointer.Core.Geometry;
using Mutantcat.ElectronicPointer.Core.Ink;

namespace Mutantcat.ElectronicPointer.Core.Selection;

/// <summary>
/// Lasso maths for the select tool. A stroke counts as selected when either end sits inside
/// the loop, or when its centreline crosses the loop outline; that is the behaviour a finger
/// or a mouse expects from a lasso, and it is small enough to keep as pure geometry here
/// rather than in the renderer.
/// </summary>
public static class Lasso
{
    /// <summary>Ray casting containment test. Works for concave loops too.</summary>
    public static bool Contains(IReadOnlyList<Vec2> polygon, Vec2 point)
    {
        if (polygon.Count < 3)
            return false;

        var inside = false;
        var j = polygon.Count - 1;
        for (var i = 0; i < polygon.Count; j = i++)
        {
            var current = polygon[i];
            var previous = polygon[j];
            if ((current.Y > point.Y) != (previous.Y > point.Y))
            {
                var crossingX = ((previous.X - current.X) * (point.Y - current.Y) / (previous.Y - current.Y)) + current.X;
                if (point.X < crossingX)
                    inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>True when the centreline of a stroke meets the loop outline.</summary>
    public static bool Contains(IReadOnlyList<Vec2> polygon, IReadOnlyList<Vec2> samples)
    {
        if (polygon.Count < 3 || samples.Count == 0)
            return false;

        if (Contains(polygon, samples[0]) || Contains(polygon, samples[^1]))
            return true;

        var edges = polygon.Count;
        for (var i = 1; i < samples.Count; i++)
        {
            var from = samples[i - 1];
            var to = samples[i];
            for (var e = 0; e < edges; e++)
            {
                if (SegmentsCross(from, to, polygon[e], polygon[(e + 1) % edges]))
                    return true;
            }
        }

        return false;
    }

    /// <summary>True when the loop of a lasso, closed by an implicit last-to-first edge, meets the stroke.</summary>
    public static bool Selects(IReadOnlyList<Vec2> loop, Stroke stroke)
        => Contains(loop, stroke.Samples.Select(sample => sample.Point).ToList());

    /// <summary>Signed area through the shoelace formula; negative means clockwise.</summary>
    public static double Area(IReadOnlyList<Vec2> polygon)
    {
        var sum = 0.0;
        var last = polygon.Count - 1;
        for (var i = 0; i < polygon.Count; last = i++)
            sum += (polygon[last].X * polygon[i].Y) - (polygon[i].X * polygon[last].Y);

        return sum / 2;
    }

    private static bool SegmentsCross(Vec2 a, Vec2 b, Vec2 c, Vec2 d)
    {
        return Orientation(a, b, c) != Orientation(a, b, d)
            && Orientation(c, d, a) != Orientation(c, d, b);
    }

    private static int Orientation(Vec2 a, Vec2 b, Vec2 c)
    {
        var cross = ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));
        return Math.Sign(cross);
    }
}
