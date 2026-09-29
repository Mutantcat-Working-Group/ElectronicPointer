using Mutantcat.ElectronicPointer.Core.Geometry;

namespace Mutantcat.ElectronicPointer.Core.Recognition;

/// <summary>
/// Tidies a hand drawn stroke into the geometric shape it was meant to be.
///
/// The old build shipped the Windows Ink analyser, which no other operating system exposes,
/// so the replacement is a self contained engine that runs everywhere from the same source.
/// It is deliberately conservative: when the ink does not read as one of the supported
/// shapes the stroke is left exactly as drawn, because a wrong tidy up destroys work in a
/// way the user cannot predict.
///
/// The pipeline is clean, gate, resample, then one of two paths: a closed loop is tested
/// against the shapes that enclose an area, an open path against the ones that point
/// somewhere. Every candidate is scored by the average distance between the drawn points and
/// the candidate geometry, normalised by the stroke's own span, so one set of thresholds
/// behaves the same on a small scribble and a full width whiteboard line. The best scoring
/// candidate wins, rather than the first one that happens to fit, which is what keeps a
/// wobbly circle from being tidied into a rectangle.
/// </summary>
public static class ShapeRecognizer
{
    // Below these an input is a dot or a twitch, not a shape.
    private const double MinimumPathLength = 24;
    private const double MinimumSpan = 10;

    // Pen input repeats samples at sub pixel spacing; those carry no direction information
    // and would otherwise blur the corner detector.
    private const double MinimumPointSpacing = 0.75;

    // How far apart the two ends of a stroke may be before the loop counts as closed.
    private const double ClosureFraction = 0.28;

    // Every threshold is set as a fraction of the stroke's own span, so the same number
    // reads the same on a small tablet stroke and a full whiteboard one. A pixel allowance
    // rides along so a small shape is not held to a standard no hand can meet.
    private const double ToleranceSlack = 1.5;

    // Resampling aims at a fixed number of samples whatever the size of the stroke, so a
    // corner detector window always covers a comparable stretch of ink.
    private const int ResampleTarget = 120;
    private const double MinimumResampleStep = 2;

    // The turn a corner costs, measured as the change of travel direction. A rectangle corner
    // turns through 90 degrees, the vertex of an equilateral triangle through 120, while a
    // straight edge turns through none at all. Sitting at 55 keeps every corner a hand can
    // draw while leaving the wobble of a shaky straight edge, which turns through a handful
    // of degrees, well below the line.
    private const double CornerAngleDegrees = 55;

    // How much ink either side of a sample counts as the baseline its direction is measured
    // over. Measuring the direction across a baseline rather than between neighbouring
    // samples is what makes the reading survive a shaky hand: a wobble of a couple of pixels
    // barely moves the direction of a twenty pixel chord, while it swings the direction of a
    // two pixel one wildly.
    private const double CornerWindowDivisor = 16;
    private const double MinimumCornerWindow = 12;
    private const double MaximumCornerWindow = 28;

    // How far apart two corner candidates may sit and still be read as the same corner. A
    // corner's turn can be shared between two neighbouring samples, so the samples that only
    // see half of it fall below the threshold and leave holes in the run; the chaining has to
    // bridge those holes, which are always narrower than the window that caused them.
    private const double CornerChainSlack = 1.5;

    private const double MinimumLoopFill = 0.35;
    private const double SliverRatio = 0.12;
    private const double PolygonTolerance = 0.09;
    private const double EllipseTolerance = 0.10;
    private const double Straightness = 1.05;

    private const double ArrowHeadReach = 0.25;
    private const double ArrowMinimumShaft = 0.35;
    private const double ArrowOnAxisTolerance = 0.06;
    private const double ArrowMaximumBarbSkew = 0.35;
    private const double ArrowBarbLengthShare = 0.12;
    private const double ArrowBarbLengthCeiling = 0.5;
    private const double ArrowMinimumBarb = 22;
    private const double ArrowMaximumBarb = 45;

    private const int EllipseSegments = 64;

