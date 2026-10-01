using Mutantcat.ElectronicPointer.Core.Board;
using Mutantcat.ElectronicPointer.Core.Geometry;
using Mutantcat.ElectronicPointer.Core.History;
using Mutantcat.ElectronicPointer.Core.Ink;
using Mutantcat.ElectronicPointer.Core.Selection;
using Mutantcat.ElectronicPointer.Core.Serialization;
using Mutantcat.ElectronicPointer.Platform.Recognition;
using Mutantcat.ElectronicPointer.Rendering;

namespace Mutantcat.ElectronicPointer.App.Session;

/// <summary>
/// Everything the user is doing to one board: the document, its undo history, the tool in
/// hand and the gestures currently in flight. The class knows nothing about windows, so the
/// same session drives every overlay window the shell opened, and a hotkey arriving on a
/// background thread can hand work over without any window in the picture.
///
/// Pointer work is modelled as begin/move/end rather than as whole strokes, because that is
/// what a pen, a finger and a mouse all arrive as, and it lets a stroke be drawn while it is
/// still being collected instead of popping into existence on lift.
/// </summary>
public sealed class BoardSession
{
    private readonly Lock _sync = new();
    private readonly UndoRedoStack _history = new();

    private BoardDocument _document = BoardDocument.CreateDefault();
    private ToolKind _tool = ToolKind.Pen;
    private uint _color = InkPalette.Colors[0];
    private double _penSize = StrokeStyle.DefaultPen().Size;
    private double _highlighterSize = StrokeStyle.DefaultHighlighter().Size;
    private double _eraserRadius = 24;
    private bool _passThrough;
    private bool _toolbarVisible = true;
    private int _frozenScreenIndex;

    private Stroke? _active;
    private readonly List<Vec2> _loop = new();
    private readonly HashSet<Stroke> _selected = new(ReferenceEqualityComparer.Instance);
    private bool _dragging;
    private Vec2 _dragFrom;
    private Vec2 _dragTo;
    private Vec2 _eraserCursor;
    private bool _eraserCursorVisible;
    private long _cleanRevision;

    /// <summary>
    /// Raised after anything a window or a toolbar would want to redraw. The thread it is
    /// raised on is wherever the change happened: pointer work is the UI thread, a
    /// recognition rewrite is wherever its engine finished. Subscribers that touch a window
    /// have to marshal to the dispatcher themselves, which is what keeps this class usable
    /// from a hotkey thread without dragging a dispatcher into it.
    /// </summary>
    public event Action? Changed;

    public BoardSession()
    {
        _cleanRevision = _document.Revision;
        _history.StateChanged += (_, _) => RaiseChanged();
    }

    // ---------------------------------------------------------------- tools

    public ToolKind Tool
    {
        get => _tool;
        set
        {
            if (_tool == value)
                return;

            _tool = value;
            if (value != ToolKind.Select)
                ClearSelection();

            CancelGesture();
            RaiseChanged();
        }
    }

    public uint Color
    {
        get => _color;
        set
        {
            if (_color == value)
                return;

            _color = value;
            RaiseChanged();
        }
    }

    public double PenSize
    {
        get => _penSize;
        set => SetSize(ref _penSize, value);
    }

    public double HighlighterSize
    {
        get => _highlighterSize;
        set => SetSize(ref _highlighterSize, value);
    }

    public double EraserRadius
    {
        get => _eraserRadius;
        set => SetSize(ref _eraserRadius, value);
    }

    /// <summary>The appearance a stroke drawn right now would get.</summary>
    public StrokeStyle CurrentStyle => StrokeStyle.Create(_tool == ToolKind.Highlighter ? StrokeKind.Highlighter : StrokeKind.Pen, _color, _tool == ToolKind.Highlighter ? _highlighterSize : _penSize);

    public bool PassThrough
    {
        get => _passThrough;
        set
        {
            if (_passThrough == value)
                return;

            _passThrough = value;
            RaiseChanged();
        }
    }

    public bool ToolbarVisible
    {
        get => _toolbarVisible;
        set
        {
            if (_toolbarVisible == value)
                return;

            _toolbarVisible = value;
            RaiseChanged();
        }
    }

