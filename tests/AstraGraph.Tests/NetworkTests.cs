using System;
using System.Collections.Generic;
using System.Linq;
using AstraGraph.Core;
using AstraGraph.Runtime;
using AstraGraph.Runtime.Network;
using AstraGraph.State;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class NetworkTests
{
    [Test]
    public void PredictionSafetyChecker_RejectsUnsafeCalls_InPredictedGraph()
    {
        var checker = new PredictionSafetyChecker(["Math.Sin", "Entity.GetPosition"]);

        var graphId = GraphId.New();
        var safeProgram = new IrProgram(graphId, "SafePredictedLogic", GraphKind.System, GraphSide.SharedPredicted);
        var entryBlock = new IrBasicBlock(0, "entry");
        var safeFunc = new IrFunction("Simulate", [], PrimitiveType.Void, entryBlock);
        safeProgram.Functions.Add(safeFunc);

        // Safe call
        entryBlock.AddInstruction(new IrInstruction(IrOpCode.CallNative, null, [], StringPayload: "Math.Sin"));
        entryBlock.SetTerminator(new IrInstruction(IrOpCode.Return));

        var bag = new DiagnosticBag();
        var isSafe = checker.Validate(safeProgram, bag);
        Assert.That(isSafe, Is.True);
        Assert.That(bag.HasErrors, Is.False);

        // Unsafe call (e.g. DateTime.Now or unregistered server method)
        var unsafeProgram = new IrProgram(GraphId.New(), "UnsafePredictedLogic", GraphKind.System, GraphSide.SharedPredicted);
        var unsafeBlock = new IrBasicBlock(0, "entry");
        var unsafeFunc = new IrFunction("BadSimulate", [], PrimitiveType.Void, unsafeBlock);
        unsafeProgram.Functions.Add(unsafeFunc);

        unsafeBlock.AddInstruction(new IrInstruction(IrOpCode.CallNative, null, [], StringPayload: "System.DateTime.Now"));
        unsafeBlock.AddInstruction(new IrInstruction(IrOpCode.CallNative, null, [], StringPayload: "ServerOnlySystem.DoServerStuff"));
        unsafeBlock.SetTerminator(new IrInstruction(IrOpCode.Return));

        var unsafeBag = new DiagnosticBag();
        var isUnsafe = checker.Validate(unsafeProgram, unsafeBag);
        Assert.That(isUnsafe, Is.False);
        Assert.That(unsafeBag.HasErrors, Is.True);
        Assert.That(unsafeBag.Count, Is.EqualTo(2));
        Assert.That(unsafeBag.All(d => d.Code == "PRED001"), Is.True);
    }

    [Test]
    public void SharedActivationCoordinator_SynchronizesActivationAtPreciseTick()
    {
        var host = new AstraGraphHost();
        var coordinator = new SharedActivationCoordinator(host) { LeadTicks = 5 };

        var graphId = GraphId.New();
        var revV1 = RevisionId.New();
        var poolV1 = new ConstantPool();
        var progV1 = new BytecodeProgram(graphId, revV1, "hash_v1", poolV1);
        host.RegisterProgram(progV1);

        var revV2 = RevisionId.New();
        var poolV2 = new ConstantPool();
        var progV2 = new BytecodeProgram(graphId, revV2, "hash_v2", poolV2);

        // Schedule on tick 10 -> activation tick will be 15
        var entry = coordinator.ScheduleActivation(progV2, "schema_hash_v2", GraphSide.Shared, currentTick: 10);
        Assert.That(entry.ActivationTick, Is.EqualTo(15));
        Assert.That(entry.Revision, Is.EqualTo(revV2));

        // Ticks 11, 12, 13, 14: program should still be v1
        for (var tick = 11; tick <= 14; tick++)
        {
            var committed = coordinator.Update(tick);
            Assert.That(committed, Is.EqualTo(0));
            Assert.That(host.GetProgram(graphId)?.Revision, Is.EqualTo(revV1));
        }

        // Tick 15: activation committed!
        var committedAt15 = coordinator.Update(15);
        Assert.That(committedAt15, Is.EqualTo(1));
        Assert.That(host.GetProgram(graphId)?.Revision, Is.EqualTo(revV2));
    }

    [Test]
    public void DeltaReplicationManager_OnlyTransmitsDirtyFields_AndClearsDirtyFlags()
    {
        var serverStore = new DynamicComponentStore();
        var clientStore = new DynamicComponentStore();

        var schemaId = SchemaId.New();
        var fHealth = FieldId.New();
        var fShield = FieldId.New();
        var fIsAlive = FieldId.New();

        var fields = new[]
        {
            new SchemaField(fHealth, "Health", PrimitiveType.Float64),
            new SchemaField(fShield, "Shield", PrimitiveType.Float64),
            new SchemaField(fIsAlive, "IsAlive", PrimitiveType.Bool)
        };
        var schema = new SchemaType(schemaId, "CombatStats", true, fields);

        const int entity1 = 42;
        var initialValues = new[]
        {
            AstraValue.FromDouble(100.0),
            AstraValue.FromDouble(50.0),
            AstraValue.FromBool(true)
        };

        var serverComp = serverStore.AddComponent(entity1, schema, initialValues);
        clientStore.AddComponent(entity1, schema, initialValues);

        // Clear initial dirty mask
        serverComp.ClearDirty();

        // 1. No changes -> 0 deltas
        var deltas1 = DeltaReplicationManager.CollectDirtyDeltas(serverStore);
        Assert.That(deltas1, Is.Empty);

        // 2. Modify only Health on server
        serverComp.SetField(0, AstraValue.FromDouble(75.5));
        Assert.That(serverComp.HasAnyDirty, Is.True);
        Assert.That(serverComp.IsDirty(0), Is.True);
        Assert.That(serverComp.IsDirty(1), Is.False);
        Assert.That(serverComp.IsDirty(2), Is.False);

        // Collect deltas: should contain exactly 1 packet with 1 field
        var deltas2 = DeltaReplicationManager.CollectDirtyDeltas(serverStore);
        Assert.That(deltas2.Count, Is.EqualTo(1));
        Assert.That(deltas2[0].EntityUid, Is.EqualTo(new AstraEntityId(entity1)));
        Assert.That(deltas2[0].DirtyFields.Count, Is.EqualTo(1));
        Assert.That(deltas2[0].DirtyFields[0].FieldId, Is.EqualTo(fHealth));
        Assert.That(deltas2[0].DirtyFields[0].Value.AsDouble(), Is.EqualTo(75.5));

        // After collecting, dirty flag must be cleared
        Assert.That(serverComp.HasAnyDirty, Is.False);

        // 3. Apply to client
        DeltaReplicationManager.ApplyDeltas(clientStore, deltas2);
        var clientComp = clientStore.GetComponent(entity1, schemaId);
        Assert.That(clientComp.GetField(0).AsDouble(), Is.EqualTo(75.5));
        Assert.That(clientComp.GetField(1).AsDouble(), Is.EqualTo(50.0)); // Unchanged
        Assert.That(clientComp.GetField(2).AsBool(), Is.True);           // Unchanged

        // 4. Consecutive collection produces no packets
        var deltas3 = DeltaReplicationManager.CollectDirtyDeltas(serverStore);
        Assert.That(deltas3, Is.Empty);
    }
}
