using AstraGraph.Core;
using AstraGraph.Core.Events;
using AstraGraph.Runtime;
using AstraGraph.Runtime.Events;
using AstraGraph.State;
using AstraGraph.VM;
using NUnit.Framework;

namespace AstraGraph.Tests;

public struct TestDamageEvent
{
    public int Damage { get; set; }
    public bool Handled { get; set; }
}

[TestFixture]
public sealed class RobustEventBridgeTests
{
    [Test]
    public void TypedRefEventAccessor_MutatesStructDirectlyWithoutBoxing()
    {
        var ev = new TestDamageEvent { Damage = 10, Handled = false };
        var accessor = TypedRefEventAccessor<TestDamageEvent>.Instance;

        // 1. Read fields unboxed
        var currentDamage = accessor.GetField(ref ev, "Damage").AsInt32();
        var currentHandled = accessor.GetField(ref ev, "Handled").AsBool();
        Assert.That(currentDamage, Is.EqualTo(10));
        Assert.That(currentHandled, Is.False);

        // 2. Mutate fields directly into native ref memory
        accessor.SetField(ref ev, "Damage", AstraValue.FromInt32(currentDamage * 2));
        accessor.SetField(ref ev, "Handled", AstraValue.True);

        // 3. Verify original struct was mutated in place
        Assert.That(ev.Damage, Is.EqualTo(20));
        Assert.That(ev.Handled, Is.True);
    }

    [Test]
    public void GraphEventRouter_DispatchRefEvent_MutatesOriginalStructInPlace()
    {
        var router = new GraphEventRouter();
        var graphId = GraphId.New();

        // Subscribe a graph handler that doubles damage and marks Handled
        router.SubscribeRef<TestDamageEvent>(graphId, "OnDamage", (ref TestDamageEvent evArgs) =>
        {
            evArgs.Damage *= 2;
            evArgs.Handled = true;
        });

        var ev = new TestDamageEvent { Damage = 10, Handled = false };

        // Dispatch
        router.DispatchRefEvent(ref ev);

        // Verify true ref semantics
        Assert.That(ev.Damage, Is.EqualTo(20));
        Assert.That(ev.Handled, Is.True);
    }

    [Test]
    public void GraphEventRouter_DirectedStructWrite_CopiesBackToTheCaller()
    {
        var router = new GraphEventRouter();
        var descriptor = new GraphEventSubscription(GraphId.New(), "OnDamage", typeof(string), typeof(TestDamageEvent));
        router.SubscribeDirected(descriptor, (_, _, boxed) =>
        {
            Assert.That(AstraValueBox.WriteMember(boxed, nameof(TestDamageEvent.Damage), AstraValue.FromInt32(20)), Is.True);
        });

        var ev = new TestDamageEvent { Damage = 10 };
        router.DispatchComponentRefEvent("owner", "component", ref ev);

        Assert.That(ev.Damage, Is.EqualTo(20));
    }

    [Test]
    public void WriteMember_SetsAnInitOnlyRecordField()
    {
        object boxed = new RateProbe(1f);
        Assert.That(AstraValueBox.WriteMember(boxed, nameof(RateProbe.FireRate), AstraValue.FromDouble(4.5)), Is.True);
        Assert.That(((RateProbe)boxed).FireRate, Is.EqualTo(4.5f));
    }

    public readonly record struct RateProbe(float FireRate);

    [Test]
    public void GraphEventRouter_DispatchRefEvent_UnmutatedEventRemainsUntouched()
    {
        var router = new GraphEventRouter();
        var graphId = GraphId.New();

        // Subscribe an observing graph handler that reads but does not mutate
        var observedDamage = 0;
        router.SubscribeRef<TestDamageEvent>(graphId, "ObserveDamage", (ref TestDamageEvent evArgs) =>
        {
            observedDamage = evArgs.Damage;
        });

        var ev = new TestDamageEvent { Damage = 10, Handled = false };

        router.DispatchRefEvent(ref ev);

        Assert.That(observedDamage, Is.EqualTo(10));
        Assert.That(ev.Damage, Is.EqualTo(10));
        Assert.That(ev.Handled, Is.False, "Synthetic Handled=true must not be injected when graph does not mutate!");
    }

