using AstraGraph.Core;
using AstraGraph.Runtime;
using NUnit.Framework;

namespace AstraGraph.Tests;

public sealed class SampleInteractEvent
{
    public bool Handled { get; set; }
    public string InteractionName { get; set; } = string.Empty;
}

public sealed class SampleDoorComponent { }

[TestFixture]
public sealed class HostAndEventRouterTests
{
    [Test]
    public void GraphScheduler_RespectsBeforeAfterAndPriorityRules()
    {
        var scheduler = new GraphScheduler();
        var executionLog = new List<string>();

        // System C: After "SystemB"
        scheduler.RegisterSystem(new SystemRegistration(
            GraphId.New(),
            "SystemC",
            Before: [],
            After: ["SystemB"],
            Priority: 0,
            UpdateCallback: (time, tick) => executionLog.Add("SystemC")));

        // System A: Before "SystemB"
        scheduler.RegisterSystem(new SystemRegistration(
            GraphId.New(),
            "SystemA",
            Before: ["SystemB"],
            After: [],
            Priority: 0,
            UpdateCallback: (time, tick) => executionLog.Add("SystemA")));

        // System B
        scheduler.RegisterSystem(new SystemRegistration(
            GraphId.New(),
            "SystemB",
            Before: [],
            After: [],
            Priority: 0,
            UpdateCallback: (time, tick) => executionLog.Add("SystemB")));

        scheduler.Update(0, 1);

        // Expected execution order: SystemA -> SystemB -> SystemC
        Assert.That(executionLog, Is.EqualTo(new[] { "SystemA", "SystemB", "SystemC" }));
    }

    [Test]
    public void GraphScheduler_DetectsCyclicDependencies()
    {
        var scheduler = new GraphScheduler();

        scheduler.RegisterSystem(new SystemRegistration(GraphId.New(), "Node1", Before: ["Node2"], After: []));
        scheduler.RegisterSystem(new SystemRegistration(GraphId.New(), "Node2", Before: ["Node1"], After: []));

        Assert.Throws<InvalidOperationException>(() => scheduler.GetOrderedSystems());
    }

    [Test]
    public void GraphEventRouter_DispatchesAndHandlesRefEvents()
    {
        var router = new GraphEventRouter();
        var graphId = GraphId.New();

        var handledCallCount = 0;

        router.Subscribe(
            componentType: typeof(SampleDoorComponent),
            eventType: typeof(SampleInteractEvent),
            graphId: graphId,
            entryPointName: "OnInteract",
            handler: (comp, ev) =>
            {
                if (ev is SampleInteractEvent interact)
                {
                    handledCallCount++;
                    interact.Handled = true;
                }
            });

        var door = new SampleDoorComponent();
        var ev = new SampleInteractEvent { InteractionName = "Open" };

        var isHandled = router.DispatchEvent(door, ev);

        Assert.That(isHandled, Is.True);
        Assert.That(ev.Handled, Is.True);
        Assert.That(handledCallCount, Is.EqualTo(1));

        // Test UnsubscribeGraph
        router.UnsubscribeGraph(graphId);
        ev.Handled = false;
        var secondDispatch = router.DispatchEvent(door, ev);

        Assert.That(secondDispatch, Is.False);
        Assert.That(ev.Handled, Is.False);
        Assert.That(handledCallCount, Is.EqualTo(1)); // not called again
    }

    [Test]
    public void AstraGraphHost_CoordinatesSchedulerAndContinuations()
    {
        var host = new AstraGraphHost();
        var sysTicked = false;

        host.Scheduler.RegisterSystem(new SystemRegistration(
            GraphId.New(),
            "TestHostSystem",
            Before: [],
            After: [],
            UpdateCallback: (time, tick) => sysTicked = true));

        host.Update(1.0, 1);
        Assert.That(sysTicked, Is.True);
    }
}
