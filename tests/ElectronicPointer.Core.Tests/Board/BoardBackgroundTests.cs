using Mutantcat.ElectronicPointer.Core.Board;
using Mutantcat.ElectronicPointer.Core.History;
using Xunit;

namespace Mutantcat.ElectronicPointer.Core.Tests.Board;

public class BoardBackgroundTests
{
    private static byte[] Pixels(int width, int height, byte value)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i++)
            pixels[i] = value;

        return pixels;
    }

    [Fact]
    public void BoardBackground_RejectsAnImageSmallerThanItsSize()
    {
        Assert.Throws<ArgumentException>(() => new BoardBackground(new byte[8], 4, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoardBackground(Pixels(4, 4, 0), 0, 4));
    }

    [Fact]
    public void BoardBackground_ReportsStrideThatMatchesThePromise()
    {
        var background = new BoardBackground(Pixels(7, 3, 0), 7, 3);

        Assert.Equal(28, background.Stride);
        Assert.Equal(7, background.Width);
        Assert.Equal(3, background.Height);
    }

    [Fact]
    public void SetBackground_SnapshotsAndRestoresThroughTheHistory()
    {
        var document = BoardDocument.CreateDefault();
        var page = document.ActivePage;
        var first = new BoardBackground(Pixels(4, 4, 1), 4, 4);
        var second = new BoardBackground(Pixels(4, 4, 2), 4, 4);
        var stack = new UndoRedoStack();

        Assert.Null(page.BackgroundImage);

        stack.Execute(document, new SetBackgroundCommand(page, first));
        Assert.Same(first, page.BackgroundImage);

        stack.Execute(document, new SetBackgroundCommand(page, second));
        Assert.Same(second, page.BackgroundImage);

        Assert.True(stack.Undo(document));
        Assert.Same(first, page.BackgroundImage);

        Assert.True(stack.Undo(document));
        Assert.Null(page.BackgroundImage);
        Assert.Equal("冻结屏幕", new SetBackgroundCommand(page, first).Label);

        Assert.True(stack.Redo(document));
        Assert.Same(first, page.BackgroundImage);
    }
}
