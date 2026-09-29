using Mutantcat.ElectronicPointer.Core.Geometry;
using Mutantcat.ElectronicPointer.Core.Recognition;
using Xunit;

namespace Mutantcat.ElectronicPointer.Core.Tests.Recognition;

/// <summary>
/// The recognizer only ever gets one chance to be right, and a wrong answer destroys work,
/// so the tests below hold both directions: every shape a user actually draws has to come
/// back, and ink that is not a shape has to be left alone.
/// </summary>
public class ShapeRecognizerTests
{
    [Fact]
    public void StraightLine_ComesBackAsALine()
    {
        var points = Line((100, 200), (420, 200));

        var shape = ShapeRecognizer.Recognize(points);

        Assert.Equal(ShapeKind.Line, shape.Kind);
        Assert.Equal(2, shape.Points.Count);
        Assert.InRange(shape.Points[0].DistanceTo(new Vec2(100, 200)), 0, 2);
        Assert.InRange(shape.Points[^1].DistanceTo(new Vec2(420, 200)), 0, 2);
    }

    [Fact]
    public void Arrow_ShaftFirstOrHeadFirst_ComesBackAsAnArrow()
    {
        // The head is drawn after the shaft, which is what a right handed user does...
        var shape = ShapeRecognizer.Recognize(Arrow());
        Assert.Equal(ShapeKind.Arrow, shape.Kind);

        // ...and before the shaft, which is what a user tidying up afterwards does. The
        // reading has to be the same, because both describe the same drawn arrow.
        var reversed = Arrow();
        reversed.Reverse();
        Assert.Equal(ShapeKind.Arrow, ShapeRecognizer.Recognize(reversed).Kind);
    }

    [Fact]
    public void Rectangle_ComesBackAsARectangle()
    {
        var points = ClosedRectangle((120, 120), (320, 260));

        var shape = ShapeRecognizer.Recognize(points);

        Assert.Equal(ShapeKind.Rectangle, shape.Kind);
        Assert.Equal(5, shape.Points.Count);
    }

    [Fact]
    public void Triangle_ComesBackAsATriangle()
    {
        var points = ClosedTriangle((120, 300), (360, 300), (240, 110));

        var shape = ShapeRecognizer.Recognize(points);

        Assert.Equal(ShapeKind.Triangle, shape.Kind);
        Assert.Equal(4, shape.Points.Count);
    }

    [Fact]
    public void Circle_ComesBackAsAnEllipse()
    {
        var points = Circle((240, 200), 110, 60);

        var shape = ShapeRecognizer.Recognize(points);

        Assert.Equal(ShapeKind.Ellipse, shape.Kind);
    }

    [Fact]
    public void ShakyTriangle_StillComesBackAsATriangle()
    {
        // A hand drawn triangle never sits on its true edges. The wobble has to read as
        // wobble rather than as corners, because losing one vertex turns a triangle into an
        // ellipse, which is a tidy up the user did not ask for.
        var points = Shaky(ClosedTriangle((120, 300), (360, 300), (240, 110)), 3);

        Assert.Equal(ShapeKind.Triangle, ShapeRecognizer.Recognize(points).Kind);
    }

    [Fact]
    public void ShakyCircle_StillComesBackAsAnEllipse()
    {
        var points = Shaky(Circle((240, 200), 110, 60), 3);

        Assert.Equal(ShapeKind.Ellipse, ShapeRecognizer.Recognize(points).Kind);
    }

    [Fact]
    public void TiltedRectangle_ComesBackAsARectangle()
    {
        // A rectangle drawn at an angle is still a rectangle; the recognizer measures across
        // the stroke's own axis rather than the screen's.
        var points = ClosedRectangle((120, 120), (320, 260))
            .Select(point => point.RotateAround(new Vec2(240, 200), 0.6))
            .ToArray();

        Assert.Equal(ShapeKind.Rectangle, ShapeRecognizer.Recognize(points).Kind);
    }

    [Fact]
    public void Scribble_ComesBackUnknown()
    {
        // A deterministic walk: the point of the test is that ink which is not a shape is
        // handed back untouched, and a fixed seed keeps that promise reproducible.
        var random = new PseudoRandom(20260929);
        var walk = new List<Vec2> { new(200, 200) };
        for (var i = 0; i < 70; i++)
            walk.Add(walk[^1] + new Vec2(random.Next(-18, 18), random.Next(-18, 18)));

        var shape = ShapeRecognizer.Recognize(walk);

        Assert.False(shape.IsRecognized);
    }

