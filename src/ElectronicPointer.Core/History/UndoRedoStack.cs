using Mutantcat.ElectronicPointer.Core.Board;

namespace Mutantcat.ElectronicPointer.Core.History;

/// <summary>
/// Undo/redo stack. The document is passed in per call rather than owned, so the same
/// stack can drive a board that is swapped out (for example when opening a file).
/// </summary>
public sealed class UndoRedoStack
{
    private readonly List<IBoardCommand> _undo = new();
    private readonly List<IBoardCommand> _redo = new();

    /// <summary>Maximum number of undo entries kept before the oldest one is dropped.</summary>
    public int Limit { get; set; } = 300;

    public int UndoCount => _undo.Count;

    public int RedoCount => _redo.Count;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string? NextUndoLabel => _undo.Count > 0 ? _undo[^1].Label : null;

    public string? NextRedoLabel => _redo.Count > 0 ? _redo[^1].Label : null;

    public event EventHandler? StateChanged;

    public void Execute(BoardDocument document, IBoardCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        command.Apply(document);
        _undo.Add(command);
        _redo.Clear();
        Trim();

        if (command is not CompositeCommand composite || composite.Count > 0)
            document.Touch();

        OnStateChanged();
    }

    /// <summary>
    /// Records a command that has already been applied to the document, so a gesture that
    /// mutated the board as it went along still lands in the history as one step. Applying
    /// it again here would either do nothing or undo itself, so the caller is trusted to
    /// have done the work.
    /// </summary>
    public void Push(BoardDocument document, IBoardCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        _undo.Add(command);
        _redo.Clear();
        Trim();

        if (command is not CompositeCommand composite || composite.Count > 0)
            document.Touch();

        OnStateChanged();
    }

    public bool Undo(BoardDocument document)
    {
        if (_undo.Count == 0)
            return false;

        var command = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        command.Revert(document);
        _redo.Add(command);
        document.Touch();
        OnStateChanged();
        return true;
    }

    public bool Redo(BoardDocument document)
    {
        if (_redo.Count == 0)
            return false;

        var command = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        command.Apply(document);
        _undo.Add(command);
        Trim();
        document.Touch();
        OnStateChanged();
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        OnStateChanged();
    }

    private void Trim()
    {
        if (Limit <= 0)
            return;

        while (_undo.Count > Limit)
            _undo.RemoveAt(0);
    }

    private void OnStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
