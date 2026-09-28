using Mutantcat.ElectronicPointer.Core.Board;
using Mutantcat.ElectronicPointer.Core.Geometry;
using Mutantcat.ElectronicPointer.Core.History;
using Mutantcat.ElectronicPointer.Core.Ink;
using Xunit;

namespace Mutantcat.ElectronicPointer.Core.Tests.History;

/// <summary>
/// <see cref="UndoRedoStack.Push"/> is the second way into the history, used by gestures that
/// mutate the board as they run. These tests pin the contract that keeps it honest: the
/// caller owns the mutation, the stack only records it, and every entry still stands for
/// exactly one undo step.
/// </summary>
public class UndoRedoStackPushTests
{
    private static Stroke StrokeAt(double x, double y)
    {
        var stroke = new Stroke();
        stroke.Append(new Vec2(x, y), 0.5);
        stroke.Append(new Vec2(x + 10, y + 10), 0.5);
        stroke.Complete();
        return stroke;
    }

    [Fact]
    public void Push_DoesNotApplyTheCommandASecondTime()
    {
        var document = BoardDocument.CreateDefault();
        var stroke = StrokeAt(10, 10);
        // The gesture already put the stroke on the page; Push must not repeat that.
        document.ActivePage.Strokes.Add(stroke);

        var stack = new UndoRedoStack();
        stack.Push(document, new AddStrokeCommand(document.ActivePage, stroke));

        Assert.Equal(1, document.ActivePage.StrokeCount);
        Assert.Same(stroke, document.ActivePage.Strokes[0]);
    }

    [Fact]
    public void Push_StillCountsAsOneUndoStep()
    {
        var document = BoardDocument.CreateDefault();
        var stroke = StrokeAt(10, 10);
        document.ActivePage.Strokes.Add(stroke);

        var stack = new UndoRedoStack();
        stack.Push(document, new AddStrokeCommand(document.ActivePage, stroke));

        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);
        Assert.Equal(1, stack.UndoCount);

        Assert.True(stack.Undo(document));
        Assert.Equal(0, document.ActivePage.StrokeCount);
        Assert.True(stack.CanRedo);
        Assert.Equal(1, stack.RedoCount);
    }

    [Fact]
    public void Push_MarksTheDocumentAsChanged()
    {
        var document = BoardDocument.CreateDefault();
        var before = document.Revision;
        var stroke = StrokeAt(10, 10);
        document.ActivePage.Strokes.Add(stroke);

        new UndoRedoStack().Push(document, new AddStrokeCommand(document.ActivePage, stroke));

        Assert.True(document.Revision > before, "Push should mark the document dirty.");
    }

    [Fact]
    public void Push_AfterAnExecute_UndoesOneStepAtATime()
    {
        var document = BoardDocument.CreateDefault();
        var page = document.ActivePage;

        var executed = StrokeAt(10, 10);
        var pushed = StrokeAt(100, 100);
        // Pushed entry is applied by hand, exactly as a live gesture would have it.
        page.Strokes.Add(pushed);

        var stack = new UndoRedoStack();
        stack.Execute(document, new AddStrokeCommand(page, executed));
        stack.Push(document, new AddStrokeCommand(page, pushed));

        Assert.Equal(2, page.StrokeCount);
        Assert.Equal(2, stack.UndoCount);

        // One undo steps back over the pushed stroke only.
        Assert.True(stack.Undo(document));
        Assert.Equal(1, page.StrokeCount);
        Assert.Same(executed, page.Strokes[0]);

        Assert.True(stack.Undo(document));
        Assert.Equal(0, page.StrokeCount);

        // And both come back, in order.
        Assert.True(stack.Redo(document));
        Assert.Same(executed, page.Strokes[0]);
        Assert.True(stack.Redo(document));
        Assert.Equal(2, page.StrokeCount);
    }

    [Fact]
    public void Push_AfterRedoHistoryExists_DropsTheRedoBranch()
    {
        var document = BoardDocument.CreateDefault();
        var page = document.ActivePage;
        var first = StrokeAt(10, 10);

        var stack = new UndoRedoStack();
        stack.Execute(document, new AddStrokeCommand(page, first));
        Assert.True(stack.Undo(document));
        Assert.True(stack.CanRedo);

        var second = StrokeAt(50, 50);
        page.Strokes.Add(second);
        stack.Push(document, new AddStrokeCommand(page, second));

        Assert.False(stack.CanRedo);
        Assert.Equal(0, stack.RedoCount);
    }

    [Fact]
    public void Push_PastTheLimit_DropsTheOldestEntry()
    {
        var document = BoardDocument.CreateDefault();
        var page = document.ActivePage;
        var stack = new UndoRedoStack { Limit = 2 };

        for (var i = 0; i < 3; i++)
        {
            var stroke = StrokeAt(i * 10, i * 10);
            page.Strokes.Add(stroke);
            stack.Push(document, new AddStrokeCommand(page, stroke));
        }

        Assert.Equal(2, stack.UndoCount);

        // Two undos empty the page, proving the third stroke is the one that was dropped.
        Assert.True(stack.Undo(document));
        Assert.True(stack.Undo(document));
        Assert.Equal(1, page.StrokeCount);
        Assert.False(stack.CanUndo);
    }

    [Fact]
    public void Push_ReportsStateChangesToSubscribers()
    {
        var document = BoardDocument.CreateDefault();
        var stroke = StrokeAt(10, 10);
        document.ActivePage.Strokes.Add(stroke);

        var stack = new UndoRedoStack();
        var raised = 0;
        stack.StateChanged += (_, _) => raised++;

        stack.Push(document, new AddStrokeCommand(document.ActivePage, stroke));

        Assert.Equal(1, raised);
    }
}
