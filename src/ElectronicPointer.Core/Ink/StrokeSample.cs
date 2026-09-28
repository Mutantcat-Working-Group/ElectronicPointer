using Mutantcat.ElectronicPointer.Core.Geometry;

namespace Mutantcat.ElectronicPointer.Core.Ink;

/// <summary>
/// One input sample. Pressure is normalised to 0..1 and falls back to
/// <see cref="NeutralPressure"/> when the device does not report it.
/// </summary>
public readonly record struct StrokeSample(Vec2 Point, double Pressure)
{
    public const double NeutralPressure = 0.5;

    public static StrokeSample Create(Vec2 point, double? pressure)
        => new(point, pressure is > 0 and <= 1 ? pressure.Value : NeutralPressure);
}
