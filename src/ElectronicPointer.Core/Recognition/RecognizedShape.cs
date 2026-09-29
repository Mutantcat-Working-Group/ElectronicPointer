using Mutantcat.ElectronicPointer.Core.Geometry;

namespace Mutantcat.ElectronicPointer.Core.Recognition;

/// <summary>
/// One stroke the recognizer managed to read, together with the clean geometry that should
/// take its place. The points are expressed in the same board coordinates as the input, so
/// replacing a stroke's samples with them moves nothing.
/// </summary>
public sealed record RecognizedShape(ShapeKind Kind, IReadOnlyList<Vec2> Points)
{
    /// <summary>What an unreadable stroke comes back as: no shape, nothing to replace.</summary>
    public static RecognizedShape Unknown { get; } = new(ShapeKind.None, Array.Empty<Vec2>());

    public bool IsRecognized => Kind != ShapeKind.None && Points.Count >= 2;
}
