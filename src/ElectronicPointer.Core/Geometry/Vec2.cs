namespace Mutantcat.ElectronicPointer.Core.Geometry;

/// <summary>
/// A 2D point in board coordinates (device independent pixels, top-left origin).
/// Immutable value type so strokes can be shared between the render backends safely.
/// </summary>
public readonly record struct Vec2(double X, double Y)
{
    public static readonly Vec2 Zero = default;

    public static readonly Vec2 One = new(1, 1);

    public double Length => Math.Sqrt(LengthSquared);

    public double LengthSquared => (X * X) + (Y * Y);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);

    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);

    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);

    public static Vec2 operator *(Vec2 a, double scale) => new(a.X * scale, a.Y * scale);

    public static Vec2 operator *(double scale, Vec2 a) => new(a.X * scale, a.Y * scale);

    public static Vec2 operator /(Vec2 a, double scale) => new(a.X / scale, a.Y / scale);

    public static double Dot(Vec2 a, Vec2 b) => (a.X * b.X) + (a.Y * b.Y);

    public static Vec2 Lerp(Vec2 a, Vec2 b, double t) => a + ((b - a) * t);

    /// <summary>Rotates 90 degrees clockwise in screen coordinates.</summary>
    public Vec2 Perp() => new(Y, -X);

    public Vec2 Normalize()
    {
        var length = Length;
        return length < 1e-9 ? Zero : this / length;
    }

    public double DistanceTo(Vec2 other) => Math.Sqrt(DistanceSquaredTo(other));

    public double DistanceSquaredTo(Vec2 other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        return (dx * dx) + (dy * dy);
    }

    public Vec2 RotateAround(Vec2 origin, double radians)
    {
        var sin = Math.Sin(radians);
        var cos = Math.Cos(radians);
        var px = X - origin.X;
        var py = Y - origin.Y;
        var nx = (px * cos) - (py * sin);
        var ny = (px * sin) + (py * cos);
        return new Vec2(nx + origin.X, ny + origin.Y);
    }

    /// <summary>
    /// Shortest distance from <paramref name="p"/> to the segment <paramref name="a"/>-<paramref name="b"/>.
    /// </summary>
    public static double DistanceToSegment(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        var lengthSquared = ab.LengthSquared;
        if (lengthSquared < 1e-12)
            return p.DistanceTo(a);

        var t = Dot(p - a, ab) / lengthSquared;
        t = Math.Clamp(t, 0, 1);
        var projection = a + (ab * t);
        return p.DistanceTo(projection);
    }

    /// <summary>Axis tolerance match, matching the epsilon used by the original ink canvas.</summary>
    public bool NearlyEquals(Vec2 other) => Math.Abs(X - other.X) < 0.01 && Math.Abs(Y - other.Y) < 0.01;

    public override string ToString() => $"({X:F2}, {Y:F2})";
}