    /// <summary>
    /// Reads one stroke. <see cref="RecognizedShape.Unknown"/> means "leave the ink alone",
    /// which is a normal answer rather than a failure.
    /// </summary>
    public static RecognizedShape Recognize(IReadOnlyList<Vec2> path)
    {
        if (path is null || path.Count < 2)
            return RecognizedShape.Unknown;

        var cleaned = Clean(path);
        if (cleaned.Count < 2)
            return RecognizedShape.Unknown;

        var span = BoundsOf(cleaned).Span;
        var length = PathLength(cleaned);
        if (length < MinimumPathLength || span < MinimumSpan)
            return RecognizedShape.Unknown;

        var resampled = Resample(cleaned, ResampleStepFor(span));
        var closingGap = resampled[0].DistanceTo(resampled[^1]);

        return closingGap <= ClosureFraction * span
            ? RecognizeClosed(resampled, closingGap, span)
            : RecognizeOpen(resampled);
    }

    // ------------------------------------------------------------------ open paths

    private static RecognizedShape RecognizeOpen(IReadOnlyList<Vec2> path)
    {
        // The arrow test comes first because it is structural: it looks for a shaft plus two
        // symmetric barbs, which is what an arrowhead is, whether the user drew the head
        // first or last. A straightness test on its own would happily accept an arrow whose
        // barbs happen to be short.
        if (TryArrow(path) is { } arrow)
            return arrow;

        var straight = PathLength(path) <= path[0].DistanceTo(path[^1]) * Straightness;
        return straight ? AsLine(path) : RecognizedShape.Unknown;
    }

    /// <summary>
    /// An arrow is a shaft with two barbs at the far end. Both directions along the stroke's
    /// own major axis are tried, because the head may sit at either end of the path.
    /// </summary>
    private static RecognizedShape? TryArrow(IReadOnlyList<Vec2> path)
    {
        var axes = FitAxes(path);
        if (axes.SemiMajor < 1e-6)
            return null;

        return ArrowAlong(path, axes, axes.Major) ?? ArrowAlong(path, axes, -axes.Major);
    }

    private static RecognizedShape? ArrowAlong(IReadOnlyList<Vec2> path, Axes axes, Vec2 axis)
    {
        var count = path.Count;
        var projections = new double[count];
        var across = new double[count];
        var lowest = double.MaxValue;
        var highest = double.MinValue;

        for (var i = 0; i < count; i++)
        {
            var offset = path[i] - axes.Center;
            projections[i] = Vec2.Dot(offset, axis);
            across[i] = Vec2.Dot(offset, axes.Minor);
            lowest = Math.Min(lowest, projections[i]);
            highest = Math.Max(highest, projections[i]);
        }

        var length = highest - lowest;
        if (length < 1e-6)
            return null;

        // The shaft is the part that stays on the axis. Barbs lean away from it, so the
        // extreme points that are still on the axis are the two ends of the shaft, and a
        // barb can never masquerade as the tip.
        var tailProjection = double.MaxValue;
        var tipProjection = double.MinValue;
        var tail = Vec2.Zero;
        var tip = Vec2.Zero;
        var onAxisLimit = ArrowOnAxisTolerance * length;

        for (var i = 0; i < count; i++)
        {
            if (Math.Abs(across[i]) > onAxisLimit)
                continue;

            if (projections[i] < tailProjection)
            {
                tailProjection = projections[i];
                tail = path[i];
            }

            if (projections[i] > tipProjection)
            {
                tipProjection = projections[i];
                tip = path[i];
            }
        }

        if (tipProjection == double.MinValue || tipProjection - tailProjection < ArrowMinimumShaft * length)
            return null;

        // The head is the last stretch of the stroke. The furthest points off the axis on
        // either side of it are the barbs; requiring one on each side is what separates an
        // arrow from a line with a hook at the end.
        var headStart = highest - ArrowHeadReach * length;
        var upper = default(Vec2);
        var lower = default(Vec2);
        var upperAcross = 0d;
        var lowerAcross = 0d;

        for (var i = 0; i < count; i++)
        {
            if (projections[i] < headStart)
                continue;

            if (across[i] > upperAcross)
            {
                upperAcross = across[i];
                upper = path[i];
            }

            if (across[i] < lowerAcross)
            {
                lowerAcross = across[i];
                lower = path[i];
            }
        }

        if (upperAcross <= onAxisLimit || -lowerAcross <= onAxisLimit)
            return null;

        var barbSpan = Math.Max(upperAcross, -lowerAcross);
        if (Math.Abs(upperAcross + lowerAcross) > ArrowMaximumBarbSkew * barbSpan)
            return null;

        var shaft = tail - tip;
        if (shaft.Length < 1e-9)
            return null;

        foreach (var barb in new[] { upper, lower })
        {
            var reach = barb - tip;
            var barbLength = reach.Length;
            if (barbLength < ArrowBarbLengthShare * length || barbLength > ArrowBarbLengthCeiling * length)
                return null;

            var angle = AngleBetween(reach, shaft);
            if (angle < ArrowMinimumBarb || angle > ArrowMaximumBarb)
                return null;
        }

        // The midpoint keeps the tidy up from pinching at the tip, where the outline narrows.
        var points = new[] { tail, Vec2.Lerp(tail, tip, 0.5), tip, upper, tip, lower };
        return new RecognizedShape(ShapeKind.Arrow, points);
    }