    /// <summary>
    /// Which display "freeze screen" grabs, as an index into the shell's list. The
    /// settings sheet owns it through a picker that names every display, so a grab lands
    /// on the desk the user actually annotates instead of whichever one the host
    /// enumerates first. It is persisted, so the choice survives the restart.
    /// </summary>
    public int FrozenScreenIndex
    {
        get => _frozenScreenIndex;
        set
        {
            if (_frozenScreenIndex == value)
                return;

            _frozenScreenIndex = value;
            RaiseChanged();
        }
    }

    // ---------------------------------------------------------------- document

    public BoardDocument Document { get { lock (_sync) return _document; } }

    public UndoRedoStack History => _history;

    public BoardPage ActivePage => Document.ActivePage;

    public int PageIndex => Document.ActiveIndex + 1;

    public int PageCount => Document.Pages.Count;

    public string PageName => Document.Pages.Count == 0 ? string.Empty : Document.ActivePage.Name;

    /// <summary>
    /// Whether the active page carries a frozen-screen picture under its ink. The palette
    /// reads this to tell its grab button which way the next press goes, and it is the
    /// active page because the freeze command is: two pages, two answers.
    /// </summary>
    public bool IsFrozen => ActivePage.BackgroundImage is not null;

    public bool CanUndo => _history.CanUndo;

    public bool CanRedo => _history.CanRedo;

    public string? NextUndoLabel => _history.NextUndoLabel;

    public string? NextRedoLabel => _history.NextRedoLabel;

    public bool IsDirty => Document.Revision != _cleanRevision;

    /// <summary>Marks the current state as the saved state, so the title can show a dot.</summary>
    public void MarkSaved() => _cleanRevision = Document.Revision;

    // ---------------------------------------------------------------- gestures

    public IReadOnlyList<Vec2> LassoLoop { get { lock (_sync) return _loop.ToArray(); } }

    public bool IsLassoing { get { lock (_sync) return _loop.Count > 2; } }

    public int SelectedStrokeCount { get { lock (_sync) return _selected.Count; } }

    public bool IsGestureActive { get { lock (_sync) return _active is not null || _loop.Count > 0 || _dragging; } }

    public Vec2 EraserCursor => _eraserCursor;

    public bool EraserCursorVisible => _eraserCursorVisible;

    /// <summary>
    /// Hides the eraser ring once the pointer has left a surface. The ring follows the
    /// pointer while it is on one, which <see cref="PointerMoved"/> tracks; this is the
    /// other half of that, so a ring is never left floating over the desk.
    /// </summary>
    public void Leave()
    {
        if (!_eraserCursorVisible)
            return;

        _eraserCursorVisible = false;
        RaiseChanged();
    }

    public void PointerPressed(Vec2 boardPoint, double? pressure)
    {
        CancelGesture();

        switch (_tool)
        {
            case ToolKind.Pen:
            case ToolKind.Highlighter:
                BeginStroke(boardPoint, pressure);
                break;
            case ToolKind.Eraser:
                _eraserCursor = boardPoint;
                _eraserCursorVisible = true;
                EraseAt(boardPoint);
                break;
            case ToolKind.Select:
                GrabSelection(boardPoint);
                break;
            default:
                break;
        }

        RaiseChanged();
    }

    public void PointerMoved(Vec2 boardPoint, double? pressure)
    {
        _eraserCursor = boardPoint;
        _eraserCursorVisible = _tool == ToolKind.Eraser && !_passThrough;

        if (_active is not null)
        {
            _active.Append(boardPoint, pressure);
            RaiseChanged();
            return;
        }

        if (_loop.Count > 0)
        {
            _loop.Add(boardPoint);
            RaiseChanged();
            return;
        }

        if (_dragging)
        {
            _dragTo = boardPoint;
            RaiseChanged();
            return;
        }

        if (_tool == ToolKind.Eraser)
            EraseAt(boardPoint);

        RaiseChanged();
    }

