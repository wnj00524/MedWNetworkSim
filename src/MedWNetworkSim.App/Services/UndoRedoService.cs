namespace MedWNetworkSim.App.Services;

public interface IUndoableCommand
{
    string Name { get; }

    void Execute();

    void Undo();
}

public interface IUndoRedoService
{
    bool CanUndo { get; }

    bool CanRedo { get; }

    void Execute(IUndoableCommand command);

    void Undo();

    void Redo();

    void Clear();
}

public sealed class UndoRedoService : IUndoRedoService
{
    private readonly Stack<IUndoableCommand> undo = new();
    private readonly Stack<IUndoableCommand> redo = new();

    public bool CanUndo => undo.Count > 0;

    public bool CanRedo => redo.Count > 0;

    public void Execute(IUndoableCommand command)
    {
        command.Execute();
        undo.Push(command);
        redo.Clear();
    }

    public void Undo()
    {
        if (!CanUndo)
        {
            return;
        }

        var command = undo.Pop();
        command.Undo();
        redo.Push(command);
    }

    public void Redo()
    {
        if (!CanRedo)
        {
            return;
        }

        var command = redo.Pop();
        command.Execute();
        undo.Push(command);
    }

    public void Clear()
    {
        undo.Clear();
        redo.Clear();
    }
}
