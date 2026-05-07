namespace MedWNetworkSim.App.Services;
/// <summary>
/// Defines the contract and required members for iundoable command implementations.
/// </summary>

public interface IUndoableCommand
{
    string Name { get; }

    void Execute();

    void Undo();
}
/// <summary>
/// Provides business logic and operations related to iundo redo.
/// </summary>

public interface IUndoRedoService
{
    bool CanUndo { get; }

    bool CanRedo { get; }

    void Execute(IUndoableCommand command);

    void Undo();

    void Redo();

    void Clear();
}
/// <summary>
/// Provides business logic and operations related to undo redo.
/// </summary>

public sealed class UndoRedoService : IUndoRedoService
{
    private readonly Stack<IUndoableCommand> undo = new();
    private readonly Stack<IUndoableCommand> redo = new();
    /// <summary>
    /// Gets a value indicating whether can undo is enabled or active.
    /// </summary>

    public bool CanUndo => undo.Count > 0;
    /// <summary>
    /// Gets a value indicating whether can redo is enabled or active.
    /// </summary>

    public bool CanRedo => redo.Count > 0;
    /// <summary>
    /// Executes the primary operation of this component.
    /// </summary>

    public void Execute(IUndoableCommand command)
    {
        command.Execute();
        undo.Push(command);
        redo.Clear();
    }
    /// <summary>
    /// Executes the undo operation.
    /// </summary>

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
    /// <summary>
    /// Executes the redo operation.
    /// </summary>

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
    /// <summary>
    /// Executes the clear operation.
    /// </summary>

    public void Clear()
    {
        undo.Clear();
        redo.Clear();
    }
}
