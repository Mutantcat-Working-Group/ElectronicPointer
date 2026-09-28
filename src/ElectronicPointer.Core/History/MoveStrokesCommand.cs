using Mutantcat.ElectronicPointer.Core.Board;
using Mutantcat.ElectronicPointer.Core.Geometry;
using Mutantcat.ElectronicPointer.Core.Ink;

namespace Mutantcat.ElectronicPointer.Core.History;

/// <summary>
/// Moves a group of strokes by one delta, as produced by dragging a lasso selection. Apply
/// and revert shift the same samples in opposite directions, so the ink keeps its shape and
/// its place in the z-order.
/// </summary>
public sealed class MoveStrokesCommand : IBoardCommand
{
    private readonly Stroke[] _strokes;
    private readonly Vec2 _delta;

    public MoveStrokesCommand(IReadOnlyList<Stroke> strokes, Vec2 delta)
    {
        _strokes = strokes.ToArray();
        _delta = delta;
    }

    public string Label => "移动笔迹";

    public void Apply(BoardDocument document)
    {
        Move(_delta);
        document.Touch();
    }

    public void Revert(BoardDocument document)
    {
        Move(-_delta);
        document.Touch();
    }

    private void Move(Vec2 delta)
    {
        foreach (var stroke in _strokes)
            stroke.Translate(delta);
    }
}