    [Test]
    public void LatentContinuation_SynchronousTimingAdvancesProperly()
    {
        var vm = new AstraVm();
        var scheduler = new ContinuationScheduler(vm);

        var pool = new ConstantPool();
        var instructions = new List<BytecodeInstruction>
        {
            new((byte)IrOpCode.Return, BytecodeInstruction.NoRegister, BytecodeInstruction.NoRegister, 0, 0)
        };
        var func = new BytecodeFunction(0, 0, 0, instructions);
        var program = new BytecodeProgram(GraphId.New(), RevisionId.New(), "hash", pool);

        var frame = new ContinuationFrame(
            program.Id,
            program,
            func,
            capturedRegisters: new AstraValue[1],
            resumePointId: Guid.NewGuid(),
            instructionPointer: 0,
            condition: ContinuationCondition.DelaySeconds(10.0, 1.0),
            targetEntity: 100);

        scheduler.Schedule(frame);

        // Before 1.0 second elapsed (simulation time 10.5s, tick 30)
        scheduler.Update(currentTimeSeconds: 10.5, currentTick: 30);
        Assert.That(scheduler.ActiveCount, Is.EqualTo(1), "Continuation should still be pending before duration.");

        // At or after 1.0 second elapsed (simulation time 11.0s, tick 60)
        scheduler.Update(currentTimeSeconds: 11.0, currentTick: 60);
        Assert.That(scheduler.ActiveCount, Is.EqualTo(0), "Continuation should wake and complete.");
    }

    [Test]
    public void EntityDeletion_DeterministicallyCleansComponentsContinuationsAndState()
    {
        var componentStore = new DynamicComponentStore();
        var stateStore = new AstraStateStore();
        var continuationScheduler = new ContinuationScheduler();

        const int entityUid = 777;

        // 1. Assign dynamic component
        var schema = new SchemaType(SchemaId.New(), "TestComp", true, [new SchemaField(FieldId.New(), "Val", PrimitiveType.Int64)]);
        componentStore.AddComponent(entityUid, schema, [AstraValue.FromInt64(42)]);
        Assert.That(componentStore.HasComponent(entityUid, schema.Id), Is.True);

        // 2. Assign state variable
        stateStore.SetEntityVariable(entityUid, "CachedEnergy", AstraValue.FromInt64(100));
        Assert.That(stateStore.GetEntityVariable(entityUid, "CachedEnergy").AsInt64(), Is.EqualTo(100));

        // 3. Schedule pending continuation
        var pool = new ConstantPool();
        var instructions = new List<BytecodeInstruction>
        {
            new((byte)IrOpCode.Return, BytecodeInstruction.NoRegister, BytecodeInstruction.NoRegister, 0, 0)
        };
        var func = new BytecodeFunction(0, 0, 0, instructions);
        var program = new BytecodeProgram(GraphId.New(), RevisionId.New(), "hash", pool);
        var frame = new ContinuationFrame(
            program.Id,
            program,
            func,
            capturedRegisters: new AstraValue[1],
            resumePointId: Guid.NewGuid(),
            instructionPointer: 0,
            condition: ContinuationCondition.DelaySeconds(0.0, 10.0),
            targetEntity: entityUid);
        continuationScheduler.Schedule(frame);
        Assert.That(continuationScheduler.ActiveCount, Is.EqualTo(1));

        // Simulate Entity Deletion cleanup
        componentStore.ClearEntity(entityUid);
        stateStore.ClearEntity(entityUid);
        continuationScheduler.CancelByEntity(entityUid);

        // Verify total cleanup
        Assert.That(componentStore.HasComponent(entityUid, schema.Id), Is.False);
        Assert.That(stateStore.GetEntityVariable(entityUid, "CachedEnergy"), Is.EqualTo(AstraValue.Null));
        Assert.That(continuationScheduler.ActiveCount, Is.EqualTo(0));
    }

    [Test]
    public void GraphScheduler_EnforcesExactOrdering_NativeA_Graph_NativeB()
    {
        var scheduler = new GraphScheduler();
        var executionTrace = new List<string>();

        // Native System A
        scheduler.RegisterSystem(new SystemRegistration(
            GraphId.New(),
            "NativeSystemA",
            Before: [],
            After: [],
            Priority: 100,
            UpdateCallback: (_, _) => executionTrace.Add("NativeA")));

        // Astra Graph: Must execute After NativeSystemA and Before NativeSystemB
        scheduler.RegisterSystem(new SystemRegistration(
            GraphId.New(),
            "GameplayGraph",
            Before: ["NativeSystemB"],
            After: ["NativeSystemA"],
            Priority: 50,
            UpdateCallback: (_, _) => executionTrace.Add("Graph")));

        // Native System B
        scheduler.RegisterSystem(new SystemRegistration(
            GraphId.New(),
            "NativeSystemB",
            Before: [],
            After: [],
            Priority: 10,
            UpdateCallback: (_, _) => executionTrace.Add("NativeB")));

        // Execute scheduler update
        scheduler.Update(0.0, 1);

        // Assert deterministic order
        Assert.That(executionTrace, Is.EqualTo(new[] { "NativeA", "Graph", "NativeB" }));
    }
}
