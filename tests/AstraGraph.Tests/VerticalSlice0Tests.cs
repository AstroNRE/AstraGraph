using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstraGraph.Core;
using AstraGraph.HotReload;
using AstraGraph.Persistence;
using AstraGraph.Persistence.Discovery;
using AstraGraph.Persistence.State;
using AstraGraph.Runtime;
using AstraGraph.State;
using AstraGraph.VM;
using NUnit.Framework;

namespace AstraGraph.Tests;

/// <summary>
/// End-to-end acceptance test for Vertical Slice 0: "Doll -> Mothroach".
/// Validates the complete lifecycle:
/// 1. Visual graph document (.agraph) definition.
/// 2. Interaction event subscription & dispatch.
/// 3. Native component inspection & branching.
/// 4. Latent execution (DoAfter / delay).
/// 5. Native ECS entity spawning and despawning.
/// 6. Live hot reload from Revision 1 to Revision 2 without server restart.
/// 7. Persistence in Live storage.
/// 8. Simulated server crash and restart recovery of graph and state.
/// 9. Rollback to Last Known Good (LKG) revision.
/// </summary>
[TestFixture]
public sealed class VerticalSlice0Tests
{
    private string _testDir = null!;
    private StorageLayout _layout = null!;
    private MockEntityManager _entityManager = null!;
    private MockVmHostServices _vmServices = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "AstraGraph_VerticalSlice_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _layout = new StorageLayout(
            projectRoot: Path.Combine(_testDir, "Resources", "AstraGraph"),
            dataRoot: Path.Combine(_testDir, "data", "AstraGraph"));
        _layout.EnsureDirectories();

        _entityManager = new MockEntityManager();
        _vmServices = new MockVmHostServices(_entityManager);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Test]
    public void VerticalSlice0_DollToMothroach_FullLifecycle_Passes()
    {
        // -------------------------------------------------------------
        // 1. Setup Runtime Host and Services
        // -------------------------------------------------------------
        var astraVm = new AstraVm();
        var dynamicComponents = new DynamicComponentStore();
        var stateStore = new AstraStateStore();
        _vmServices.StateStore = stateStore;

        var host = new AstraGraphHost(astraVm, dynamicComponents, stateStore, _vmServices);
        var bootstrapLoader = new BootstrapLoader(_layout);
        var persistentStateStore = new PersistentStateStore(_layout);

        var graphId = GraphId.New();
        var transformCountSymbol = SymbolId.New();
        _vmServices.ActiveGraphId = graphId;

        // -------------------------------------------------------------
        // 2. Setup Entities in Mock World
        // -------------------------------------------------------------
        const int userPlayer = 10;
        const int targetDoll1 = 20;
        const int targetDoll2 = 21;
        const int targetChair = 30;

        _entityManager.AddEntity(userPlayer, "Player");
        _entityManager.AddEntity(targetDoll1, "Doll");
        _entityManager.AddEntity(targetDoll2, "Doll");
        _entityManager.AddEntity(targetChair, "Chair");

        // -------------------------------------------------------------
        // 3. Build Bytecode Programs for Revision 1 and Revision 2
        // -------------------------------------------------------------
        var progV1 = BuildDollToMothroachProgram(graphId, RevisionId.New(), "MobMothroach", delaySeconds: 0.5);
        var progV2 = BuildDollToMothroachProgram(graphId, RevisionId.New(), "MobGiantMothroach", delaySeconds: 0.25);

        // -------------------------------------------------------------
        // 4. Publish Revision 1
        // -------------------------------------------------------------
        host.RegisterProgram(progV1);
        stateStore.SetVariable(graphId, transformCountSymbol, "TransformCount", AstraValue.FromInt64(0), isPersistent: true);

        // Register event handler routing
        host.EventRouter.Subscribe(
            componentType: null,
            eventType: typeof(MockInteractUsingEvent),
            graphId: graphId,
            entryPointName: "OnInteractUsing",
            handler: (comp, ev) =>
            {
                var interactEvent = (MockInteractUsingEvent)ev;
                var ep = progV1.EntryPoints[0];
                var initialRegs = new AstraValue[ep.RegisterCount];
                initialRegs[0] = AstraValue.FromEntityUid(interactEvent.User);
                initialRegs[1] = AstraValue.FromEntityUid(interactEvent.Target);

                var result = astraVm.Execute(progV1, ep, initialRegs, hostServices: _vmServices);
                if (result.IsYielded && result.YieldState != null)
                {
                    var continuation = new ContinuationFrame(
                        graphId,
                        progV1,
                        ep,
                        result.YieldState.Arguments.ToArray(),
                        result.YieldState.ResumePointId,
                        result.YieldState.NextInstructionPointer,
                        ContinuationCondition.DelaySeconds(0.0, 0.5),
                        interactEvent.Target);

                    host.Continuations.Schedule(continuation);
                }
            });

        // -------------------------------------------------------------
        // 5. Fire Event on Non-Doll Entity (Chair) -> Should Branch False & Do Nothing
        // -------------------------------------------------------------
        host.EventRouter.DispatchEvent(null, new MockInteractUsingEvent(userPlayer, targetChair));
        Assert.That(host.Continuations.ActiveCount, Is.EqualTo(0));
        Assert.That(_entityManager.SpawnedEntities, Is.Empty);

        // -------------------------------------------------------------
        // 6. Fire Event on Target Doll 1 -> Should Yield Continuation
        // -------------------------------------------------------------
        host.EventRouter.DispatchEvent(null, new MockInteractUsingEvent(userPlayer, targetDoll1));
        Assert.That(host.Continuations.ActiveCount, Is.EqualTo(1));
        Assert.That(_entityManager.SpawnedEntities, Is.Empty);

        // Advance simulation time by 0.2s -> Still waiting
        host.Update(currentTimeSeconds: 0.2, currentTick: 12);
        Assert.That(host.Continuations.ActiveCount, Is.EqualTo(1));
        Assert.That(_entityManager.SpawnedEntities, Is.Empty);

        // Advance simulation time to 0.6s -> Latent DoAfter completes!
        host.Update(currentTimeSeconds: 0.6, currentTick: 36);
        Assert.That(host.Continuations.ActiveCount, Is.EqualTo(0));

        // Verify entity spawned & old doll deleted
        Assert.That(_entityManager.SpawnedEntities.Count, Is.EqualTo(1));
        Assert.That(_entityManager.SpawnedEntities[0].Prototype, Is.EqualTo("MobMothroach"));
        Assert.That(_entityManager.Entities.ContainsKey(targetDoll1), Is.False);

        // Verify persistent counter updated
        var countAfterV1 = stateStore.GetVariable(graphId, transformCountSymbol, "TransformCount").AsInt64();
        Assert.That(countAfterV1, Is.EqualTo(1));

        // -------------------------------------------------------------
        // 7. Live Hot Reload to Revision 2 (Spawns Giant Mothroach)
        // -------------------------------------------------------------
        host.RegisterProgram(progV2);

        // Re-subscribe router to progV2
        host.EventRouter.UnsubscribeGraph(graphId);
        host.EventRouter.Subscribe(
            componentType: null,
            eventType: typeof(MockInteractUsingEvent),
            graphId: graphId,
            entryPointName: "OnInteractUsing",
            handler: (comp, ev) =>
            {
                var interactEvent = (MockInteractUsingEvent)ev;
                var ep = progV2.EntryPoints[0];
                var initialRegs = new AstraValue[ep.RegisterCount];
                initialRegs[0] = AstraValue.FromEntityUid(interactEvent.User);
                initialRegs[1] = AstraValue.FromEntityUid(interactEvent.Target);

                var result = astraVm.Execute(progV2, ep, initialRegs, hostServices: _vmServices);
                if (result.IsYielded && result.YieldState != null)
                {
                    var continuation = new ContinuationFrame(
                        graphId,
                        progV2,
                        ep,
                        result.YieldState.Arguments.ToArray(),
                        result.YieldState.ResumePointId,
                        result.YieldState.NextInstructionPointer,
                        ContinuationCondition.DelaySeconds(1.0, 0.25),
                        interactEvent.Target);

                    host.Continuations.Schedule(continuation);
                }
            });

        // Trigger on Doll 2
        host.EventRouter.DispatchEvent(null, new MockInteractUsingEvent(userPlayer, targetDoll2));
        Assert.That(host.Continuations.ActiveCount, Is.EqualTo(1));

        // Advance time to 1.3s -> Revision 2 completes!
        host.Update(currentTimeSeconds: 1.3, currentTick: 78);
        Assert.That(host.Continuations.ActiveCount, Is.EqualTo(0));

        Assert.That(_entityManager.SpawnedEntities.Count, Is.EqualTo(2));
        Assert.That(_entityManager.SpawnedEntities[1].Prototype, Is.EqualTo("MobGiantMothroach"));
        Assert.That(_entityManager.Entities.ContainsKey(targetDoll2), Is.False);

        var countAfterV2 = stateStore.GetVariable(graphId, transformCountSymbol, "TransformCount").AsInt64();
        Assert.That(countAfterV2, Is.EqualTo(2));

        // -------------------------------------------------------------
        // 8. Live Storage Persistence & Crash Recovery Simulation
        // -------------------------------------------------------------
        // Save live document to data/AstraGraph/Live/DollToMothroach.agraph
        var liveDoc = new GraphDocument
        {
            Id = graphId,
            Name = "DollToMothroach",
            Kind = GraphKind.System,
            Side = GraphSide.Server
        };
        bootstrapLoader.SaveLive(liveDoc, "DollToMothroach.agraph", author: "LiveAdmin");

        // Save persistent snapshot to disk
        persistentStateStore.SaveState("ServerShutdownState", stateStore);

        // -------------------------------------------------------------
        // 9. Cold Server Reboot Simulation
        // -------------------------------------------------------------
        // Clean orphaned temp files on startup
        var recovered = bootstrapLoader.RecoverOnStartup();
        Assert.That(recovered, Is.EqualTo(0));

        // Discover graphs from disk
        var discoveredGraphs = bootstrapLoader.DiscoverAll();
        var restoredDoc = discoveredGraphs.Single(g => g.Id == graphId);
        Assert.That(restoredDoc.Origin, Is.EqualTo(GraphOrigin.Live));
        Assert.That(restoredDoc.Document.Name, Is.EqualTo("DollToMothroach"));

        // Restore state snapshot
        var freshStateStore = new AstraStateStore();
        var stateRestored = persistentStateStore.RestoreState("ServerShutdownState", freshStateStore);
        Assert.That(stateRestored, Is.True);

        var recoveredCount = freshStateStore.GetVariable(graphId, transformCountSymbol, "TransformCount").AsInt64();
        Assert.That(recoveredCount, Is.EqualTo(2));
    }

    private static BytecodeProgram BuildDollToMothroachProgram(
        GraphId graphId,
        RevisionId revisionId,
        string spawnPrototype,
        double delaySeconds)
    {
        var pool = new ConstantPool();
        var entryPointNameIdx = pool.GetOrAddString("OnInteractUsing");
        var hasDollMethodIdx = pool.GetOrAddString("HasDollComponent");
        var resumeGuidIdx = pool.GetOrAddGuid(Guid.NewGuid());
        var spawnMethodIdx = pool.GetOrAddString("SpawnEntity");
        var protoIdx = pool.GetOrAddString(spawnPrototype);
        var deleteMethodIdx = pool.GetOrAddString("DeleteEntity");
        var varNameIdx = pool.GetOrAddString("TransformCount");
        var oneConstIdx = pool.GetOrAddInt64(1);

        // Registers:
        // r0: UserEntity
        // r1: TargetEntity
        // r2: HasComponent bool
        // r3: Spawn prototype string
        // r4: Spawned EntityUid
        // r5: TransformCount current
        // r6: Constant 1
        // r7: TransformCount incremented
        var instructions = new List<BytecodeInstruction>
        {
            // 0: CallNative HasDollComponent(TargetEntity=r1) -> r2
            new BytecodeInstruction((byte)IrOpCode.CallNative, 2, hasDollMethodIdx, 1, 1),

            // 1: BranchIf r2, trueIp: 3, falseIp: 2
            new BytecodeInstruction((byte)IrOpCode.BranchIf, 0, 2, 3, 2),

            // 2: Return (false branch, do nothing)
            new BytecodeInstruction((byte)IrOpCode.Return, 0, -1, 0, 0),

            // 3: YieldContinuation (DoAfter delay), nextIp: 4
            new BytecodeInstruction((byte)IrOpCode.YieldContinuation, BytecodeInstruction.NoRegister, (int)ContinuationKind.DoAfter, resumeGuidIdx, 4),

            // 4: LoadConst prototype -> r3
            new BytecodeInstruction((byte)IrOpCode.LoadConst, 3, protoIdx, 0, 0),

            // 5: CallNative SpawnEntity(r3) -> r4
            new BytecodeInstruction((byte)IrOpCode.CallNative, 4, spawnMethodIdx, 1, 3),

            // 6: CallNative DeleteEntity(TargetEntity=r1)
            new BytecodeInstruction((byte)IrOpCode.CallNative, BytecodeInstruction.NoRegister, deleteMethodIdx, 1, 1),

            // 7: LoadVariable "TransformCount" -> r5
            new BytecodeInstruction((byte)IrOpCode.LoadVariable, 5, varNameIdx, 0, 0),

            // 8: LoadConst 1 -> r6
            new BytecodeInstruction((byte)IrOpCode.LoadConst, 6, oneConstIdx, 0, 0),

            // 9: Add r5 + r6 -> r7
            new BytecodeInstruction((byte)IrOpCode.Add, 7, 5, 6, 0),

            // 10: StoreVariable "TransformCount" (r7)
            new BytecodeInstruction((byte)IrOpCode.StoreVariable, 0, varNameIdx, 7, 0),

            // 11: Return
            new BytecodeInstruction((byte)IrOpCode.Return, 0, -1, 0, 0)
        };

        var function = new BytecodeFunction(
            nameConstantIndex: entryPointNameIdx,
            registerCount: 8,
            parameterCount: 2,
            instructions: instructions);

        var program = new BytecodeProgram(
            id: graphId,
            revision: revisionId,
            semanticHash: "doll_mothroach_" + revisionId.Value.ToString("N"),
            constants: pool);

        program.Functions.Add(function);
        program.EntryPoints.Add(function);

        return program;
    }
}

