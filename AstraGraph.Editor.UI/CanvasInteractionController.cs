using System;
using System.Collections.Generic;
using System.Linq;
using AstraGraph.Core;
using AstraGraph.Editor.Core;
using AstraGraph.Editor.Core.Debugging;
using AstraGraph.Editor.Core.Profiling;
using AstraGraph.Editor.Core.Search;

namespace AstraGraph.Editor.UI;

public enum InteractionState
{
    Idle,
    Panning,
    DraggingNodes,
    DraggingWire,
    BoxSelecting
}

public sealed class CanvasInteractionController
{
    public CanvasModel Model { get; }
    public CanvasCommandStack CommandStack { get; }
    public NodePaletteIndexer PaletteIndexer { get; }
    public VisualDebuggerSession? DebuggerSession { get; set; }
    public VisualProfilerOverlay? ProfilerOverlay { get; set; }

    public InteractionState State { get; private set; } = InteractionState.Idle;

    public VisualPin? DraggingSourcePin { get; private set; }
    public CanvasPoint DraggingWireCurrentPos { get; private set; }
    public CanvasRect? CurrentSelectionBox { get; private set; }
    public List<ContextCompletionSuggestion>? ActiveCompletionSuggestions { get; private set; }
    public IReadOnlyList<VisualNode>? Clipboard => _clipboardNodes;

    private CanvasPoint _lastMouseScreen;
    private CanvasPoint _mouseDownWorld;
    private readonly Dictionary<NodeId, CanvasPoint> _initialNodePositions = [];
    private List<VisualNode>? _clipboardNodes;

    public event Action? OnRepaintRequested;