    public void PointerReleased(Vec2 boardPoint)
    {
        FlushEraseBatch();

        var moved = _dragging && _dragFrom.DistanceTo(_dragTo) > 1.0;
        var delta = _dragging ? _dragTo - _dragFrom : Vec2.Zero;

        if (_active is not null)
        {
            var page = ActivePage;
            var stroke = _active;
            stroke.Complete();
            page.ActiveStroke = null;
            _active = null;
            _history.Execute(Document, new AddStrokeCommand(page, stroke));
            RaiseChanged();
            return;
        }

        if (_loop.Count > 0)
        {
            var loop = _loop.ToArray();
            _loop.Clear();

            if (loop.Length > 2)
                Select(loop);

            RaiseChanged();
            return;
        }

        if (moved && delta != Vec2.Zero)
        {
            var strokes = SelectedStrokes();
            if (strokes.Count > 0)
                _history.Execute(Document, new MoveStrokesCommand(strokes, delta));
        }

        _dragging = false;
        RaiseChanged();
    }

    /// <summary>Abandons the gesture in flight, for instance when the tool is switched.</summary>
    public void CancelGesture()
    {
        // An eraser sweep is only pushed onto the history when the pen is lifted. Abandoning
        // the gesture instead - switching tool mid sweep, say - would otherwise drop the
        // commands on the floor, and the ink they removed is gone with no undo to bring it
        // back. Flushing first keeps a half finished sweep answerable to undo.
        FlushEraseBatch();

        if (_active is not null)
        {
            ActivePage.ActiveStroke = null;
            _active = null;
        }

        _loop.Clear();
        _dragging = false;
    }

    private void BeginStroke(Vec2 boardPoint, double? pressure)
    {
        var page = ActivePage;
        var stroke = new Stroke(CurrentStyle);
        stroke.Append(boardPoint, pressure);
        page.ActiveStroke = stroke;
        _active = stroke;
    }

    private void GrabSelection(Vec2 boardPoint)
    {
        var stroke = SelectedStrokeAt(boardPoint);
        if (stroke is null)
        {
            ClearSelection();
            _loop.Add(boardPoint);
            return;
        }

        _dragging = true;
        _dragFrom = boardPoint;
        _dragTo = boardPoint;
    }

    private void Select(IReadOnlyList<Vec2> loop)
    {
        lock (_sync)
        {
            _selected.Clear();
            foreach (var stroke in ActivePage.Strokes)
            {
                if (Lasso.Selects(loop, stroke))
                    _selected.Add(stroke);
            }
        }
    }

    private Stroke? SelectedStrokeAt(Vec2 boardPoint)
    {
        lock (_sync)
        {
            // Only strokes on the page in front of the user can be grabbed. A selection can
            // outlive the page it was made on, and a holdover stroke would otherwise let
            // the pointer drag ink the user cannot see.
            foreach (var stroke in ActivePage.Strokes)
            {
                if (_selected.Contains(stroke) && StrokeHitTester.Intersects(stroke, boardPoint, 6))
                    return stroke;
            }
        }

        return null;
    }

    private IReadOnlyList<Stroke> SelectedStrokes()
    {
        lock (_sync)
        {
            return ActivePage.Strokes.Where(_selected.Contains).ToArray();
        }
    }

    private void ClearSelection()
    {
        lock (_sync) _selected.Clear();
    }

    // ---------------------------------------------------------------- erasing

    /// <summary>
    /// One eraser sweep is one undoable step, so the commands a pass collects are applied
    /// together on the next release. Erasing the same stroke twice in a sweep is impossible,
    /// because it is already gone.
    /// </summary>
    private readonly List<EraseStrokeCommand> _eraseBatch = new();

    private void EraseAt(Vec2 boardPoint)
    {
        var page = ActivePage;
        Stroke? hit = null;
        for (var i = page.Strokes.Count - 1; i >= 0; i--)
        {
            if (StrokeHitTester.Intersects(page.Strokes[i], boardPoint, _eraserRadius))
            {
                hit = page.Strokes[i];
                break;
            }
        }

        if (hit is null)
            return;

        var command = new EraseStrokeCommand(page, hit);
        command.Apply(Document);
        _eraseBatch.Add(command);
    }

    private void FlushEraseBatch()
    {
        if (_eraseBatch.Count == 0)
            return;

        var composite = new CompositeCommand("擦除笔迹", _eraseBatch.ToArray());
        _eraseBatch.Clear();
        _history.Push(Document, composite);
    }

