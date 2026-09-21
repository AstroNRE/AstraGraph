using System.Collections.Generic;
using AstraGraph.Core;
using AstraGraph.Editor.Core;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class CanvasModelTests
{
    [Test]
    public void Viewport_ScreenToWorldAndWorldToScreenRoundtrip()
    {
        var viewport = new CanvasViewport
        {
            PanX = 150f,
            PanY = -80f,
            Zoom = 1.5f
        };

        var originalScreen = new CanvasPoint(300f, 400f);
        var world = viewport.ScreenToWorld(originalScreen);
        var roundtripScreen = viewport.WorldToScreen(world);

        Assert.That(roundtripScreen.X, Is.EqualTo(originalScreen.X).Within(0.001f));
        Assert.That(roundtripScreen.Y, Is.EqualTo(originalScreen.Y).Within(0.001f));
    }

    [Test]
    public void Viewport_GridSnappingRoundsToGridSize()
    {
        var p1 = new CanvasPoint(14.9f, 33.1f);
        var snapped = CanvasViewport.SnapToGrid(p1, 16.0f);

        Assert.That(snapped.X, Is.EqualTo(16.0f));
        Assert.That(snapped.Y, Is.EqualTo(32.0f));
    }

    [Test]
    public void WireRouter_CalculateCubicBezier_GeneratesCorrectEndpointsAndCurvature()
    {
        var start = new CanvasPoint(100f, 200f);
        var end = new CanvasPoint(400f, 500f);

        var curve = WireRouter.CalculateCubicBezier(start, end);

        Assert.That(curve.Start, Is.EqualTo(start));
        Assert.That(curve.End, Is.EqualTo(end));
        Assert.That(curve.Control1.X, Is.GreaterThan(start.X));
        Assert.That(curve.Control2.X, Is.LessThan(end.X));

        var samples = WireRouter.SamplePoints(curve, 10);
        Assert.That(samples.Count, Is.EqualTo(11));
        Assert.That(samples[0], Is.EqualTo(start));
        Assert.That(samples[^1], Is.EqualTo(end));
    }

    [Test]
    public void WireRouter_DistanceToWire_DetectsProximityToSpline()
    {
        var start = new CanvasPoint(0f, 0f);
        var end = new CanvasPoint(200f, 0f);
        var curve = WireRouter.CalculateCubicBezier(start, end);

        // Point near the middle of horizontal wire
        var nearPoint = new CanvasPoint(100f, 2f);
        var farPoint = new CanvasPoint(100f, 100f);

        var distNear = WireRouter.DistanceToWire(nearPoint, curve);
        var distFar = WireRouter.DistanceToWire(farPoint, curve);

        Assert.That(distNear, Is.LessThan(10f));
        Assert.That(distFar, Is.GreaterThan(50f));
    }

    [Test]
    public void PinConnectionValidator_EnforcesConnectionRules()
    {
        var nodeA = NodeId.New();
        var nodeB = NodeId.New();

        var outExecA = new VisualPin(PinId.New(), nodeA, "Out", PinDirection.Output, PinKind.Execution, "Flow");
        var inExecA = new VisualPin(PinId.New(), nodeA, "In", PinDirection.Input, PinKind.Execution, "Flow");
        var outDataA = new VisualPin(PinId.New(), nodeA, "Value", PinDirection.Output, PinKind.Data, "System.Int32");

        var inExecB = new VisualPin(PinId.New(), nodeB, "In", PinDirection.Input, PinKind.Execution, "Flow");
        var inDataBInt64 = new VisualPin(PinId.New(), nodeB, "Target", PinDirection.Input, PinKind.Data, "System.Int64");
        var inDataBString = new VisualPin(PinId.New(), nodeB, "Name", PinDirection.Input, PinKind.Data, "System.String");

        var connections = new List<VisualConnection>();

        // 1. Cannot connect to self
        var selfCheck = PinConnectionValidator.Validate(outExecA, outExecA, connections);
        Assert.That(selfCheck.IsValid, Is.False);

        // 2. Cannot connect pins on same node
        var sameNodeCheck = PinConnectionValidator.Validate(outExecA, inExecA, connections);
        Assert.That(sameNodeCheck.IsValid, Is.False);

        // 3. Cannot connect Input to Input
        var inToInCheck = PinConnectionValidator.Validate(inExecA, inExecB, connections);
        Assert.That(inToInCheck.IsValid, Is.False);

        // 4. Cannot connect Execution to Data
        var kindMismatchCheck = PinConnectionValidator.Validate(outExecA, inDataBInt64, connections);
        Assert.That(kindMismatchCheck.IsValid, Is.False);

        // 5. Valid Execution connection
        var validExecCheck = PinConnectionValidator.Validate(outExecA, inExecB, connections);
        Assert.That(validExecCheck.IsValid, Is.True);

        connections.Add(new VisualConnection(outExecA.Id, inExecB.Id));

        // 6. Cannot add second incoming connection to same Execution Input
        var secondNodeC = NodeId.New();
        var outExecC = new VisualPin(PinId.New(), secondNodeC, "Out", PinDirection.Output, PinKind.Execution, "Flow");
        var duplicateExecInCheck = PinConnectionValidator.Validate(outExecC, inExecB, connections);
        Assert.That(duplicateExecInCheck.IsValid, Is.False);

        // 7. Compatible Data coercion (Int32 -> Int64)
        var validCoercionCheck = PinConnectionValidator.Validate(outDataA, inDataBInt64, connections);
        Assert.That(validCoercionCheck.IsValid, Is.True);

        // 8. Incompatible Data types (Int32 -> String)
        var invalidTypeCheck = PinConnectionValidator.Validate(outDataA, inDataBString, connections);
        Assert.That(invalidTypeCheck.IsValid, Is.False);
    }

    [Test]
    public void CanvasModel_AddRemoveAndSelectNodes()
    {
        var model = new CanvasModel();
        var node1 = new VisualNode(NodeId.New(), "Node1", "Math.Add", new CanvasPoint(50f, 50f));
        var node2 = new VisualNode(NodeId.New(), "Node2", "Math.Mul", new CanvasPoint(200f, 50f));

        model.AddNode(node1);
        model.AddNode(node2);

        Assert.That(model.Nodes.Count, Is.EqualTo(2));

        model.SelectNode(node1.Id);
        Assert.That(node1.IsSelected, Is.True);
        Assert.That(node2.IsSelected, Is.False);

        model.SelectArea(new CanvasRect(40f, 40f, 300f, 100f));
        Assert.That(node1.IsSelected, Is.True);
        Assert.That(node2.IsSelected, Is.True);

        model.RemoveNode(node1.Id);
        Assert.That(model.Nodes.Count, Is.EqualTo(1));
    }

    [Test]
    public void CanvasModel_ToFromGraphDocumentRoundtrip()
    {
        var model = new CanvasModel();
        model.Viewport.PanX = 25f;
        model.Viewport.PanY = 40f;
        model.Viewport.Zoom = 1.25f;

        var node1 = new VisualNode(NodeId.New(), "Entry", "Astra.EntryPoint", new CanvasPoint(100f, 150f));
        var pin1 = new VisualPin(PinId.New(), node1.Id, "Out", PinDirection.Output, PinKind.Execution, "Flow");
        node1.Pins.Add(pin1);

        var node2 = new VisualNode(NodeId.New(), "Log", "Core.Log", new CanvasPoint(300f, 150f));
        var pin2 = new VisualPin(PinId.New(), node2.Id, "In", PinDirection.Input, PinKind.Execution, "Flow");
        node2.Pins.Add(pin2);

        model.AddNode(node1);
        model.AddNode(node2);
        model.AddConnection(new VisualConnection(pin1.Id, pin2.Id));

        var graphId = GraphId.New();
        var doc = model.ToGraphDocument(graphId, "TestCanvasDoc", GraphKind.System, GraphSide.Server);

        var restoredModel = CanvasModel.FromGraphDocument(doc);

        Assert.That(restoredModel.Nodes.Count, Is.EqualTo(2));
        Assert.That(restoredModel.Connections.Count, Is.EqualTo(1));
        Assert.That(restoredModel.Viewport.PanX, Is.EqualTo(25f));
        Assert.That(restoredModel.Viewport.PanY, Is.EqualTo(40f));
        Assert.That(restoredModel.Viewport.Zoom, Is.EqualTo(1.25f));
        Assert.That(restoredModel.Nodes[node1.Id].Position.X, Is.EqualTo(100f));
    }

    [Test]
    public void CanvasCommandStack_AddMoveConnectUndoRedo()
    {
        var model = new CanvasModel();
        var stack = new CanvasCommandStack();

        var node1 = new VisualNode(NodeId.New(), "NodeA", "TypeA", new CanvasPoint(0f, 0f));
        var pin1 = new VisualPin(PinId.New(), node1.Id, "Out", PinDirection.Output, PinKind.Execution, "Flow");
        node1.Pins.Add(pin1);

        var node2 = new VisualNode(NodeId.New(), "NodeB", "TypeB", new CanvasPoint(200f, 0f));
        var pin2 = new VisualPin(PinId.New(), node2.Id, "In", PinDirection.Input, PinKind.Execution, "Flow");
        node2.Pins.Add(pin2);

        // 1. Add NodeA
        stack.Execute(new AddNodeCommand(model, node1));
        Assert.That(model.Nodes.Count, Is.EqualTo(1));

        // 2. Add NodeB
        stack.Execute(new AddNodeCommand(model, node2));
        Assert.That(model.Nodes.Count, Is.EqualTo(2));

        // 3. Connect pins
        var conn = new VisualConnection(pin1.Id, pin2.Id);
        stack.Execute(new ConnectPinsCommand(model, conn));
        Assert.That(model.Connections.Count, Is.EqualTo(1));

        // 4. Move NodeA
        var moves = new Dictionary<NodeId, (CanvasPoint OldPos, CanvasPoint NewPos)>
        {
            [node1.Id] = (new CanvasPoint(0f, 0f), new CanvasPoint(50f, 50f))
        };
        stack.Execute(new MoveNodesCommand(model, moves));
        Assert.That(model.Nodes[node1.Id].Position, Is.EqualTo(new CanvasPoint(50f, 50f)));

        // Undo move
        stack.Undo();
        Assert.That(model.Nodes[node1.Id].Position, Is.EqualTo(new CanvasPoint(0f, 0f)));

        // Redo move
        stack.Redo();
        Assert.That(model.Nodes[node1.Id].Position, Is.EqualTo(new CanvasPoint(50f, 50f)));

        // Undo move, undo connect, undo add NodeB
        stack.Undo();
        stack.Undo();
        Assert.That(model.Connections.Count, Is.EqualTo(0));
        stack.Undo();
        Assert.That(model.Nodes.Count, Is.EqualTo(1));

        // Redo all
        stack.Redo();
        stack.Redo();
        Assert.That(model.Nodes.Count, Is.EqualTo(2));
        Assert.That(model.Connections.Count, Is.EqualTo(1));
    }
}