    // ------------------------------------------------------------------ closed loops

    private static RecognizedShape RecognizeClosed(List<Vec2> path, double closingGap, double span)
    {
        var window = CornerWindowFor(span);

        // A loop drawn by hand usually comes back with its first point repeated at the end.
        // Leaving the duplicate in would put a zero length segment under the corner detector
        // and invent a corner at the seam.
        var loop = closingGap < window ? TrimClosingSeam(path) : path;

        var bounds = BoundsOf(loop);

        // Ink that wanders back and forth inside its own bounding box encloses too little
        // area to be a shape, however neat the box around it looks.
        if (Math.Abs(ShoelaceArea(loop)) < MinimumLoopFill * Math.Max(1e-9, bounds.Width * bounds.Height))
            return RecognizedShape.Unknown;

        var axes = FitAxes(loop);
        if (axes.SemiMinor < SliverRatio * axes.SemiMajor)
            return AsLine(loop);

        var corners = FindCorners(loop, window);

        // Every candidate is scored the same way and the closest fit wins. Comparing scores
        // rather than trusting an order matters because the shapes overlap: a circle sits
        // well inside the rectangle around it, so only the better of the two readings is
        // worth showing the user.
        var bestScore = double.MaxValue;
        RecognizedShape? best = null;

        Consider(AxisAlignedRectangle(axes), ShapeKind.Rectangle, PolygonTolerance * span + ToleranceSlack);

        if (corners.Count >= 3 && TriangleOutline(corners) is { } triangle)
        {
            Consider(triangle, ShapeKind.Triangle, PolygonTolerance * span + ToleranceSlack);
        }

        Consider(EllipseOutline(axes), ShapeKind.Ellipse, EllipseTolerance * span + ToleranceSlack);

        return best ?? RecognizedShape.Unknown;

        void Consider(IReadOnlyList<Vec2> outline, ShapeKind kind, double tolerance)
        {
            var score = MeanDistanceToOutline(loop, outline);
            if (score > tolerance || score >= bestScore)
                return;

            bestScore = score;
            best = new RecognizedShape(kind, outline);
        }
    }

    private static List<Vec2> TrimClosingSeam(List<Vec2> path)
    {
        var trimmed = new List<Vec2>(path);
        while (trimmed.Count > 2 && trimmed[0].DistanceTo(trimmed[^1]) < MinimumPointSpacing)
            trimmed.RemoveAt(trimmed.Count - 1);

        return trimmed;
    }

    /// <summary>
    /// The rectangle spanned by the loop's own principal axes, so it is found whether the
    /// user drew it level with the desk or tilted. Returns the outline, closing point first.
    /// </summary>
    private static List<Vec2> AxisAlignedRectangle(Axes axes) => new()
    {
        axes.Center + (axes.Major * axes.MinProjection) + (axes.Minor * axes.MinAcross),
        axes.Center + (axes.Major * axes.MaxProjection) + (axes.Minor * axes.MinAcross),
        axes.Center + (axes.Major * axes.MaxProjection) + (axes.Minor * axes.MaxAcross),
        axes.Center + (axes.Major * axes.MinProjection) + (axes.Minor * axes.MaxAcross),
        axes.Center + (axes.Major * axes.MinProjection) + (axes.Minor * axes.MinAcross),
    };

    /// <summary>
    /// The triangle a loop of corners describes: its three sharpest turns, put back into loop
    /// order so the outline runs the same way round as the ink. Null when the corners do not
    /// describe three vertices at all.
    /// </summary>
    private static IReadOnlyList<Vec2>? TriangleOutline(List<Corner> corners)
    {
        // Corners are counted by direction change, and a shaky hand produces extra ones at a
        // vertex, so the vertices are taken as the three sharpest turns rather than the first
        // three the detector happened to find. They are then put back into loop order, so the
        // outline runs the same way round as the ink.
        var sharpest = corners
            .OrderByDescending(corner => corner.AngleDegrees)
            .Take(3)
            .OrderBy(corner => corner.Index)
            .ToArray();

        if (sharpest.Length < 3)
            return null;

        var outline = new List<Vec2>(sharpest.Select(corner => corner.Point)) { sharpest[0].Point };
        return outline;
    }

