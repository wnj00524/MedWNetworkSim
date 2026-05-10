using System;
using MedWNetworkSim.App.Services;
using Xunit;

namespace MedWNetworkSim.Tests;

public sealed class UndoRedoServiceTests
{
    private sealed class MockCommand : IUndoableCommand
    {
        public string Name => "TestCommand";
        public int ExecuteCount { get; private set; }
        public int UndoCount { get; private set; }

        public void Execute()
        {
            ExecuteCount++;
        }

        public void Undo()
        {
            UndoCount++;
        }
    }

    [Fact]
    public void Execute_AddsToUndoStack_ClearsRedoStack_AndExecutesCommand()
    {
        var service = new UndoRedoService();
        var command1 = new MockCommand();
        var command2 = new MockCommand();

        service.Execute(command1);
        service.Undo();
        // At this point, redo has command1

        service.Execute(command2);

        Assert.True(service.CanUndo);
        Assert.False(service.CanRedo);
        Assert.Equal(1, command2.ExecuteCount);
        Assert.Equal(0, command2.UndoCount);
    }

    [Fact]
    public void Undo_WhenCanUndo_UndoesCommand_AndMovesToRedoStack()
    {
        var service = new UndoRedoService();
        var command = new MockCommand();

        service.Execute(command);
        service.Undo();

        Assert.False(service.CanUndo);
        Assert.True(service.CanRedo);
        Assert.Equal(1, command.ExecuteCount);
        Assert.Equal(1, command.UndoCount);
    }

    [Fact]
    public void Undo_WhenCannotUndo_DoesNothing()
    {
        var service = new UndoRedoService();

        service.Undo(); // Should not throw

        Assert.False(service.CanUndo);
        Assert.False(service.CanRedo);
    }

    [Fact]
    public void Redo_WhenCanRedo_ExecutesCommand_AndMovesToUndoStack()
    {
        var service = new UndoRedoService();
        var command = new MockCommand();

        service.Execute(command);
        service.Undo();
        service.Redo();

        Assert.True(service.CanUndo);
        Assert.False(service.CanRedo);
        Assert.Equal(2, command.ExecuteCount); // Executed initially, then executed again on Redo
        Assert.Equal(1, command.UndoCount);
    }

    [Fact]
    public void Redo_WhenCannotRedo_DoesNothing()
    {
        var service = new UndoRedoService();

        service.Redo(); // Should not throw

        Assert.False(service.CanUndo);
        Assert.False(service.CanRedo);
    }

    [Fact]
    public void Clear_EmptiesBothStacks()
    {
        var service = new UndoRedoService();
        var command1 = new MockCommand();
        var command2 = new MockCommand();

        service.Execute(command1);
        service.Execute(command2);

        // At this point Undo has 2, Redo has 0
        service.Undo();

        // At this point Undo has 1, Redo has 1
        Assert.True(service.CanUndo);
        Assert.True(service.CanRedo);

        service.Clear();

        Assert.False(service.CanUndo);
        Assert.False(service.CanRedo);
    }
}
