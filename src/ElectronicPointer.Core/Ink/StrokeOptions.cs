namespace Mutantcat.ElectronicPointer.Core.Ink;

/// <summary>
/// Options for the perfect-freehand style outline generator. The defaults mirror the
/// upstream library so strokes look the same on every platform.
/// </summary>
public sealed record StrokeOptions(
    double Size = 16,
    double Thinning = 0.5,
    double Smoothing = 0.5,
    double Streamline = 0.5,
    bool SimulatePressure = true,
    Func<double, double>? Easing = null,
    StrokeCapOptions? Start = null,
    StrokeCapOptions? End = null,
    bool Last = false)
{
    public Func<double, double> EasingOrDefault => Easing ?? (t => t);
}

/// <summary>
/// End-cap description. A tapered end shrinks the stroke towards the tip; a capped end
/// draws a rounded (or flat) cap.
/// </summary>
public sealed record StrokeCapOptions(
    bool Cap = true,
    double Taper = 0,
    bool TaperEnabled = false,
    Func<double, double>? Easing = null);

/// <summary>A generated stroke point with the derived data the outline needs.</summary>
public readonly record struct StrokePoint(
    Mutantcat.ElectronicPointer.Core.Geometry.Vec2 Point,
    double Pressure,
    Mutantcat.ElectronicPointer.Core.Geometry.Vec2 Vector,
    double Distance,
    double RunningLength);
