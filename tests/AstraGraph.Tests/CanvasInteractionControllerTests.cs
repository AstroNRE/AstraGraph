using System.Collections.Generic;
using System.Linq;
using AstraGraph.Core;
using AstraGraph.Editor.Core;
using AstraGraph.Editor.Core.Search;
using AstraGraph.Editor.UI;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class CanvasInteractionControllerTests
{
    private CanvasModel _model = null!;
    private CanvasCommandStack _commandStack = null!;
    private NodePaletteIndexer _indexer = null!;
    private CanvasInteractionController _controller = null!;
    private VisualNode _nodeA = null!;
    private VisualNode _nodeB = null!;
    private VisualPin _outPinA = null!;
    private VisualPin _inPinB = null!;

    [SetUp]
    public void SetUp()
    {
        _model = new CanvasModel();
        _commandStack = new CanvasCommandStack();
        _indexer = new NodePaletteIndexer();
        _controller = new CanvasInteractionController(_model, _commandStack, _indexer);

        _nodeA = new VisualNode(NodeId.New(), "SourceNode", "Sample.Source", new CanvasPoint(96f, 96f)) { Width = 150f, Height = 80f };
        _outPinA = new VisualPin(PinId.New(), _nodeA.Id, "OutExec", PinDirection.Output, PinKind.Execution, "Flow");
        _nodeA.Pins.Add(_outPinA);

        _nodeB = new VisualNode(NodeId.New(), "TargetNode", "Sample.Target", new CanvasPoint(400f, 96f)) { Width = 150f, Height = 80f };
        _inPinB = new VisualPin(PinId.New(), _nodeB.Id, "InExec", PinDirection.Input, PinKind.Execution, "Flow");
        _nodeB.Pins.Add(_inPinB);

        _model.AddNode(_nodeA);
        _model.AddNode(_nodeB);
    }

    [Test]
    public void ClickNode_SelectsNode_AndClearsOtherSelections()
    {
        _controller.OnMouseDown(new CanvasPoint(120f, 120f), MouseButton.Left, ModifierKeys.None);
        _controller.OnMouseUp(new CanvasPoint(120f, 120f), MouseButton.Left);

        Assert.That(_nodeA.IsSelected, Is.True);
        Assert.That(_nodeB.IsSelected, Is.False);

        // Click node B
        _controller.OnMouseDown(new CanvasPoint(420f, 120f), MouseButton.Left, ModifierKeys.None);
        _controller.OnMouseUp(new CanvasPoint(420f, 120f), MouseButton.Left);

        Assert.That(_nodeA.IsSelected, Is.False);
        Assert.That(_nodeB.IsSelected, Is.True);
    }

    [Test]
    public void CtrlClickNode_AllowsMultiSelection()
    {
        _controller.OnMouseDown(new CanvasPoint(120f, 120f), MouseButton.Left, ModifierKeys.None);
        _controller.OnMouseUp(new CanvasPoint(120f, 120f), MouseButton.Left);

        _controller.OnMouseDown(new CanvasPoint(420f, 120f), MouseButton.Left, new ModifierKeys(Ctrl: true));
        _controller.OnMouseUp(new CanvasPoint(420f, 120f), MouseButton.Left);

        Assert.That(_nodeA.IsSelected, Is.True);
        Assert.That(_nodeB.IsSelected, Is.True);
    }

    [Test]
    public void DragNode_MovesPosition_AndSupportsUndoRedo()
    {
        var initialPos = _nodeA.Position;

        // Mouse down on node A
        _controller.OnMouseDown(new CanvasPoint(120f, 120f), MouseButton.Left, ModifierKeys.None);
        Assert.That(_controller.State, Is.EqualTo(InteractionState.DraggingNodes));

        // Drag 64px right, 32px down (multiples of 16 for grid snap)
        _controller.OnMouseMove(new CanvasPoint(184f, 152f));

        // Release
        _controller.OnMouseUp(new CanvasPoint(184f, 152f), MouseButton.Left);
        Assert.That(_controller.State, Is.EqualTo(InteractionState.Idle));

        Assert.That(_nodeA.Position.X, Is.EqualTo(initialPos.X + 64f).Within(0.001f));
        Assert.That(_nodeA.Position.Y, Is.EqualTo(initialPos.Y + 32f).Within(0.001f));

        // Undo
        _controller.OnKeyDown(EditorKeyCode.Z, new ModifierKeys(Ctrl: true));
        Assert.That(_nodeA.Position.X, Is.EqualTo(initialPos.X).Within(0.001f));
        Assert.That(_nodeA.Position.Y, Is.EqualTo(initialPos.Y).Within(0.001f));

        // Redo
        _controller.OnKeyDown(EditorKeyCode.Y, new ModifierKeys(Ctrl: true));
        Assert.That(_nodeA.Position.X, Is.EqualTo(initialPos.X + 64f).Within(0.001f));
        Assert.That(_nodeA.Position.Y, Is.EqualTo(initialPos.Y + 32f).Within(0.001f));
    }

    [Test]
    public void MiddleClickDrag_PansViewport()
    {
        var startPanX = _model.Viewport.PanX;
        var startPanY = _model.Viewport.PanY;

        _controller.OnMouseDown(new CanvasPoint(200f, 200f), MouseButton.Middle, ModifierKeys.None);
        Assert.That(_controller.State, Is.EqualTo(InteractionState.Panning));

        _controller.OnMouseMove(new CanvasPoint(260f, 240f));
        _controller.OnMouseUp(new CanvasPoint(260f, 240f), MouseButton.Middle);

        Assert.That(_controller.State, Is.EqualTo(InteractionState.Idle));
        Assert.That(_model.Viewport.PanX, Is.EqualTo(startPanX + 60f).Within(0.001f));
        Assert.That(_model.Viewport.PanY, Is.EqualTo(startPanY + 40f).Within(0.001f));
    }

    [Test]
    public void MouseWheel_ZoomsInAndOut()
    {
        var initialZoom = _model.Viewport.Zoom;

        // Zoom in
        _controller.OnMouseWheel(new CanvasPoint(200f, 200f), 120f);
        Assert.That(_model.Viewport.Zoom, Is.GreaterThan(initialZoom));

        // Zoom out
        _controller.OnMouseWheel(new CanvasPoint(200f, 200f), -240f);
        Assert.That(_model.Viewport.Zoom, Is.LessThan(initialZoom));
    }

    [Test]
    public void BoxSelection_SelectsEnclosingNodes()
    {
        // Drag box starting from (50, 50) to (300, 300) covering nodeA but not nodeB
        _controller.OnMouseDown(new CanvasPoint(50f, 50f), MouseButton.Left, ModifierKeys.None);
        Assert.That(_controller.State, Is.EqualTo(InteractionState.BoxSelecting));

        _controller.OnMouseMove(new CanvasPoint(300f, 300f));
        _controller.OnMouseUp(new CanvasPoint(300f, 300f), MouseButton.Left);

        Assert.That(_nodeA.IsSelected, Is.True);
        Assert.That(_nodeB.IsSelected, Is.False);
    }

    [Test]
    public void DragWire_BetweenCompatiblePins_CreatesConnection()
    {
        // Locate pin anchor in screen coords
        var pinAPos = _nodeA.GetPinAnchorPosition(_outPinA.Id);
        var pinBPos = _nodeB.GetPinAnchorPosition(_inPinB.Id);

        _controller.OnMouseDown(pinAPos, MouseButton.Left, ModifierKeys.None);
        Assert.That(_controller.State, Is.EqualTo(InteractionState.DraggingWire));
        Assert.That(_controller.DraggingSourcePin, Is.EqualTo(_outPinA));

        _controller.OnMouseMove(pinBPos);
        _controller.OnMouseUp(pinBPos, MouseButton.Left);

        Assert.That(_controller.State, Is.EqualTo(InteractionState.Idle));
        Assert.That(_model.Connections.Count, Is.EqualTo(1));
        var connection = _model.Connections[0];
        Assert.That(connection.SourcePinId, Is.EqualTo(_outPinA.Id));
        Assert.That(connection.TargetPinId, Is.EqualTo(_inPinB.Id));
    }

    [Test]
    public void DragWire_ToEmptySpace_GeneratesContextCompletionSuggestions()
    {
        var pinAPos = _nodeA.GetPinAnchorPosition(_outPinA.Id);
        var emptySpacePos = new CanvasPoint(800f, 800f);

        _controller.OnMouseDown(pinAPos, MouseButton.Left, ModifierKeys.None);
        _controller.OnMouseMove(emptySpacePos);
        _controller.OnMouseUp(emptySpacePos, MouseButton.Left);

        Assert.That(_controller.ActiveCompletionSuggestions, Is.Not.Null);
        Assert.That(_controller.ActiveCompletionSuggestions.Count, Is.GreaterThan(0));
    }

    [Test]
    public void Delete_Key_RemovesSelectedNodesAndWires()
    {
        _nodeA.IsSelected = true;
        _model.AddConnection(new VisualConnection(_outPinA.Id, _inPinB.Id));

        _controller.OnKeyDown(EditorKeyCode.Delete, ModifierKeys.None);

        Assert.That(_model.Nodes.ContainsKey(_nodeA.Id), Is.False);
        Assert.That(_model.Connections.Count, Is.EqualTo(0));

        // Undo restores node and connection
        _controller.OnKeyDown(EditorKeyCode.Z, new ModifierKeys(Ctrl: true));
        Assert.That(_model.Nodes.ContainsKey(_nodeA.Id), Is.True);
        Assert.That(_model.Connections.Count, Is.EqualTo(1));
    }

    [Test]
    public void SelectAll_And_CopyPaste_DuplicatesNodes()
    {
        // Select All
        _controller.OnKeyDown(EditorKeyCode.A, new ModifierKeys(Ctrl: true));
        Assert.That(_nodeA.IsSelected, Is.True);
        Assert.That(_nodeB.IsSelected, Is.True);

        // Copy
        _controller.OnKeyDown(EditorKeyCode.C, new ModifierKeys(Ctrl: true));
        Assert.That(_controller.Clipboard, Is.Not.Null);
        Assert.That(_controller.Clipboard!.Count, Is.EqualTo(2));

        // Paste
        _controller.OnKeyDown(EditorKeyCode.V, new ModifierKeys(Ctrl: true));
        Assert.That(_model.Nodes.Count, Is.EqualTo(4));

        var pastedNodes = _model.Nodes.Values.Where(n => n.Id != _nodeA.Id && n.Id != _nodeB.Id).ToList();
        Assert.That(pastedNodes.Count, Is.EqualTo(2));
        Assert.That(pastedNodes.All(n => n.IsSelected), Is.True);
        Assert.That(_nodeA.IsSelected, Is.False);
        Assert.That(_nodeB.IsSelected, Is.False);
    }

    [Test]
    public void Render_IssuesDrawCallsToRenderer()
    {
        _model.AddConnection(new VisualConnection(_outPinA.Id, _inPinB.Id));

        var renderer = new MockCanvasRenderer();
        _controller.Render(renderer);

        Assert.That(renderer.DrawCalls.Count, Is.GreaterThan(0));
        Assert.That(renderer.DrawCalls.Any(c => c.StartsWith("Bezier:", System.StringComparison.Ordinal)), Is.True);
        Assert.That(renderer.DrawCalls.Any(c => c.StartsWith("FillRect:", System.StringComparison.Ordinal)), Is.True);
        Assert.That(renderer.DrawCalls.Any(c => c.StartsWith("Text: 'SourceNode'", System.StringComparison.Ordinal)), Is.True);
    }
}
