namespace ACEditor.Core.Editing;

public interface IEditCommand
{
    string Description { get; }
    void Execute();
    void Undo();
}

public sealed class PropertyEditCommand<T>(string description, Action<T> setter, T before, T after)
    : IEditCommand
{
    public string Description { get; } = description;
    public void Execute() => setter(after);
    public void Undo() => setter(before);
}

public sealed class UndoRedoStack
{
    private readonly Stack<IEditCommand> _undo = [];
    private readonly Stack<IEditCommand> _redo = [];
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoDescription => _undo.TryPeek(out var command) ? command.Description : null;
    public string? RedoDescription => _redo.TryPeek(out var command) ? command.Description : null;

    public void Execute(IEditCommand command)
    {
        command.Execute();
        _undo.Push(command);
        _redo.Clear();
    }

    public void Undo()
    {
        if (!_undo.TryPop(out var command)) return;
        command.Undo();
        _redo.Push(command);
    }

    public void Redo()
    {
        if (!_redo.TryPop(out var command)) return;
        command.Execute();
        _undo.Push(command);
    }
}