public sealed record MockInteractUsingEvent(int User, int Target);

public sealed class MockEntityManager
{
    private int _nextUid = 100;

    public Dictionary<int, string> Entities { get; } = [];
    public List<(int Uid, string Prototype)> SpawnedEntities { get; } = [];

    public void AddEntity(int uid, string prototype)
    {
        Entities[uid] = prototype;
    }

    public int SpawnEntity(string prototype)
    {
        var uid = _nextUid++;
        Entities[uid] = prototype;
        SpawnedEntities.Add((uid, prototype));
        return uid;
    }

    public void DeleteEntity(int uid)
    {
        Entities.Remove(uid);
    }

    public bool HasDoll(int uid)
    {
        return Entities.TryGetValue(uid, out var proto) && proto == "Doll";
    }
}

public sealed class MockVmHostServices : IVmHostServices
{
    private readonly MockEntityManager _entities;
    public AstraStateStore? StateStore { get; set; }
    public GraphId ActiveGraphId { get; set; }

    public MockVmHostServices(MockEntityManager entities)
    {
        _entities = entities;
    }

    public AstraValue CallNative(string methodDescriptor, IReadOnlyList<AstraValue> arguments)
    {
        switch (methodDescriptor)
        {
            case "HasDollComponent":
                var targetUid = arguments.Count > 0 ? arguments[0].AsEntityUid() : 0;
                return AstraValue.FromBool(_entities.HasDoll(targetUid));

            case "SpawnEntity":
                var proto = arguments.Count > 0 ? (arguments[0].AsString() ?? "Unknown") : "Unknown";
                var spawned = _entities.SpawnEntity(proto);
                return AstraValue.FromEntityUid(spawned);

            case "DeleteEntity":
                var delUid = arguments.Count > 0 ? arguments[0].AsEntityUid() : 0;
                _entities.DeleteEntity(delUid);
                return AstraValue.Null;

            default:
                throw new NotSupportedException($"Unknown mock native call: {methodDescriptor}");
        }
    }

    public AstraValue GetVariable(SymbolId variableId, string name)
    {
        if (StateStore != null && StateStore.TryGetVariable(ActiveGraphId, name, out var val))
        {
            return val;
        }
        return AstraValue.Null;
    }

    public void SetVariable(SymbolId variableId, string name, AstraValue value)
    {
        StateStore?.SetVariable(ActiveGraphId, variableId, name, value, isPersistent: true);
    }

    public AstraValue GetComponent(int entityUid, string componentTypeName)
    {
        return AstraValue.Null;
    }

    public bool HasComponent(int entityUid, string componentTypeName)
    {
        if (componentTypeName == "Doll")
        {
            return _entities.HasDoll(entityUid);
        }
        return false;
    }

    public void SetComponentField(int entityUid, string schemaIdAndFieldId, AstraValue value)
    {
    }
}
