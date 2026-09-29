using Mutantcat.ElectronicPointer.Core.Geometry;
using Mutantcat.ElectronicPointer.Core.Recognition;

namespace Mutantcat.ElectronicPointer.Platform.Recognition;

/// <summary>
/// One stroke the recognizer read, plus the clean geometry that should stand in for it.
/// <see cref="Index"/> is the position the stroke held in the list that was handed in, so
/// the caller never has to match strokes back up by identity.
/// </summary>
public sealed record RecognizedInk(int Index, ShapeKind Kind, IReadOnlyList<Vec2> Points)
{
    public RecognizedInk(int index, RecognizedShape shape)
        : this(index, shape.Kind, shape.Points)
    {
    }
}

/// <summary>
/// What the recognizer made of a run of strokes. Strokes it could not read are left out
/// rather than reported as failures, because ink the user drew on purpose should stay.
/// </summary>
public sealed record RecognitionReport(IReadOnlyList<RecognizedInk> Ink, string Summary)
{
    public static RecognitionReport Nothing { get; } = new(Array.Empty<RecognizedInk>(), "没有识别到形状");

    public int Count => Ink.Count;
}