    [Fact]
    public void StrokeTooSmallToRead_ComesBackUnknown()
    {
        Assert.False(ShapeRecognizer.Recognize(Line((100, 100), (104, 102))).IsRecognized);
        Assert.False(ShapeRecognizer.Recognize(new[] { new Vec2(10, 10), new Vec2(11, 10) }).IsRecognized);
        Assert.False(ShapeRecognizer.Recognize(Array.Empty<Vec2>()).IsRecognized);
    }

    // ------------------------------------------------------------------ fixtures

    private static List<Vec2> Line((double X, double Y) from, (double X, double Y) to)
    {
        var points = new List<Vec2>();
        var a = new Vec2(from.X, from.Y);
        var b = new Vec2(to.X, to.Y);
        var steps = (int)Math.Ceiling(a.DistanceTo(b) / 8);

        for (var i = 0; i <= steps; i++)
            points.Add(Vec2.Lerp(a, b, (double)i / steps));

        return points;
    }

    /// <summary>Shaft, then head: tip, upper barb, back to the tip, lower barb.</summary>
    private static List<Vec2> Arrow()
    {
        var points = Line((110, 200), (400, 200));
        points.Add(new Vec2(400, 200));
        points.Add(new Vec2(368, 172));
        points.Add(new Vec2(400, 200));
        points.Add(new Vec2(368, 228));
        return points;
    }

    private static List<Vec2> ClosedRectangle((double X, double Y) min, (double X, double Y) max)
    {
        var points = new List<Vec2>();
        var corners = new[]
        {
            new Vec2(min.X, min.Y),
            new Vec2(max.X, min.Y),
            new Vec2(max.X, max.Y),
            new Vec2(min.X, max.Y),
        };

        for (var i = 0; i < corners.Length; i++)
            points.AddRange(Line((corners[i].X, corners[i].Y), (corners[(i + 1) % corners.Length].X, corners[(i + 1) % corners.Length].Y))[..^1]);

        // A loop drawn by hand comes back to where it started.
        points.Add(corners[0]);
        return points;
    }

    private static List<Vec2> ClosedTriangle((double X, double Y) a, (double X, double Y) b, (double X, double Y) c)
    {
        var points = new List<Vec2>();
        var corners = new[] { new Vec2(a.X, a.Y), new Vec2(b.X, b.Y), new Vec2(c.X, c.Y) };

        for (var i = 0; i < corners.Length; i++)
            points.AddRange(Line((corners[i].X, corners[i].Y), (corners[(i + 1) % corners.Length].X, corners[(i + 1) % corners.Length].Y))[..^1]);

        points.Add(corners[0]);
        return points;
    }

    private static List<Vec2> Circle((double X, double Y) center, double radius, int samples)
    {
        var points = new List<Vec2>();
        for (var i = 0; i <= samples; i++)
        {
            var angle = i * (2 * Math.PI / samples);
            points.Add(new Vec2(center.X + (Math.Cos(angle) * radius), center.Y + (Math.Sin(angle) * radius)));
        }

        return points;
    }

    /// <summary>
    /// A fixed sequence, so a failure is a bug in the recognizer rather than a bad roll.
    /// <see cref="Random"/> is shared global state and would make the suite flaky.
    /// </summary>
    private sealed class PseudoRandom
    {
        private ulong _state;

        public PseudoRandom(int seed) => _state = (ulong)seed;

        public double Next(double minimum, double maximum)
        {
            _state = (_state * 6364136223846793005UL) + 1442695040888963407UL;
            var value = (double)((_state >> 33) % 1000000UL) / 1000000d;
            return minimum + (value * (maximum - minimum));
        }

        public int Next(int minimum, int maximum)
        {
            _state = (_state * 6364136223846793005UL) + 1442695040888963407UL;
            var value = (int)((_state >> 33) % (ulong)(maximum - minimum));
            return minimum + Math.Abs(value);
        }
    }

    /// <summary>The same stroke as a hand would draw it, a few pixels off the true geometry.</summary>
    private static List<Vec2> Shaky(List<Vec2> points, double pixels)
    {
        var jitter = new PseudoRandom(20260929);
        return points
            .Select(point => new Vec2(point.X + jitter.Next(-pixels, pixels), point.Y + jitter.Next(-pixels, pixels)))
            .ToList();
    }
}