    public CanvasInteractionController(
        CanvasModel model,
        CanvasCommandStack commandStack,
        NodePaletteIndexer paletteIndexer)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        CommandStack = commandStack ?? throw new ArgumentNullException(nameof(commandStack));
        PaletteIndexer = paletteIndexer ?? throw new ArgumentNullException(nameof(paletteIndexer));
    }

    public void OnMouseDown(CanvasPoint screenPos, MouseButton button, ModifierKeys modifiers)
    {
        _lastMouseScreen = screenPos;
        var worldPos = Model.Viewport.ScreenToWorld(screenPos);
        _mouseDownWorld = worldPos;
        ActiveCompletionSuggestions = null;

        if (button == MouseButton.Middle)
        {
            State = InteractionState.Panning;
            return;
        }

        if (button == MouseButton.Left)
        {
            // 1. Check if clicked on a pin
            var clickedPin = FindPinAtWorldPos(worldPos);
            if (clickedPin != null)
            {
                State = InteractionState.DraggingWire;
                DraggingSourcePin = clickedPin;
                DraggingWireCurrentPos = worldPos;
                OnRepaintRequested?.Invoke();
                return;
            }

            // 2. Check if clicked on a node
            var clickedNode = FindNodeAtWorldPos(worldPos);
            if (clickedNode != null)
            {
                State = InteractionState.DraggingNodes;
                if (!clickedNode.IsSelected)
                {
                    Model.SelectNode(clickedNode.Id, addToSelection: modifiers.Ctrl);
                }

                _initialNodePositions.Clear();
                foreach (var node in Model.GetSelectedNodes())
                {
                    _initialNodePositions[node.Id] = node.Position;
                }

                OnRepaintRequested?.Invoke();
                return;
            }

            // 3. Clicked on canvas empty space -> box select
            if (!modifiers.Ctrl)
            {
                Model.DeselectAll();
            }

            State = InteractionState.BoxSelecting;
            CurrentSelectionBox = new CanvasRect(worldPos.X, worldPos.Y, 0f, 0f);
            OnRepaintRequested?.Invoke();
        }
    }

    public void OnMouseMove(CanvasPoint screenPos)
    {
        var worldPos = Model.Viewport.ScreenToWorld(screenPos);
        var screenDelta = screenPos - _lastMouseScreen;
        _lastMouseScreen = screenPos;

        switch (State)
        {
            case InteractionState.Panning:
                Model.Viewport.PanX += screenDelta.X;
                Model.Viewport.PanY += screenDelta.Y;
                OnRepaintRequested?.Invoke();
                break;

            case InteractionState.DraggingWire:
                DraggingWireCurrentPos = worldPos;
                OnRepaintRequested?.Invoke();
                break;

            case InteractionState.DraggingNodes:
                var worldDelta = worldPos - _mouseDownWorld;
                foreach (var (nodeId, startPos) in _initialNodePositions)
                {
                    if (Model.Nodes.TryGetValue(nodeId, out var node))
                    {
                        var newPos = startPos + worldDelta;
                        node.Position = CanvasViewport.SnapToGrid(newPos);
                    }
                }
                OnRepaintRequested?.Invoke();
                break;

            case InteractionState.BoxSelecting:
                var minX = MathF.Min(_mouseDownWorld.X, worldPos.X);
                var minY = MathF.Min(_mouseDownWorld.Y, worldPos.Y);
                var width = MathF.Abs(worldPos.X - _mouseDownWorld.X);
                var height = MathF.Abs(worldPos.Y - _mouseDownWorld.Y);
                var box = new CanvasRect(minX, minY, width, height);
                CurrentSelectionBox = box;
                Model.SelectArea(box);
                OnRepaintRequested?.Invoke();
                break;
        }
    }

    public void OnMouseUp(CanvasPoint screenPos, MouseButton button)
    {
        var worldPos = Model.Viewport.ScreenToWorld(screenPos);

        if (State == InteractionState.DraggingWire && DraggingSourcePin != null)
        {
            var targetPin = FindPinAtWorldPos(worldPos);
            if (targetPin != null)
            {
                // Connect two pins if valid
                var validation = PinConnectionValidator.Validate(DraggingSourcePin, targetPin, Model.Connections);
                if (validation.IsValid)
                {
                    var source = DraggingSourcePin.Direction == PinDirection.Output ? DraggingSourcePin : targetPin;
                    var target = DraggingSourcePin.Direction == PinDirection.Input ? DraggingSourcePin : targetPin;
                    CommandStack.Execute(new ConnectPinsCommand(Model, new VisualConnection(source.Id, target.Id)));
                }
            }
            else
            {
                // Released in open space -> generate contextual completion suggestions
                ActiveCompletionSuggestions = ContextCompletionEngine.SuggestForPin(DraggingSourcePin, PaletteIndexer);
            }

            DraggingSourcePin = null;
            State = InteractionState.Idle;
            OnRepaintRequested?.Invoke();
            return;
        }

        if (State == InteractionState.DraggingNodes)
        {
            // Record moves in command stack
            var moves = new Dictionary<NodeId, (CanvasPoint OldPos, CanvasPoint NewPos)>();
            foreach (var (nodeId, startPos) in _initialNodePositions)
            {
                if (Model.Nodes.TryGetValue(nodeId, out var node) && node.Position != startPos)
                {
                    moves[nodeId] = (startPos, node.Position);
                }
            }

            if (moves.Count > 0)
            {
                CommandStack.Execute(new MoveNodesCommand(Model, moves));
            }

            _initialNodePositions.Clear();
            State = InteractionState.Idle;
            OnRepaintRequested?.Invoke();
            return;
        }

        if (State == InteractionState.BoxSelecting)
        {
            CurrentSelectionBox = null;
            State = InteractionState.Idle;
            OnRepaintRequested?.Invoke();
            return;
        }

        State = InteractionState.Idle;
        OnRepaintRequested?.Invoke();
    }

    public void OnMouseWheel(CanvasPoint screenPos, float delta)
    {
        var worldBefore = Model.Viewport.ScreenToWorld(screenPos);
        var zoomFactor = delta > 0 ? 1.15f : 0.85f;
        Model.Viewport.Zoom *= zoomFactor;

        var screenAfter = Model.Viewport.WorldToScreen(worldBefore);
        Model.Viewport.PanX += screenPos.X - screenAfter.X;
        Model.Viewport.PanY += screenPos.Y - screenAfter.Y;

        OnRepaintRequested?.Invoke();
    }

    public void OnKeyDown(EditorKeyCode key, ModifierKeys modifiers)
    {
        if (key == EditorKeyCode.Delete)
        {
            var selected = Model.GetSelectedNodes();
            foreach (var node in selected)
            {
                CommandStack.Execute(new DeleteNodeCommand(Model, node.Id));
            }
            OnRepaintRequested?.Invoke();
            return;
        }

        if (modifiers.Ctrl && key == EditorKeyCode.Z)
        {
            CommandStack.Undo();
            OnRepaintRequested?.Invoke();
            return;
        }

        if (modifiers.Ctrl && key == EditorKeyCode.Y)
        {
            CommandStack.Redo();
            OnRepaintRequested?.Invoke();
            return;
        }

        if (modifiers.Ctrl && key == EditorKeyCode.A)
        {
            foreach (var node in Model.Nodes.Values)
            {
                node.IsSelected = true;
            }
            OnRepaintRequested?.Invoke();
            return;
        }

        if (modifiers.Ctrl && key == EditorKeyCode.C)
        {
            _clipboardNodes = Model.GetSelectedNodes().ToList();
            return;
        }

        if (modifiers.Ctrl && key == EditorKeyCode.V && _clipboardNodes != null && _clipboardNodes.Count > 0)
        {
            Model.DeselectAll();
            var offset = new CanvasPoint(40f, 40f);

            foreach (var orig in _clipboardNodes)
            {
                var clone = new VisualNode(NodeId.New(), orig.Name, orig.NodeType, orig.Position + offset)
                {
                    IsSelected = true
                };

                foreach (var p in orig.Pins)
                {
                    clone.Pins.Add(new VisualPin(PinId.New(), clone.Id, p.Name, p.Direction, p.Kind, p.DataType));
                }

                foreach (var (k, v) in orig.Properties)
                {
                    clone.Properties[k] = v;
                }

                CommandStack.Execute(new AddNodeCommand(Model, clone));
            }

            OnRepaintRequested?.Invoke();
            return;
        }

        if (key == EditorKeyCode.F9)
        {
            var selectedNodes = Model.GetSelectedNodes();
            var selected = selectedNodes.Count > 0 ? selectedNodes[0] : null;
            if (selected != null && DebuggerSession != null)
            {
                DebuggerSession.ToggleBreakpoint(selected.Id);
                OnRepaintRequested?.Invoke();
                return;
            }
        }

        if (key == EditorKeyCode.F5)
        {
            if (DebuggerSession != null && DebuggerSession.IsPaused)
            {
                DebuggerSession.Resume();
                OnRepaintRequested?.Invoke();
                return;
            }
        }

        if (key == EditorKeyCode.F10)
        {
            if (DebuggerSession != null && DebuggerSession.IsPaused)
            {
                DebuggerSession.StepOver();
                OnRepaintRequested?.Invoke();
                return;
            }
        }

        if (key == EditorKeyCode.F11)
        {
            if (DebuggerSession != null && DebuggerSession.IsPaused)
            {
                DebuggerSession.StepInto();
                OnRepaintRequested?.Invoke();
                return;
            }
        }
    }

    public void Render(ICanvasRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);

        // 1. Draw connections
        foreach (var conn in Model.Connections)
        {
            var sourceNode = Model.GetNodeForPin(conn.SourcePinId);
            var targetNode = Model.GetNodeForPin(conn.TargetPinId);

            if (sourceNode != null && targetNode != null)
            {
                var p1World = sourceNode.GetPinAnchorPosition(conn.SourcePinId);
                var p2World = targetNode.GetPinAnchorPosition(conn.TargetPinId);

                var p1Screen = Model.Viewport.WorldToScreen(p1World);
                var p2Screen = Model.Viewport.WorldToScreen(p2World);

                var curve = WireRouter.CalculateCubicBezier(p1Screen, p2Screen);

                var isHot = ProfilerOverlay?.IsActive == true && ProfilerOverlay.IsHotConnection(conn);
                var wireColor = isHot ? CanvasColor.Orange : CanvasColor.LightGray;
                var wireThickness = isHot ? 3.5f : 2f;
                renderer.DrawBezier(curve, wireColor, wireThickness);
            }
        }

        // 2. Draw dragging wire
        if (State == InteractionState.DraggingWire && DraggingSourcePin != null)
        {
            var sourceNode = Model.GetNodeForPin(DraggingSourcePin.Id);
            if (sourceNode != null)
            {
                var p1World = sourceNode.GetPinAnchorPosition(DraggingSourcePin.Id);
                var p1Screen = Model.Viewport.WorldToScreen(p1World);
                var p2Screen = Model.Viewport.WorldToScreen(DraggingWireCurrentPos);

                var curve = WireRouter.CalculateCubicBezier(p1Screen, p2Screen);
                renderer.DrawBezier(curve, CanvasColor.Yellow, 2.5f);
            }
        }

        // 3. Draw nodes
        foreach (var node in Model.Nodes.Values)
        {
            var screenPos = Model.Viewport.WorldToScreen(node.Position);
            var screenWidth = node.Width * Model.Viewport.Zoom;
            var screenHeight = node.Height * Model.Viewport.Zoom;
            var nodeRect = new CanvasRect(screenPos.X, screenPos.Y, screenWidth, screenHeight);

            // Node body
            renderer.FillRectangle(nodeRect, CanvasColor.DarkGray);

            // Node border (highlighted if suspended, selected, or hot)
            var borderColor = CanvasColor.Gray;
            var borderThickness = 1f;

            if (DebuggerSession?.SuspendedNodeId == node.Id)
            {
                borderColor = CanvasColor.Yellow;
                borderThickness = 3f;
            }
            else if (node.IsSelected)
            {
                borderColor = CanvasColor.Blue;
                borderThickness = 2.5f;
            }
            else if (ProfilerOverlay?.IsActive == true)
            {
                var heat = ProfilerOverlay.GetNodeHeat(node.Id);
                if (heat != null && heat.Level >= HeatLevel.Warm)
                {
                    borderColor = heat.Level switch
                    {
                        HeatLevel.Critical => CanvasColor.Red,
                        HeatLevel.Hot => CanvasColor.Orange,
                        _ => CanvasColor.Yellow
                    };
                    borderThickness = 2f;
                }
            }

            renderer.DrawRectangle(nodeRect, borderColor, borderThickness);

            // Breakpoint circle indicator on top-left
            var hasBp = DebuggerSession?.HasBreakpoint(node.Id) == true;
            if (hasBp)
            {
                var bpCenter = new CanvasPoint(screenPos.X + 8f * Model.Viewport.Zoom, screenPos.Y + 10f * Model.Viewport.Zoom);
                renderer.FillCircle(bpCenter, 5f * Model.Viewport.Zoom, CanvasColor.Red);
            }

            // Header title
            var textX = screenPos.X + (hasBp ? 18f * Model.Viewport.Zoom : 8f * Model.Viewport.Zoom);
            renderer.DrawText(node.Name, new CanvasPoint(textX, screenPos.Y + 6f), CanvasColor.White, 12f * Model.Viewport.Zoom);

            // Heat badge
            if (ProfilerOverlay?.IsActive == true)
            {
                var heat = ProfilerOverlay.GetNodeHeat(node.Id);
                if (heat != null && heat.HitCount > 0)
                {
                    var badgePos = new CanvasPoint(screenPos.X + screenWidth - (65f * Model.Viewport.Zoom), screenPos.Y + 6f);
                    renderer.DrawText(heat.FormattedText, badgePos, CanvasColor.Yellow, 10f * Model.Viewport.Zoom);
                }
            }

            // Pins
            foreach (var pin in node.Pins)
            {
                var pinWorldPos = node.GetPinAnchorPosition(pin.Id);
                var pinScreenPos = Model.Viewport.WorldToScreen(pinWorldPos);
                var pinColor = pin.Kind == PinKind.Execution ? CanvasColor.White : CanvasColor.Green;
                renderer.FillCircle(pinScreenPos, 5f * Model.Viewport.Zoom, pinColor);
            }
        }

        // 4. Draw selection box
        if (CurrentSelectionBox.HasValue)
        {
            var box = CurrentSelectionBox.Value;
            var screenP1 = Model.Viewport.WorldToScreen(new CanvasPoint(box.Left, box.Top));
            var screenP2 = Model.Viewport.WorldToScreen(new CanvasPoint(box.Right, box.Bottom));
            var screenRect = new CanvasRect(screenP1.X, screenP1.Y, screenP2.X - screenP1.X, screenP2.Y - screenP1.Y);
            renderer.DrawRectangle(screenRect, CanvasColor.Blue, 1.5f);
        }
    }

    private VisualPin? FindPinAtWorldPos(CanvasPoint worldPos, float pinHitRadius = 14f)
    {
        var radiusSq = pinHitRadius * pinHitRadius;

        foreach (var node in Model.Nodes.Values)
        {
            foreach (var pin in node.Pins)
            {
                var anchor = node.GetPinAnchorPosition(pin.Id);
                var dx = anchor.X - worldPos.X;
                var dy = anchor.Y - worldPos.Y;
                if ((dx * dx) + (dy * dy) <= radiusSq)
                {
                    return pin;
                }
            }
        }

        return null;
    }

    private VisualNode? FindNodeAtWorldPos(CanvasPoint worldPos)
    {
        // Hit-test in reverse order so top-most node is selected
        foreach (var node in Model.Nodes.Values.Reverse())
        {
            if (node.Bounds.Contains(worldPos))
            {
                return node;
            }
        }
        return null;
    }
}
