using Mutantcat.ElectronicPointer.Core.Geometry;

namespace Mutantcat.ElectronicPointer.Core.Ink;

/// <summary>
/// Managed port of the perfect-freehand stroke generator. It turns raw input samples
/// into a variable-width outline with pressure driven width, tapered ends and rounded
/// caps, which is what makes the ink read as a pen rather than a polyline.
/// </summary>
public static class Freehand
{
    /// <summary>Rate of change for simulated pressure, matching the upstream library.</summary>
    private const double RateOfPressureChange = 0.275;

    private const double FixedPi = Math.PI + 0.0001;

    private const double DefaultPressure = 0.5;

    private const double FirstPointPressure = 0.25;

    private const int TaperSteps = 13;

    private static double EaseInOut(double t) => t * (2 - t);

    private static double EaseOutCubic(double t) => --t * t * t + 1;

    /// <summary>
    /// Computes the normalized stroke points: interpolated, streamlined, and carrying the
    /// pressure, direction, segment distance and running length the outline needs.
    /// </summary>
    public static IReadOnlyList<StrokePoint> GetStrokePoints(IReadOnlyList<StrokeSample> samples, StrokeOptions options)
    {
        if (samples.Count == 0)
            return Array.Empty<StrokePoint>();

        var streamline = options.Streamline;
        var size = options.Size;
        var isComplete = options.Last;

        // Interpolation level between the incoming points.
        var t = 0.15 + (1 - streamline) * 0.85;

        var px = new List<Vec2>(samples.Count);
        var pp = new List<double>(samples.Count);
        foreach (var sample in samples)
        {
            px.Add(sample.Point);
            pp.Add(sample.Pressure);
        }

        // Two samples only would render as a flat bar, so insert interpolated samples in
        // between. Mutate copies only, never the caller's collection.
        if (px.Count == 2)
        {
            var lastPoint = px[1];
            var lastPressure = pp[1];
            px.RemoveAt(1);
            pp.RemoveAt(1);
            for (var i = 1; i < 5; i++)
            {
                px.Add(Vec2.Lerp(px[0], lastPoint, i / 4.0));
                pp.Add(lastPressure);
            }
        }

        // A single sample becomes a dot, which needs a neighbour to define a direction.
        if (px.Count == 1)
        {
            px.Add(new Vec2(px[0].X + 1, px[0].Y + 1));
            pp.Add(pp[0]);
        }

        var strokePoints = new List<StrokePoint>(px.Count)
        {
            new StrokePoint(px[0], pp[0] > 0 ? pp[0] : FirstPointPressure, new Vec2(1, 1), 0, 0),
        };

        var hasReachedMinimumLength = false;
        var runningLength = 0.0;
        var prev = strokePoints[0];
        var max = px.Count - 1;

        for (var i = 1; i < px.Count; i++)
        {
            var point = isComplete && i == max
                ? px[i]
                : Vec2.Lerp(prev.Point, px[i], t);

            // Ignore jitter that does not move the point.
            if (point.NearlyEquals(prev.Point))
                continue;

            var distance = point.DistanceTo(prev.Point);
            runningLength += distance;

            // Skip the initial wiggle until the stroke is long enough to have a direction.
            if (i < max && !hasReachedMinimumLength)
            {
                if (runningLength < size)
                    continue;
                hasReachedMinimumLength = true;
            }

            prev = new StrokePoint(
                point,
                pp[i] > 0 ? pp[i] : DefaultPressure,
                (prev.Point - point).Normalize(),
                distance,
                runningLength);
            strokePoints.Add(prev);
        }

        // The first point has no predecessor, so borrow the second point's direction.
        strokePoints[0] = strokePoints.Count > 1
            ? strokePoints[0] with { Vector = strokePoints[1].Vector }
            : strokePoints[0] with { Vector = Vec2.Zero };

        return strokePoints;
    }