    // ---------------------------------------------------------------- commands

    public void Undo() => RunOnDocument(document => _ = _history.Undo(document));

    public void Redo() => RunOnDocument(document => _ = _history.Redo(document));

    public void ClearPage()
    {
        // Everything on the page is about to disappear, so the selection disappears with it.
        ClearSelection();

        var page = ActivePage;
        if (!page.HasInk())
            return;

        RunOnDocument(document => _history.Execute(document, new ClearPageCommand(page)));
    }

    public void AddPage()
    {
        var page = new BoardPage();
        RunOnDocument(document => _history.Execute(document, new AddPageCommand(page, document.ActiveIndex + 1)));
    }

    public void RemovePage()
    {
        ClearSelection();

        RunOnDocument(document =>
        {
            if (document.Pages.Count <= 1)
                return;

            _history.Execute(document, new RemovePageCommand(document.ActiveIndex, document.ActivePage));
        });
    }

    public void NextPage() => GoToPage(Document.ActiveIndex + 1);

    public void PreviousPage() => GoToPage(Document.ActiveIndex - 1);

    /// <summary>
    /// Shows another page. The selection does not travel with the user: ink on a page that
    /// is no longer on screen must not still be selectable, draggable or erasable.
    /// </summary>
    public void GoToPage(int index)
    {
        ClearSelection();
        RunOnDocument(document => document.ActiveIndex = index);
    }

    /// <summary>Applies a frozen screen behind the ink, or removes it when passed null.</summary>
    public void Freeze(BoardBackground? background)
        => RunOnDocument(document => _history.Execute(document, new SetBackgroundCommand(document.ActivePage, background)));

    public string Serialize()
    {
        MarkSaved();
        return BoardDocumentSerializer.Serialize(Document, AppIdentity.Version, AppIdentity.ApplicationId);
    }

    public bool TryLoad(string json, out string? failureReason)
    {
        var loaded = BoardDocumentSerializer.Deserialize(json);
        if (loaded is null)
        {
            failureReason = "文件不是本软件能读的批注文档。";
            return false;
        }

        lock (_sync)
        {
            CancelGesture();
            ClearSelection();
            _document = loaded;
        }

        _history.Clear();
        MarkSaved();
        RaiseChanged();
        failureReason = null;
        return true;
    }

    /// <summary>
    /// Tidies the selected strokes into the shapes they were meant to be.
    ///
    /// The recognition itself runs off the UI thread, but the rewrite it produces is a
    /// normal edit: it goes on the undo history as one step and leaves the original ink a
    /// single undo away. A reading the user disagrees with therefore costs one press, and
    /// strokes the engine could not read are left exactly as they were drawn.
    /// </summary>
    public async Task<RecognitionReport> RecognizeAsync(
        IInkRecognizer recognizer,
        CancellationToken cancellationToken)
    {
        var strokes = SelectedStrokes();
        var paths = strokes
            .Select(stroke => (IReadOnlyList<Vec2>)stroke.Samples.Select(sample => sample.Point).ToArray())
            .ToArray();

        var report = await recognizer.RecognizeAsync(paths, cancellationToken).ConfigureAwait(false);
        ApplyRecognition(strokes, report);
        return report;
    }

    private void ApplyRecognition(IReadOnlyList<Stroke> strokes, RecognitionReport report)
    {
        var commands = new List<IBoardCommand>();

        foreach (var ink in report.Ink)
        {
            if (ink.Index < 0 || ink.Index >= strokes.Count)
                continue;

            commands.Add(new ReplaceStrokeSamplesCommand(strokes[ink.Index], ink.Points));
        }

        if (commands.Count == 0)
            return;

        RunOnDocument(document => _history.Execute(document, new CompositeCommand("整理形状", commands)));
    }

    private void RunOnDocument(Action<BoardDocument> action)
    {
        var document = Document;
        lock (_sync)
            action(document);

        RaiseChanged();
    }

    private void SetSize(ref double field, double value)
    {
        var clamped = Math.Clamp(value, 1, 96);
        if (Math.Abs(clamped - field) < 0.001)
            return;

        field = clamped;
        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke();
}
