using Mutantcat.ElectronicPointer.Core.Board;

namespace Mutantcat.ElectronicPointer.Core.History;

/// <summary>
/// A reversible edit to a <see cref="BoardDocument"/>. Commands capture the objects they
/// touch so undoing does not need a snapshot of the whole document.
/// </summary>
public interface IBoardCommand
{
    string Label
    {
        get;
    }

    void Apply(BoardDocument document);

    void Revert(BoardDocument document);
}

/// <summary>Applies several commands as one undoable step, e.g. one eraser sweep.</summary>
public sealed class CompositeCommand : IBoardCommand
{
    private readonly List<IBoardCommand> _commands = new();

    public CompositeCommand(string label) => Label = label;

    public CompositeCommand(string label, IEnumerable<IBoardCommand> commands) : this(label)
    {
        _commands.AddRange(commands);
    }

    public string Label
    {
        get;
    }

    public int Count => _commands.Count;

    public void Add(IBoardCommand command) => _commands.Add(command);

    public void Apply(BoardDocument document)
    {
        foreach (var command in _commands)
            command.Apply(document);
    }

    public void Revert(BoardDocument document)
    {
        for (var i = _commands.Count - 1; i >= 0; i--)
            _commands[i].Revert(document);
    }
}
