using Mutantcat.ElectronicPointer.Core.Board;
using Mutantcat.ElectronicPointer.Core.Ink;

namespace Mutantcat.ElectronicPointer.Core.History;

public sealed class AddStrokeCommand : IBoardCommand
{
    private readonly BoardPage _page;
    private readonly Stroke _stroke;

    public AddStrokeCommand(BoardPage page, Stroke stroke)
    {
        _page = page;
        _stroke = stroke;
    }

    public string Label => "新增笔迹";

    public void Apply(BoardDocument document) => _page.Strokes.Add(_stroke);

    public void Revert(BoardDocument document) => _page.Strokes.Remove(_stroke);
}

public sealed class EraseStrokeCommand : IBoardCommand
{
    private readonly BoardPage _page;
    private readonly Stroke _stroke;
    private int _index = -1;

    public EraseStrokeCommand(BoardPage page, Stroke stroke)
    {
        _page = page;
        _stroke = stroke;
    }

    public string Label => "擦除笔迹";

    public void Apply(BoardDocument document)
    {
        _index = _page.Strokes.IndexOf(_stroke);
        if (_index >= 0)
            _page.Strokes.RemoveAt(_index);
    }

    public void Revert(BoardDocument document)
    {
        if (_index < 0)
            return;

        _page.Strokes.Insert(Math.Min(_index, _page.Strokes.Count), _stroke);
    }
}

public sealed class ClearPageCommand : IBoardCommand
{
    private readonly BoardPage _page;
    private readonly List<Stroke> _cleared = new();

    public ClearPageCommand(BoardPage page) => _page = page;

    public string Label => "清空页面";

    public void Apply(BoardDocument document)
    {
        _cleared.Clear();
        _cleared.AddRange(_page.Strokes);
        _page.Strokes.Clear();
    }

    public void Revert(BoardDocument document)
    {
        _page.Strokes.Clear();
        _page.Strokes.AddRange(_cleared);
        _cleared.Clear();
    }
}

public sealed class StyleStrokeCommand : IBoardCommand
{
    private readonly Stroke _stroke;
    private readonly StrokeStyle _newStyle;
    private StrokeStyle _oldStyle;

    public StyleStrokeCommand(Stroke stroke, StrokeStyle newStyle)
    {
        _stroke = stroke;
        _newStyle = newStyle;
        _oldStyle = stroke.Style;
    }

    public string Label => "修改笔迹";

    public void Apply(BoardDocument document)
    {
        _oldStyle = _stroke.Style;
        _stroke.SetStyle(_newStyle);
    }

    public void Revert(BoardDocument document) => _stroke.SetStyle(_oldStyle);
}

public sealed class AddPageCommand : IBoardCommand
{
    private readonly BoardPage _page;
    private readonly int _index;
    private int _previousActiveIndex;

    public AddPageCommand(BoardPage page, int index)
    {
        _page = page;
        _index = index;
    }

    public string Label => "新建页面";

    public void Apply(BoardDocument document)
    {
        _previousActiveIndex = document.ActiveIndex;
        document.InsertPage(_index, _page);
    }

    public void Revert(BoardDocument document)
    {
        document.Pages.Remove(_page);
        _page.Owner = null;
        document.ActiveIndex = _previousActiveIndex;
    }
}

public sealed class RemovePageCommand : IBoardCommand
{
    private readonly BoardPage _page;
    private readonly int _index;
    private int _previousActiveIndex;

    public RemovePageCommand(int index, BoardPage page)
    {
        _index = index;
        _page = page;
    }

    public string Label => "删除页面";

    public void Apply(BoardDocument document)
    {
        if (document.Pages.Count <= 1)
            return;

        _previousActiveIndex = document.ActiveIndex;
        document.Pages.RemoveAt(_index);
        _page.Owner = null;
        document.ActiveIndex = Math.Clamp(_previousActiveIndex, 0, document.Pages.Count - 1);
    }

    public void Revert(BoardDocument document)
    {
        document.Pages.Insert(Math.Min(_index, document.Pages.Count), _page);
        _page.Owner = document;
        document.ActiveIndex = _previousActiveIndex;
    }
}
