namespace Mutantcat.ElectronicPointer.Core.Board;

/// <summary>
/// The whole document: an ordered set of pages plus the active page. A monotonically
/// increasing <see cref="Revision"/> is the only notification mechanism, which keeps the
/// document platform free and trivial to render from.
/// </summary>
public sealed class BoardDocument
{
    private int _activeIndex;

    public List<BoardPage> Pages { get; } = new();

    public long Revision
    {
        get;
        private set;
    }

    public int ActiveIndex
    {
        get => _activeIndex;
        set
        {
            if (Pages.Count == 0)
            {
                _activeIndex = 0;
                return;
            }

            var clamped = Math.Clamp(value, 0, Pages.Count - 1);
            if (clamped == _activeIndex)
                return;

            _activeIndex = clamped;
            Touch();
        }
    }

    public BoardPage ActivePage
    {
        get
        {
            if (Pages.Count == 0)
                throw new InvalidOperationException("The document has no pages yet.");

            return Pages[_activeIndex];
        }
    }

    public static BoardDocument CreateDefault()
    {
        var document = new BoardDocument();
        var page = document.AddPage("页面 1");
        _ = page;
        return document;
    }

    public void Touch() => Revision++;

    public BoardPage AddPage(string? name = null)
    {
        var index = Pages.Count;
        return InsertPage(index, name);
    }

    public BoardPage InsertPage(int index, string? name = null)
    {
        var page = new BoardPage { Name = name ?? $"页面 {index + 1}" };
        InsertPage(index, page);
        return page;
    }

    public void InsertPage(int index, BoardPage page)
    {
        var target = Math.Clamp(index, 0, Pages.Count);
        Pages.Insert(target, page);
        page.Owner = this;
        if (_activeIndex >= target)
            _activeIndex++;

        _activeIndex = Math.Clamp(_activeIndex, 0, Math.Max(0, Pages.Count - 1));
        Touch();
    }

    public bool RemovePage(int index)
    {
        if (Pages.Count <= 1)
            return false;

        if (index < 0 || index >= Pages.Count)
            return false;

        var page = Pages[index];
        Pages.RemoveAt(index);
        page.Owner = null;
        _activeIndex = Math.Clamp(_activeIndex, 0, Pages.Count - 1);
        Touch();
        return true;
    }

    public bool CanClearActivePage() => ActivePage.HasInk();
}
