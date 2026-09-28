using Mutantcat.ElectronicPointer.Core.Board;
using Mutantcat.ElectronicPointer.Core.Geometry;
using Mutantcat.ElectronicPointer.Core.History;
using Mutantcat.ElectronicPointer.Core.Ink;
using Xunit;

namespace Mutantcat.ElectronicPointer.Core.Tests.History;

public class StrokeMovementTests
{
    private static (BoardDocument Document, Stroke Stroke) DocumentWithStroke()
    {
        var document = BoardDocument.CreateDefault();
        var stroke = new Stroke();
        stroke.Append(new Vec2(10, 10), 0.5);
        stroke.Append(new Vec2(20, 20), 0.5);
        stroke.Complete();
        document.ActivePage.Strokes.Add(stroke);
        return (document, stroke);
    }

    [Fact]
    public void MoveStrokes_ShiftsEverySampleAnd_Undo_PutsItBack()
    {
        var (document, stroke) = DocumentWithStroke();
        var stack = new UndoRedoStack();
        var command = new MoveStrokesCommand(new[] { stroke }, new Vec2(100, -25));

        stack.Execute(document, command);

        Assert.Equal(new Vec2(110, -15), stroke.Samples[0].Point);
        Assert.Equal(new Vec2(120, -5), stroke.Samples[1].Point);

        Assert.True(stack.Undo(document));
        Assert.Equal(new Vec2(10, 10), stroke.Samples[0].Point);
        Assert.Equal(new Vec2(20, 20), stroke.Samples[1].Point);

        Assert.True(stack.Redo(document));
        Assert.Equal(new Vec2(110, -15), stroke.Samples[0].Point);
        Assert.Equal(new Vec2(120, -5), stroke.Samples[1].Point);
    }

    [Fact]
    public void MoveStrokes_MovesOnlyTheStrokesItWasGiven()
    {
        var document = BoardDocument.CreateDefault();
        var moved = new Stroke();
        moved.Append(new Vec2(10, 10), 0.5);
        moved.Append(new Vec2(20, 20), 0.5);
        moved.Complete();

        var leftAlone = new Stroke();
        leftAlone.Append(new Vec2(100, 100), 0.5);
        leftAlone.Append(new Vec2(120, 120), 0.5);
        leftAlone.Complete();

        document.ActivePage.Strokes.Add(moved);
        document.ActivePage.Strokes.Add(leftAlone);
        var stack = new UndoRedoStack();

        stack.Execute(document, new MoveStrokesCommand(new[] { moved }, new Vec2(1, 1)));

        Assert.Equal(new Vec2(11, 11), moved.Samples[0].Point);
        Assert.Equal(new Vec2(100, 100), leftAlone.Samples[0].Point);
    }

    [Fact]
    public void MoveStrokes_ZeroOffset_LeavesRevisionAlone()
    {
        var (document, stroke) = DocumentWithStroke();
        var revision = stroke.Revision;

        stroke.Translate(Vec2.Zero);

        Assert.Equal(revision, stroke.Revision);
    }

    [Fact]
    public void MoveStrokes_KeepsPressureIntact()
    {
        var (document, stroke) = DocumentWithStroke();
        stroke.Samples[0] = new StrokeSample(new Vec2(10, 10), 0.25);

        stroke.Translate(new Vec2(5, 5));

        Assert.Equal(0.25, stroke.Samples[0].Pressure);
    }
}
