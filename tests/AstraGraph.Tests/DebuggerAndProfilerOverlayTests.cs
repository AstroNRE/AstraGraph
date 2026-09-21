using System.Linq;
using AstraGraph.Core;
using AstraGraph.Editor.Core;
using AstraGraph.Editor.Core.Debugging;
using AstraGraph.Editor.Core.Profiling;
using AstraGraph.Editor.Core.Search;
using AstraGraph.Editor.UI;
using AstraGraph.Runtime.Debugging;
using AstraGraph.Runtime.Profiling;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class DebuggerAndProfilerOverlayTests
{
    private CanvasModel _model = null!;
    private VisualNode _nodeA = null!;
    private VisualNode _nodeB = null!;
    private VisualNode _nodeC = null!;
    private VisualConnection _connAB = null!;
    private GraphDebugger _graphDebugger = null!;
    private GraphProfiler _graphProfiler = null!;
    private GraphId _graphId;

    [SetUp]
    public void SetUp()
    {
        _graphId = GraphId.New();
        _model = new CanvasModel();
        _graphDebugger = new GraphDebugger();
        _graphProfiler = new GraphProfiler();

        _nodeA = new VisualNode(NodeId.New(), "SourceNode", "Sample.Source", new CanvasPoint(100f, 100f));
        var outPinA = new VisualPin(PinId.New(), _nodeA.Id, "Out", PinDirection.Output, PinKind.Execution, "Flow");
        _nodeA.Pins.Add(outPinA);

        _nodeB = new VisualNode(NodeId.New(), "ProcessNode", "Sample.Process", new CanvasPoint(300f, 100f));
        var inPinB = new VisualPin(PinId.New(), _nodeB.Id, "In", PinDirection.Input, PinKind.Execution, "Flow");
        var outPinB = new VisualPin(PinId.New(), _nodeB.Id, "Out", PinDirection.Output, PinKind.Execution, "Flow");
        _nodeB.Pins.Add(inPinB);
        _nodeB.Pins.Add(outPinB);

        _nodeC = new VisualNode(NodeId.New(), "ColdNode", "Sample.Cold", new CanvasPoint(500f, 100f));
        var inPinC = new VisualPin(PinId.New(), _nodeC.Id, "In", PinDirection.Input, PinKind.Execution, "Flow");
        _nodeC.Pins.Add(inPinC);

        _model.AddNode(_nodeA);
        _model.AddNode(_nodeB);
        _model.AddNode(_nodeC);

        _connAB = new VisualConnection(outPinA.Id, inPinB.Id);
        _model.AddConnection(_connAB);
    }

    [Test]
    public void VisualDebuggerSession_BreakpointToggle_AndClear()
    {
        var session = new VisualDebuggerSession(_model, _graphDebugger);

        Assert.That(session.HasBreakpoint(_nodeA.Id), Is.False);

        var eventFired = false;
        session.OnBreakpointChanged += (id, active) =>
        {
            if (id == _nodeA.Id && active)
            {
                eventFired = true;
            }
        };

        // Toggle on
        session.ToggleBreakpoint(_nodeA.Id);
        Assert.That(session.HasBreakpoint(_nodeA.Id), Is.True);
        Assert.That(eventFired, Is.True);

        // Toggle off
        session.ToggleBreakpoint(_nodeA.Id);
        Assert.That(session.HasBreakpoint(_nodeA.Id), Is.False);

        // Clear all
        session.ToggleBreakpoint(_nodeA.Id);
        session.ToggleBreakpoint(_nodeB.Id);
        Assert.That(session.HasBreakpoint(_nodeA.Id), Is.True);
        Assert.That(session.HasBreakpoint(_nodeB.Id), Is.True);

        session.ClearAllBreakpoints();
        Assert.That(session.HasBreakpoint(_nodeA.Id), Is.False);
        Assert.That(session.HasBreakpoint(_nodeB.Id), Is.False);
    }

    [Test]
    public void VisualDebuggerSession_Suspension_LocalsAndWatches()
    {
        var session = new VisualDebuggerSession(_model, _graphDebugger);

        var registers = new[]
        {
            AstraValue.FromInt64(12345),
            AstraValue.FromString("AstraStation"),
            AstraValue.FromBool(true)
        };

        var suspension = new DebugSuspension(_nodeA.Id, instructionPointer: 7, registers);
        session.HandleSuspension(suspension);

        Assert.That(session.IsPaused, Is.True);
        Assert.That(session.SuspendedNodeId, Is.EqualTo(_nodeA.Id));

        var locals = session.GetLocals();
        Assert.That(locals.Count, Is.EqualTo(3));
        Assert.That(locals[0].Name, Is.EqualTo("r0"));
        Assert.That(locals[0].Value, Is.EqualTo("12345"));
        Assert.That(locals[1].Value, Is.EqualTo("AstraStation"));

        // Watches
        session.AddWatch("MyPlayerId", "r0");
        session.AddWatch("MyName", "r1");
        session.AddWatch("InvalidReg", "r99");

        var watches = session.EvaluateWatches();
        Assert.That(watches.Count, Is.EqualTo(3));
        Assert.That(watches[0].Value, Is.EqualTo("12345"));
        Assert.That(watches[1].Value, Is.EqualTo("AstraStation"));
        Assert.That(watches[2].Value, Is.EqualTo("<out of range>"));

        // Call stack
        var callStack = session.GetCallStack();
        Assert.That(callStack.Count, Is.GreaterThan(0));
        Assert.That(callStack[0].NodeId, Is.EqualTo(_nodeA.Id));

        // Resume
        session.Resume();
        Assert.That(session.IsPaused, Is.False);
        Assert.That(session.SuspendedNodeId, Is.Null);
    }

    [Test]
    public void VisualProfilerOverlay_CalculatesHeatAndHotPaths()
    {
        // Record instructions for nodeA (10,000 hits), nodeB (8,500 hits), nodeC (0 hits)
        for (var i = 0; i < 10000; i++)
        {
            _graphProfiler.RecordInstruction(_graphId, _nodeA.Id);
        }
        for (var i = 0; i < 8500; i++)
        {
            _graphProfiler.RecordInstruction(_graphId, _nodeB.Id);
        }

        var overlay = new VisualProfilerOverlay(_graphProfiler, _graphId, _model);

        var heatA = overlay.GetNodeHeat(_nodeA.Id);
        var heatB = overlay.GetNodeHeat(_nodeB.Id);
        var heatC = overlay.GetNodeHeat(_nodeC.Id);

        Assert.That(heatA, Is.Not.Null);
        Assert.That(heatA!.HitCount, Is.EqualTo(10000));
        Assert.That(heatA.Level, Is.EqualTo(HeatLevel.Critical));
        Assert.That(heatA.FormattedText, Is.EqualTo("10.0k hits"));

        Assert.That(heatB, Is.Not.Null);
        Assert.That(heatB!.HitCount, Is.EqualTo(8500));
        Assert.That(heatB.Level, Is.EqualTo(HeatLevel.Critical).Or.EqualTo(HeatLevel.Hot));

        Assert.That(heatC, Is.Not.Null);
        Assert.That(heatC!.HitCount, Is.EqualTo(0));
        Assert.That(heatC.Level, Is.EqualTo(HeatLevel.Cold));

        // Connection AB connects two hot nodes -> hot path
        Assert.That(overlay.IsHotConnection(_connAB), Is.True);
    }

    [Test]
    public void CanvasInteractionController_WithDebuggerAndProfiler_RendersVisualMarkers()
    {
        var commandStack = new CanvasCommandStack();
        var indexer = new NodePaletteIndexer();
        var controller = new CanvasInteractionController(_model, commandStack, indexer);

        var debugSession = new VisualDebuggerSession(_model, _graphDebugger);
        var profilerOverlay = new VisualProfilerOverlay(_graphProfiler, _graphId, _model);

        controller.DebuggerSession = debugSession;
        controller.ProfilerOverlay = profilerOverlay;

        // 1. Select Node A and press F9 to toggle breakpoint
        _nodeA.IsSelected = true;
        controller.OnKeyDown(EditorKeyCode.F9, ModifierKeys.None);
        Assert.That(debugSession.HasBreakpoint(_nodeA.Id), Is.True);

        // 2. Simulate suspension on Node A
        var suspension = new DebugSuspension(_nodeA.Id, 1, [AstraValue.FromInt64(999)]);
        debugSession.HandleSuspension(suspension);

        // 3. Record profiler stats
        for (var i = 0; i < 5000; i++)
        {
            _graphProfiler.RecordInstruction(_graphId, _nodeA.Id);
            _graphProfiler.RecordInstruction(_graphId, _nodeB.Id);
        }
        profilerOverlay.Refresh();

        // 4. Render canvas
        var renderer = new MockCanvasRenderer();
        controller.Render(renderer);

        // Verify breakpoint circle was drawn
        Assert.That(renderer.DrawCalls.Any(c => c.StartsWith("FillCircle:", System.StringComparison.Ordinal)), Is.True);

        // Verify heat badge text was drawn
        Assert.That(renderer.DrawCalls.Any(c => c.Contains("hits", System.StringComparison.Ordinal)), Is.True);

        // Verify hot connection was drawn
        Assert.That(renderer.DrawCalls.Any(c => c.StartsWith("Bezier:", System.StringComparison.Ordinal)), Is.True);

        // 5. Press F5 to Resume
        controller.OnKeyDown(EditorKeyCode.F5, ModifierKeys.None);
        Assert.That(debugSession.IsPaused, Is.False);
    }
}