    /// <summary>
    /// The ellipse the loop's own axes describe. The half extents come from the ink, not from
    /// the spread of the samples: a circle's points sit at the radius they were drawn at, and
    /// the covariance of that cloud is smaller than the radius by a factor of root two.
    /// </summary>
    private static IReadOnlyList<Vec2> EllipseOutline(Axes axes)
    {
        var outline = new List<Vec2>(EllipseSegments + 1);
        for (var i = 0; i <= EllipseSegments; i++)
        {
            var angle = (i % EllipseSegments) * (2 * Math.PI / EllipseSegments);
            outline.Add(axes.Center
                + axes.Major * (Math.Cos(angle) * axes.SemiMajor)
                + axes.Minor * (Math.Sin(angle) * axes.SemiMinor));
        }

        return outline;
    }

    // ------------------------------------------------------------------ corners

    /// <summary>
    /// Finds the places where the ink changes direction. Every sample is given the direction
    /// it arrived with and the direction it left with, each measured over the same baseline of
    /// ink, and the angle between the two is the turn at that sample.
    ///
    /// Measuring between neighbouring samples finds nothing: hand drawn samples sit a couple
    /// of pixels apart, so a corner arrives as two gentle kinks and the wobble of the hand
    /// reads as corners on a perfectly straight edge. Measuring across a baseline fixes both,
    /// and it keeps a straight edge at no turn at all while a corner still shows the full
    /// angle of the shape it belongs to.
    /// </summary>
    private static List<Corner> FindCorners(IReadOnlyList<Vec2> path, double window)
    {
        var count = path.Count;
        if (count < 3)
            return new List<Corner>();

        var arc = ArcLengths(path);

        var found = new List<Corner>();
        for (var i = 0; i < count; i++)
        {
            var turn = TurnAt(path, arc, i, window);
            if (turn >= CornerAngleDegrees)
                found.Add(new Corner(i, turn, path[i]));
        }

        return Cluster(found, arc, window);
    }

    /// <summary>
    /// Cumulative arc length along a closed loop, so corners can be told apart by how far
    /// along the ink they sit rather than by how many samples happen to fall between them.
    /// </summary>
    private static double[] ArcLengths(IReadOnlyList<Vec2> path)
    {
        var arc = new double[path.Count + 1];
        for (var i = 0; i < path.Count; i++)
            arc[i + 1] = arc[i] + path[i].DistanceTo(path[(i + 1) % path.Count]);

        return arc;
    }

    /// <summary>The shorter way round a closed loop between two samples.</summary>
    private static double ArcDistance(double[] arc, int from, int to)
    {
        var direct = Math.Abs(arc[from] - arc[to]);
        return Math.Min(direct, arc[^1] - direct);
    }

    /// <summary>
    /// Collapses the samples that all see the same corner into one corner.
    ///
    /// A corner does not read as a single sample: every sample within a window of it sees the
    /// same whole turn through its own window, so one vertex comes back as a run of
    /// neighbouring candidates. Merging those runs by sample count alone is not enough,
    /// because resampling changes how many samples a window covers, and because the run is
    /// broken by samples that fall just short of the threshold. Instead the candidates are
    /// chained along the ink while they stay within a window of each other, and the sharpest
    /// member of each chain wins. The first and last chain are joined when the corner straddles
    /// the seam where the stroke started and ended.
    /// </summary>
    private static List<Corner> Cluster(List<Corner> candidates, double[] arc, double window)
    {
        var gap = window * CornerChainSlack;
        var chains = new List<List<Corner>>();

        foreach (var corner in candidates.OrderBy(candidate => candidate.Index))
        {
            if (chains.Count > 0 && ArcDistance(arc, corner.Index, chains[^1][^1].Index) <= gap)
            {
                chains[^1].Add(corner);
                continue;
            }

            chains.Add(new List<Corner> { corner });
        }

        if (chains.Count > 1 && ArcDistance(arc, chains[0][^1].Index, chains[^1][^1].Index) <= gap)
        {
            chains[0].AddRange(chains[^1]);
            chains.RemoveAt(chains.Count - 1);
        }

        var corners = new List<Corner>(chains.Count);
        foreach (var chain in chains)
            corners.Add(chain.OrderByDescending(member => member.AngleDegrees).First());

        return corners.OrderBy(corner => corner.Index).ToList();
    }

