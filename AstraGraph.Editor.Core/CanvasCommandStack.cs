using System;
using System.Collections.Generic;
using System.Linq;
using AstraGraph.Core;

namespace AstraGraph.Editor.Core;

public interface IEditorCommand
{
    string Description { get; }
    void Execute();
    void Undo();
}

public sealed class CanvasCommandStack
{
    private readonly Stack<IEditorCommand> _undoStack = new();
    private readonly Stack<IEditorCommand> _redoStack = new();
    private readonly int _maxHistory;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public int UndoCount => _undoStack.Count;
    public int RedoCount => _redoStack.Count;

    public CanvasCommandStack(int maxHistory = 100)
    {
        _maxHistory = Math.Max(10, maxHistory);
    }

    public void Execute(IEditorCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        command.Execute();
        _undoStack.Push(command);
        _redoStack.Clear();

        if (_undoStack.Count > _maxHistory)
        {
            var items = _undoStack.Reverse().Skip(1).ToList();
            _undoStack.Clear();
            foreach (var item in items)
            {
                _undoStack.Push(item);
            }
        }
    }

    public bool Undo()
    {
        if (!CanUndo) return false;
        var command = _undoStack.Pop();
        command.Undo();
        _redoStack.Push(command);
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo) return false;
        var command = _redoStack.Pop();
        command.Execute();
        _undoStack.Push(command);
        return true;
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }
}

public sealed class AddNodeCommand : IEditorCommand
{
    private readonly CanvasModel _model;
    private readonly VisualNode _node;

    public string Description => $"Add node '{_node.Name}'";

    public AddNodeCommand(CanvasModel model, VisualNode node)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _node = node ?? throw new ArgumentNullException(nameof(node));
    }

    public void Execute() => _model.AddNode(_node);
    public void Undo() => _model.RemoveNode(_node.Id);
}

public sealed class DeleteNodeCommand : IEditorCommand
{
    private readonly CanvasModel _model;
    private readonly VisualNode _node;
    private readonly List<VisualConnection> _savedConnections = [];

    public string Description => $"Delete node '{_node.Name}'";

    public DeleteNodeCommand(CanvasModel model, NodeId nodeId)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _node = _model.Nodes[nodeId];
    }

    public void Execute()
    {
        _savedConnections.Clear();
        var pinIds = _node.Pins.Select(p => p.Id).ToHashSet();
        foreach (var conn in _model.Connections)
        {
            if (pinIds.Contains(conn.SourcePinId) || pinIds.Contains(conn.TargetPinId))
            {
                _savedConnections.Add(conn);
            }
        }
        _model.RemoveNode(_node.Id);
    }

    public void Undo()
    {
        _model.AddNode(_node);
        foreach (var conn in _savedConnections)
        {
            _model.AddConnection(conn);
        }
    }
}

public sealed class MoveNodesCommand : IEditorCommand
{
    private readonly CanvasModel _model;
    private readonly IReadOnlyDictionary<NodeId, (CanvasPoint OldPos, CanvasPoint NewPos)> _moves;

    public string Description => $"Move {_moves.Count} nodes";

    public MoveNodesCommand(CanvasModel model, IReadOnlyDictionary<NodeId, (CanvasPoint OldPos, CanvasPoint NewPos)> moves)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _moves = moves ?? throw new ArgumentNullException(nameof(moves));
    }

    public void Execute()
    {
        foreach (var (nodeId, (_, newPos)) in _moves)
        {
            if (_model.Nodes.TryGetValue(nodeId, out var node))
            {
                node.Position = newPos;
            }
        }
    }

    public void Undo()
    {
        foreach (var (nodeId, (oldPos, _)) in _moves)
        {
            if (_model.Nodes.TryGetValue(nodeId, out var node))
            {
                node.Position = oldPos;
            }
        }
    }
}

public sealed class ConnectPinsCommand : IEditorCommand
{
    private readonly CanvasModel _model;
    private readonly VisualConnection _connection;

    public string Description => $"Connect pins {_connection.SourcePinId} -> {_connection.TargetPinId}";

    public ConnectPinsCommand(CanvasModel model, VisualConnection connection)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public void Execute() => _model.AddConnection(_connection);
    public void Undo() => _model.RemoveConnection(_connection);
}

public sealed class DisconnectPinsCommand : IEditorCommand
{
    private readonly CanvasModel _model;
    private readonly VisualConnection _connection;

    public string Description => $"Disconnect pins {_connection.SourcePinId} -> {_connection.TargetPinId}";

    public DisconnectPinsCommand(CanvasModel model, VisualConnection connection)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public void Execute() => _model.RemoveConnection(_connection);
    public void Undo() => _model.AddConnection(_connection);
}
