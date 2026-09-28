using Mutantcat.ElectronicPointer.Core.Board;

namespace Mutantcat.ElectronicPointer.Core.History;

/// <summary>
/// Sets or clears the frozen-screen image behind the ink. Undoing a freeze puts back what
/// the page had before, which may be nothing at all.
/// </summary>
public sealed class SetBackgroundCommand : IBoardCommand
{
    private readonly BoardPage _page;
    private readonly BoardBackground? _background;
    private BoardBackground? _previous;

    public SetBackgroundCommand(BoardPage page, BoardBackground? background)
    {
        _page = page;
        _background = background;
        _previous = page.BackgroundImage;
    }

    public string Label => _background is null ? "取消背景图" : "冻结屏幕";

    public void Apply(BoardDocument document)
    {
        _previous = _page.BackgroundImage;
        _page.BackgroundImage = _background;
        _page.Touch();
    }

    public void Revert(BoardDocument document)
    {
        _page.BackgroundImage = _previous;
        _page.Touch();
    }
}