    /// <summary>
    /// How much the ink changes direction at one sample, measured between the baseline it
    /// arrived on and the baseline it leaves on. A straight stretch reads as no turn, the
    /// corner of a square as a right angle, and the vertex of an equilateral triangle as
    /// its full 120 degrees, so the number is the change of travel direction rather than the
    /// interior angle of the shape.
    /// </summary>
    private static double TurnAt(IReadOnlyList<Vec2> path, double[] arc, int index, double window)
    {
        var here = path[index];
        var incoming = here - PointAlong(path, arc, index, -window);
        var outgoing = PointAlong(path, arc, index, window) - here;
        if (incoming.Length < 1e-9 || outgoing.Length < 1e-9)
            return 0;

        return AngleBetween(incoming, outgoing);
    }

    /// <summary>
    /// The point a given distance along the ink from a sample, in either direction. The loop
    /// closes on itself, so walking past either end carries on round the far side.
    /// </summary>
    private static Vec2 PointAlong(IReadOnlyList<Vec2> path, double[] arc, int index, double distance)
    {
        var total = arc[^1];
        if (total < 1e-9)
            return path[index];

        var target = (arc[index] + distance) % total;
        if (target < 0)
            target += total;

        var low = 0;
        var high = path.Count;
        while (high - low > 1)
        {
            var middle = (low + high) / 2;
            if (arc[middle] <= target)
                low = middle;
            else
                high = middle;
        }

        var from = path[low];
        var to = path[(low + 1) % path.Count];
        var segment = arc[low + 1] - arc[low];
        var share = segment < 1e-9 ? 0 : (target - arc[low]) / segment;

        return Vec2.Lerp(from, to, share);
    }

    // ------------------------------------------------------------------ geometry

    /// <summary>Drops samples that repeat the previous position, which pen input is full of.</summary>
    private static List<Vec2> Clean(IReadOnlyList<Vec2> path)
    {
        var cleaned = new List<Vec2>(path.Count) { path[0] };

        for (var i = 1; i < path.Count; i++)
        {
            if (path[i].DistanceTo(cleaned[^1]) >= MinimumPointSpacing)
                cleaned.Add(path[i]);
        }

        return cleaned;
    }

    /// <summary>Walks the stroke at a fixed arc length, so every test sees an even spread.</summary>
    private static List<Vec2> Resample(IReadOnlyList<Vec2> path, double step)
    {
        var resampled = new List<Vec2> { path[0] };
        var needed = step;

        for (var i = 1; i < path.Count; i++)
        {
            var from = path[i - 1];
            var to = path[i];
            var segment = to - from;
            var segmentLength = segment.Length;
            if (segmentLength < 1e-9)
                continue;

            var travelled = 0d;
            while (segmentLength - travelled >= needed)
            {
                travelled += needed;
                needed = step;
                resampled.Add(from + (segment * (travelled / segmentLength)));
            }

            needed -= segmentLength - travelled;
        }

        // The end the user lifted the pen at always belongs to the path. Dropping it would
        // shorten every stroke by up to one step, and a loop drawn back to where it started
        // would look open.
        if (resampled[^1].DistanceTo(path[^1]) > MinimumPointSpacing)
            resampled.Add(path[^1]);

        return resampled;
    }

    /// <summary>
    /// The sample spacing for a stroke of this size: fine enough that a corner covers several
    /// samples, coarse enough that the detector stays cheap on a long stroke.
    /// </summary>
    private static double ResampleStepFor(double span) =>
        Math.Max(MinimumResampleStep, span / ResampleTarget);

    private static double CornerWindowFor(double span) =>
        Math.Clamp(span / CornerWindowDivisor, MinimumCornerWindow, MaximumCornerWindow);

    private static double PathLength(IReadOnlyList<Vec2> path)
    {
        var total = 0d;
        for (var i = 1; i < path.Count; i++)
            total += path[i].DistanceTo(path[i - 1]);

        return total;
    }

    private static Bounds BoundsOf(IReadOnlyList<Vec2> path)
    {
        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;

        foreach (var point in path)
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        return new Bounds(minX, minY, maxX, maxY);
    }