    /// <summary>
    /// Builds the outline of a stroke: left side, end cap, right side (reversed) and
    /// start cap, in winding order for a filled polygon.
    /// </summary>
    public static IReadOnlyList<Vec2> GetStrokeOutlinePoints(IReadOnlyList<StrokePoint> points, StrokeOptions options)
    {
        var size = options.Size;
        var thinning = options.Thinning;
        var smoothing = options.Smoothing;
        var simulatePressure = options.SimulatePressure;
        var easing = options.EasingOrDefault;
        var start = options.Start;
        var end = options.End;
        var isComplete = options.Last;

        if (points.Count == 0 || size <= 0)
            return Array.Empty<Vec2>();

        var totalLength = points[^1].RunningLength;

        var capStart = start?.Cap ?? true;
        var taperStart = start is { TaperEnabled: true } ? Math.Max(size, totalLength) : start?.Taper ?? 0;
        var taperStartEase = start?.Easing ?? EaseInOut;

        var capEnd = end?.Cap ?? true;
        var taperEnd = end is { TaperEnabled: true } ? Math.Max(size, totalLength) : end?.Taper ?? 0;
        var taperEndEase = end?.Easing ?? EaseOutCubic;

        // Minimum allowed distance between outline points, squared.
        var minDistance = Math.Pow(size * smoothing, 2);

        var leftPts = new List<Vec2>(points.Count);
        var rightPts = new List<Vec2>(points.Count);

        // Starting pressure uses the average of the first few points so that lines do not
        // all begin fat.
        var prevPressure = AveragedStartingPressure(points, simulatePressure, size);

        var radius = GetStrokeRadius(size, thinning, points[^1].Pressure, easing);
        double firstRadius = double.NaN;

        var prevVector = points[0].Vector;
        var pl = points[0].Point;
        var pr = pl;
        var tl = pl;
        var tr = pr;
        var isPrevPointSharpCorner = false;

        for (var index = 0; index < points.Count; index++)
        {
            var sp = points[index];
            var pressure = sp.Pressure;
            var point = sp.Point;
            var vector = sp.Vector;
            var distance = sp.Distance;
            var runningLength = sp.RunningLength;

            // Trim noise from the tail of the line.
            if (index < points.Count - 1 && totalLength - runningLength < 3)
                continue;

            // Pressure sensitive width. When thinning is disabled the width stays constant,
            // which is what a highlighter needs.
            if (thinning > 0)
            {
                if (simulatePressure)
                {
                    var spd = Math.Min(1, distance / size);
                    var rp = Math.Min(1, 1 - spd);
                    pressure = Math.Min(1, prevPressure + ((rp - prevPressure) * (spd * RateOfPressureChange)));
                }

                radius = GetStrokeRadius(size, thinning, pressure, easing);
            }
            else
            {
                radius = size / 2;
            }

            if (double.IsNaN(firstRadius))
                firstRadius = radius;

            // Taper the start and the end, using whichever taper is stronger.
            var ts = runningLength < taperStart ? taperStartEase(runningLength / taperStart) : 1;
            var te = runningLength < taperEnd ? taperEndEase(runningLength / taperEnd) : 1;
            radius = Math.Max(0.01, radius * Math.Min(ts, te));

            // Sharp corners get a rounded fan instead of the projected offset, otherwise
            // the outline folds over itself.
            var nextVector = index < points.Count - 1 ? points[index + 1].Vector : points[index].Vector;
            var nextDpr = index < points.Count - 1 ? Vec2.Dot(vector, nextVector) : 1.0;
            var prevDpr = Vec2.Dot(vector, prevVector);

            var isPointSharpCorner = prevDpr < 0 && !isPrevPointSharpCorner;
            var isNextPointSharpCorner = nextDpr < 0;

            if (isPointSharpCorner || isNextPointSharpCorner)
            {
                var offset = prevVector.Perp() * radius;
                var step = 1.0 / TaperSteps;
                var t = 0.0;
                for (; t <= 1.0; t += step)
                {
                    tl = (point - offset).RotateAround(point, FixedPi * t);
                    leftPts.Add(tl);

                    tr = (point + offset).RotateAround(point, FixedPi * -t);
                    rightPts.Add(tr);
                }

                pl = tl;
                pr = tr;

                if (isNextPointSharpCorner)
                    isPrevPointSharpCorner = true;

                continue;
            }

            isPrevPointSharpCorner = false;

            // The last point gets a single offset pair and no projection.
            if (index == points.Count - 1)
            {
                var offset = vector.Perp() * radius;
                leftPts.Add(point - offset);
                rightPts.Add(point + offset);
                continue;
            }

            var projected = Vec2.Lerp(nextVector, vector, nextDpr).Perp() * radius;

            tl = point - projected;
            if (index <= 1 || (pl - tl).LengthSquared > minDistance)
            {
                leftPts.Add(tl);
                pl = tl;
            }

            tr = point + projected;
            if (index <= 1 || (pr - tr).LengthSquared > minDistance)
            {
                rightPts.Add(tr);
                pr = tr;
            }

            prevPressure = pressure;
            prevVector = vector;
        }

        var firstPoint = points[0].Point;
        var lastPoint = points.Count > 1 ? points[^1].Point : points[0].Point + Vec2.One;

        var startCap = new List<Vec2>();
        var endCap = new List<Vec2>();

        var taperedStart = taperStart != 0 && start is { TaperEnabled: true };
        var taperedEnd = taperEnd != 0 && end is { TaperEnabled: true };

        if (points.Count == 1)
        {
            // A single sample that is not tapered renders as a dot.
            if ((!taperedStart && !taperedEnd) || isComplete)
            {
                var direction = (firstPoint - lastPoint).Normalize().Perp();
                var capPoint = firstPoint + (direction * (double.IsNaN(firstRadius) ? -radius : -firstRadius));
                var dotPts = new List<Vec2>(TaperSteps);
                var step = 1.0 / TaperSteps;
                var t = step;
                for (; t <= 1.0; t += step)
                    dotPts.Add(capPoint.RotateAround(firstPoint, FixedPi * 2 * t));

                return dotPts;
            }
        }
        else
        {
            if (taperedStart || (taperedEnd && points.Count == 1))
            {
                // Tapered start: nothing to draw.
            }
            else if (capStart)
            {
                var step = 1.0 / TaperSteps;
                var t = step;
                for (; t <= 1.0; t += step)
                    startCap.Add(rightPts[0].RotateAround(firstPoint, FixedPi * t));
            }
            else
            {
                var corners = leftPts[0] - rightPts[0];
                var offsetA = corners * 0.5;
                var offsetB = corners * 0.51;
                startCap.Add(firstPoint - offsetA);
                startCap.Add(firstPoint - offsetB);
                startCap.Add(firstPoint + offsetA);
                startCap.Add(firstPoint + offsetB);
            }

            var direction = (points[^1].Vector * -1).Perp();

            if (taperedEnd || (taperedStart && points.Count == 1))
            {
                endCap.Add(lastPoint);
            }
            else if (capEnd)
            {
                var capPoint = lastPoint + (direction * radius);
                var step = 1.0 / 29.0;
                var t = step;
                for (; t < 1.0; t += step)
                    endCap.Add(capPoint.RotateAround(lastPoint, FixedPi * 3 * t));
            }
            else
            {
                endCap.Add(lastPoint + (direction * radius));
                endCap.Add(lastPoint + (direction * (radius * 0.99)));
                endCap.Add(lastPoint - (direction * radius));
                endCap.Add(lastPoint - (direction * (radius * 0.99)));
            }
        }

        rightPts.Reverse();

        var result = new List<Vec2>(leftPts.Count + endCap.Count + rightPts.Count + startCap.Count);
        result.AddRange(leftPts);
        result.AddRange(endCap);
        result.AddRange(rightPts);
        result.AddRange(startCap);
        return result;
    }

    public static double GetStrokeRadius(double size, double thinning, double pressure, Func<double, double> easing)
        => size * easing(0.5 - (thinning * (0.5 - pressure)));

    private static double AveragedStartingPressure(
        IReadOnlyList<StrokePoint> points,
        bool simulatePressure,
        double size)
    {
        var take = Math.Min(points.Count, 10);
        var accumulator = points[0].Pressure;
        for (var i = 0; i < take; i++)
        {
            var pressure = points[i].Pressure;
            if (simulatePressure)
            {
                // Speed of change (how far this segment travelled) and rate of change.
                var sp = Math.Min(1, points[i].Distance / size);
                var rp = Math.Min(1, 1 - sp);
                pressure = Math.Min(1, accumulator + ((rp - accumulator) * (sp * RateOfPressureChange)));
            }

            accumulator = (accumulator + pressure) / 2;
        }

        return accumulator;
    }
}