    private static double ShoelaceArea(IReadOnlyList<Vec2> path)
    {
        var total = 0d;
        for (var i = 1; i < path.Count; i++)
            total += (path[i - 1].X * path[i].Y) - (path[i].X * path[i - 1].Y);

        return Math.Abs(total) / 2;
    }

    /// <summary>
    /// The principal axes of a point cloud. This is the compact way to say "which way does
    /// this stroke run", and it is what lets an ellipse or a tilted rectangle be recognised
    /// without trying every possible rotation.
    /// The half extents along those axes travel with them, because a candidate shape is built
    /// from the ink's own reach rather than from how tightly the samples cluster.
    /// </summary>
    private static Axes FitAxes(IReadOnlyList<Vec2> points)
    {
        var center = Vec2.Zero;
        foreach (var point in points)
            center += point;

        center /= points.Count;

        var sxx = 0d;
        var syy = 0d;
        var sxy = 0d;
        foreach (var point in points)
        {
            var dx = point.X - center.X;
            var dy = point.Y - center.Y;
            sxx += dx * dx;
            syy += dy * dy;
            sxy += dx * dy;
        }

        var theta = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
        var major = new Vec2(Math.Cos(theta), Math.Sin(theta));
        var minor = major.Perp();

        var minProjection = double.MaxValue;
        var maxProjection = double.MinValue;
        var minAcross = double.MaxValue;
        var maxAcross = double.MinValue;

        foreach (var point in points)
        {
            var offset = point - center;
            minProjection = Math.Min(minProjection, Vec2.Dot(offset, major));
            maxProjection = Math.Max(maxProjection, Vec2.Dot(offset, major));
            minAcross = Math.Min(minAcross, Vec2.Dot(offset, minor));
            maxAcross = Math.Max(maxAcross, Vec2.Dot(offset, minor));
        }

        return new Axes(
            center,
            major,
            minor,
            (maxProjection - minProjection) / 2,
            (maxAcross - minAcross) / 2,
            minProjection,
            maxProjection,
            minAcross,
            maxAcross);
    }

    /// <summary>
    /// How far the drawn points sit from a candidate outline, on average. Averaging rather
    /// than taking the worst point is what makes the test forgiving of a shaky hand without
    /// also forgiving of the wrong shape.
    /// </summary>
    private static double MeanDistanceToOutline(IReadOnlyList<Vec2> points, IReadOnlyList<Vec2> outline)
    {
        if (points.Count == 0 || outline.Count < 2)
            return double.MaxValue;

        var total = 0d;
        foreach (var point in points)
        {
            var nearest = double.MaxValue;
            for (var i = 1; i < outline.Count; i++)
                nearest = Math.Min(nearest, Vec2.DistanceToSegment(point, outline[i - 1], outline[i]));

            total += nearest;
        }

        return total / points.Count;
    }

    private static double AngleBetween(Vec2 a, Vec2 b)
    {
        var denominator = a.Length * b.Length;
        if (denominator < 1e-9)
            return 0;

        return Math.Acos(Math.Clamp(Vec2.Dot(a, b) / denominator, -1, 1)) * 180 / Math.PI;
    }

    /// <summary>The stroke's own ends, as the two extreme points along its principal axis.</summary>
    private static RecognizedShape AsLine(IReadOnlyList<Vec2> points)
    {
        var axes = FitAxes(points);
        var start = points[0];
        var end = points[^1];
        var lowest = double.MaxValue;
        var highest = double.MinValue;

        foreach (var point in points)
        {
            var projection = Vec2.Dot(point - axes.Center, axes.Major);
            if (projection < lowest)
            {
                lowest = projection;
                start = point;
            }

            if (projection > highest)
            {
                highest = projection;
                end = point;
            }
        }

        return new RecognizedShape(ShapeKind.Line, new[] { start, end });
    }

    // ------------------------------------------------------------------ records

    private readonly record struct Corner(int Index, double AngleDegrees, Vec2 Point);

    private readonly record struct Bounds(double MinX, double MinY, double MaxX, double MaxY)
    {
        public double Width => MaxX - MinX;

        public double Height => MaxY - MinY;

        /// <summary>The larger of the two extents: the yardstick every threshold is set in.</summary>
        public double Span => Math.Max(Width, Height);
    }

    private readonly record struct Axes(
        Vec2 Center,
        Vec2 Major,
        Vec2 Minor,
        double SemiMajor,
        double SemiMinor,
        double MinProjection,
        double MaxProjection,
        double MinAcross,
        double MaxAcross);
}
