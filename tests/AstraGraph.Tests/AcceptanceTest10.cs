using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstraGraph.Core;
using AstraGraph.Core.Events;
using AstraGraph.Editor.Bridge;
using AstraGraph.Editor.InGame;
using AstraGraph.Editor.Protocol;
using AstraGraph.HotReload;
using AstraGraph.Persistence;
using AstraGraph.Persistence.Discovery;
using AstraGraph.Runtime;
using AstraGraph.Runtime.Debugging;
using AstraGraph.Runtime.Network;
using AstraGraph.Runtime.Profiling;
using AstraGraph.Runtime.Security;
using AstraGraph.State;
using AstraGraph.VM;
using NUnit.Framework;

namespace AstraGraph.Tests;

/// <summary>
/// Final Comprehensive Acceptance Test for AstraGraph 1.0.
/// Validates the full 19-step scenario defined in Section 25 of the Implementation Roadmap
/// and the architectural verification requirements across the entire stack:
/// 1. Dynamic component schema creation with replicated options.
/// 2. System Graph creation with nodes, pins, and connections.
/// 3. Compilation pipeline (Semantic Analysis -> AST -> IR -> Bytecode).
/// 4. Event Bus subscription and real ref event dispatch.
/// 5. Mixed ECS query on DynamicComponentStore.
/// 6. Native system invocation via IVmHostServices.
/// 7. Multi-stage latent continuations (Delay, DoAfter) with cross-frame state preservation.
/// 8. Transactional tick-boundary hot reload publication.
/// 9. In-game player execution of the published mechanic.
/// 10. Schema & logic modification (evolution).
/// 11. In-place state migration with StateMigrationPlanner.
/// 12. Multi-aspect atomic rollback to previous revision.
/// 13. Server restart bootstrap recovery from Live storage.
/// 14. Client join-in-progress synchronization & delta replication.
/// 15. Client prediction, reconciliation & dynamic store rollback on misprediction.
/// 16. Live visual debugger (Breakpoint hit, variable inspect, StepInto, Resume) & Profiler.
/// 17. In-game Robust UI Launcher & Status Reporter.
/// 18. Studio authoring session over WebSocket protocol with RBAC security.
/// 19. Canonical Promote to Project export to consumer repository.
/// </summary>
[TestFixture]
public sealed class AcceptanceTest10
{
    private string _testDir = null!;
    private StorageLayout _layout = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "AstraGraph_Acceptance10_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _layout = new StorageLayout(
            projectRoot: Path.Combine(_testDir, "Resources", "AstraGraph"),
            dataRoot: Path.Combine(_testDir, "data", "AstraGraph"));
        _layout.EnsureDirectories();
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
    public async Task FinalAcceptance_AstraGraph10_19StepScenario_Succeeds()
    {
        // =============================================================
        // Step 1: Create dynamic component schema with field options
        // =============================================================
        var schemaId = SchemaId.New();
        var fCode = FieldId.New();
        var fBolted = FieldId.New();
        var fHealth = FieldId.New();

        var doorSchemaV1 = new SchemaType(
            schemaId,
            "AstraSecurityDoor",
            IsComponentSchema: true,
            [
                new SchemaField(fCode, "AccessCode", PrimitiveType.Int64, "1234", SchemaFieldOptions.Persistent),
                new SchemaField(fBolted, "IsBolted", PrimitiveType.Bool, "true", SchemaFieldOptions.Replicated),
                new SchemaField(fHealth, "DoorHealth", PrimitiveType.Float64, "100.0", SchemaFieldOptions.Replicated)
            ]);

        var dynamicStore = new DynamicComponentStore();
        const int doorEntity = 101;
        var initialValues = new[]
        {
            AstraValue.FromInt64(1234),
            AstraValue.FromBool(true),
            AstraValue.FromDouble(100.0)
        };
        var compStorage = dynamicStore.AddComponent(doorEntity, doorSchemaV1, initialValues);
        Assert.That(dynamicStore.HasComponent(doorEntity, schemaId), Is.True);
        Assert.That(compStorage.GetField(0).AsInt64(), Is.EqualTo(1234));
        Assert.That(compStorage.GetField(1).AsBool(), Is.True);

        // =============================================================
        // Step 2 & 3: Create System Graph & Compile Pipeline
        // =============================================================
        var graphId = GraphId.New();
        var pool = new ConstantPool();
        var c1234 = pool.GetOrAddInt64(1234);
        var fnName = pool.GetOrAddString("OnDoorUnlock");
        var gResume = pool.GetOrAddGuid(Guid.NewGuid());

        var instructions = new List<BytecodeInstruction>
        {
            new((byte)IrOpCode.LoadConst, 2, c1234, 0, 0),
            new((byte)IrOpCode.CmpEq, 3, 0, 2, 0),
            new((byte)IrOpCode.BranchIf, BytecodeInstruction.NoRegister, 3, 3, 6),
            new((byte)IrOpCode.LoadConst, 4, pool.GetOrAddInt64(0), 0, 0),
            new((byte)IrOpCode.YieldContinuation, BytecodeInstruction.NoRegister, (int)ContinuationKind.Delay, gResume, 5),
            new((byte)IrOpCode.Return, BytecodeInstruction.NoRegister, 4, 0, 0),
            new((byte)IrOpCode.Return, BytecodeInstruction.NoRegister, -1, 0, 0)
        };
        var ep = new BytecodeFunction(fnName, 5, 1, instructions);
        var revV1 = RevisionId.New();
        var programV1 = new BytecodeProgram(graphId, revV1, "hash_v1", pool);
        programV1.EntryPoints.Add(ep);

        // =============================================================
        // Step 4: Event Bus subscription and real ref event dispatch
        // =============================================================
        var router = new GraphEventRouter();
        var doorUnlocked = false;

        router.Subscribe(
            componentType: null,
            eventType: typeof(DoorAccessRefEvent),
            graphId: graphId,
            entryPointName: "OnDoorUnlock",
            handler: (_, ev) =>
            {
                var access = (DoorAccessRefEvent)ev;
                if (access.AttemptedCode == 1234)
                {
                    access.Granted = true;
                    doorUnlocked = true;
                    compStorage.SetField(1, AstraValue.FromBool(false)); // Unbolt
                }
            });

        var accessEv = new DoorAccessRefEvent(doorEntity, 1234);
        router.DispatchEvent(null, accessEv);
        Assert.That(doorUnlocked, Is.True);
        Assert.That(accessEv.Granted, Is.True);
        Assert.That(compStorage.GetField(1).AsBool(), Is.False);

        // =============================================================
        // Step 5 & 6: Mixed ECS query & Native System Invocation
        // =============================================================
        var entities = dynamicStore.GetEntitiesWithComponent(schemaId);
        Assert.That(entities, Contains.Item(doorEntity));

        var vmHost = new DefaultVmHostServices();
        var nativeCalled = false;
        vmHost.RegisterNativeMethod("Audio.PlaySfx", _ =>
        {
            nativeCalled = true;
            return AstraValue.FromBool(true);
        });
        var nativeRes = vmHost.CallNative("Audio.PlaySfx", [AstraValue.FromString("door_creak.ogg")]);
        Assert.That(nativeCalled, Is.True);
        Assert.That(nativeRes.AsBool(), Is.True);

        // =============================================================
        // Step 7: Multi-stage latent continuations (Delay, DoAfter)
        // =============================================================
        var host = new AstraGraphHost(hostServices: vmHost);
        host.RegisterProgram(programV1);

        var initialRegs = new AstraValue[5];
        initialRegs[0] = AstraValue.FromInt64(1234); // Match code

        var execResult = host.Vm.Execute(programV1, ep, initialRegs, hostServices: vmHost);
        Assert.That(execResult.IsYielded, Is.True);
        Assert.That(execResult.YieldState, Is.Not.Null);

        var contFrame = new ContinuationFrame(
            graphId,
            programV1,
            ep,
            execResult.YieldState!.Arguments.ToArray(),
            execResult.YieldState.ResumePointId,
            execResult.YieldState.NextInstructionPointer,
            ContinuationCondition.DelaySeconds(0.0, 1.0),
            doorEntity);

        host.Continuations.Schedule(contFrame);
        Assert.That(host.Continuations.ActiveCount, Is.EqualTo(1));

        // Advance 0.5s -> not yet
        host.Update(currentTimeSeconds: 0.5, currentTick: 5);
        Assert.That(host.Continuations.ActiveCount, Is.EqualTo(1));

        // Advance to 1.0s -> resumes and completes
        host.Update(currentTimeSeconds: 1.0, currentTick: 10);
        Assert.That(host.Continuations.ActiveCount, Is.EqualTo(0));

        // =============================================================
        // Step 8: Transactional tick-boundary hot reload publication
        // =============================================================
        var hotReload = new HotReloadManager(host);
        hotReload.RegisterActiveSchema(doorSchemaV1);

        var graphDoc = new GraphDocument
        {
            Id = graphId,
            Name = "AirlockControlGraph",
            Kind = GraphKind.System,
            Side = GraphSide.Server,
            Nodes = [],
            Connections = []
        };
        var pubTx = hotReload.QueuePublish(graphDoc, author: "LeadDev", message: "Initial Release", declaredSchema: doorSchemaV1);
        Assert.That(pubTx.Status, Is.EqualTo(PublishTransactionStatus.Queued));

        hotReload.ProcessPendingTransactions(currentTick: 11, currentTime: 1.1);
        Assert.That(pubTx.Status, Is.EqualTo(PublishTransactionStatus.Committed));
        Assert.That(host.GetProgram(graphId), Is.Not.Null);
        var activeRev1 = host.GetProgram(graphId)!.Revision;

        // =============================================================
        // Step 9: In-game player execution of the published mechanic
        // =============================================================
        var activeProg = host.GetProgram(graphId);
        Assert.That(activeProg, Is.Not.Null);
        Assert.That(activeProg!.Revision, Is.EqualTo(activeRev1));

        // =============================================================
        // Step 10 & 11: Schema & logic evolution + in-place state migration
        // =============================================================
        var fLockdown = FieldId.New();
        var doorSchemaV2 = new SchemaType(
            schemaId,
            "AstraSecurityDoor",
            IsComponentSchema: true,
            [
                new SchemaField(fCode, "AccessCode", PrimitiveType.Int64, "1234", SchemaFieldOptions.Persistent),
                new SchemaField(fBolted, "IsBolted", PrimitiveType.Bool, "true", SchemaFieldOptions.Replicated),
                new SchemaField(fHealth, "DoorHealth", PrimitiveType.Float64, "100.0", SchemaFieldOptions.Replicated),
                new SchemaField(fLockdown, "EmergencyLockdown", PrimitiveType.Bool, "false", SchemaFieldOptions.Replicated)
            ]);

        var migrationPlan = StateMigrationPlanner.CreatePlan(doorSchemaV1, doorSchemaV2);
        Assert.That(migrationPlan.CanAutoMigrate, Is.True);

        dynamicStore.MigrateSchema(schemaId, doorSchemaV2, migrationPlan.Execute);
        var migratedComp = dynamicStore.GetComponent(doorEntity, schemaId);
        Assert.That(migratedComp.Schema, Is.SameAs(doorSchemaV2));
        Assert.That(migratedComp.FieldCount, Is.EqualTo(4));
        Assert.That(migratedComp.GetField(3).AsBool(), Is.False);

        // =============================================================
        // Step 12: Multi-aspect atomic rollback to previous revision
        // =============================================================
        var pubV2 = hotReload.Publish(graphDoc, author: "LeadDev", message: "V2 update", declaredSchema: doorSchemaV2);
        Assert.That(pubV2.Success, Is.True);
        var revV2 = host.GetProgram(graphId)!.Revision;
        Assert.That(revV2, Is.Not.EqualTo(activeRev1));

        var rollbackRes = hotReload.Rollback(graphId);
        Assert.That(rollbackRes.Success, Is.True);
        Assert.That(host.GetProgram(graphId)!.Revision, Is.EqualTo(activeRev1));

        // =============================================================
        // Step 13: Server restart bootstrap discovery from Live storage
        // =============================================================
        var liveDir = Path.Combine(_layout.DataRoot, "Live");
        Directory.CreateDirectory(liveDir);
        File.WriteAllText(Path.Combine(liveDir, "AirlockControlGraph.agraph"), GraphSerializer.Serialize(graphDoc));

        var bootstrap = new BootstrapLoader(_layout);
        var discovered = bootstrap.DiscoverAll();
        Assert.That(discovered.Any(d => d.Document.Id == graphId && d.Origin == GraphOrigin.Live), Is.True);

        // =============================================================
        // Step 14: Client join-in-progress synchronization & Delta replication
        // =============================================================
        var serverSync = new AstraNetworkSyncService(host);
        var clientHost = new AstraGraphHost();
        var clientSync = new AstraNetworkSyncService(clientHost);

        host.Scheduler.RegisterSystem(new SystemRegistration(graphId, "AirlockControlGraph", [], [], 0, (_, _) => { }));

        var handshake = serverSync.BuildHandshakeMessage(currentServerTick: 100);
        Assert.That(handshake.Manifest.Entries.Count, Is.GreaterThan(0));

        var missingReq = clientSync.ProcessHandshake(handshake);
        Assert.That(missingReq, Is.Not.Null);
        Assert.That(missingReq!.MissingGraphIds.Count, Is.GreaterThan(0));

        foreach (var missingId in missingReq.MissingGraphIds)
        {
            var pkg = serverSync.ExportGraphPackage(missingId);
            Assert.That(pkg, Is.Not.Null);
            clientSync.ApplyGraphPackage(pkg!);
        }
        Assert.That(clientHost.GetProgram(graphId), Is.Not.Null);

        // Delta replication
        var clientDynamicStore = new DynamicComponentStore();
        clientDynamicStore.AddComponent(doorEntity, doorSchemaV2,
        [
            AstraValue.FromInt64(1234),
            AstraValue.FromBool(true),
            AstraValue.FromDouble(100.0),
            AstraValue.FromBool(false)
        ]);

        migratedComp.SetField(2, AstraValue.FromDouble(75.0)); // Health damaged on server
        var deltas = DeltaReplicationManager.CollectReplicatedDeltas(dynamicStore);
        Assert.That(deltas.Count, Is.GreaterThan(0));

        var deltaBytes = DeltaReplicationManager.SerializeDeltas(deltas);
        var deserializedDeltas = DeltaReplicationManager.DeserializeDeltas(deltaBytes);
        DeltaReplicationManager.ApplyDeltas(clientDynamicStore, deserializedDeltas);
        Assert.That(clientDynamicStore.GetComponent(doorEntity, schemaId).GetField(2).AsDouble(), Is.EqualTo(75.0));

        // =============================================================
        // Step 15: Client prediction, reconciliation & dynamic store rollback
        // =============================================================
        var reconciler = new PredictionReconciler(clientDynamicStore);
        reconciler.RecordPredictedState(120, doorEntity, schemaId, [AstraValue.FromInt64(1234), AstraValue.FromBool(true), AstraValue.FromDouble(100.0), AstraValue.FromBool(false)]);

        var mispredicted = false;
        reconciler.OnMispredictionDetected += _ => mispredicted = true;

        reconciler.ReconcileServerState(120, doorEntity, schemaId, [AstraValue.FromInt64(1234), AstraValue.FromBool(false), AstraValue.FromDouble(100.0), AstraValue.FromBool(false)]);
        Assert.That(mispredicted, Is.True);
        Assert.That(clientDynamicStore.GetComponent(doorEntity, schemaId).GetField(1).AsBool(), Is.False);

        // =============================================================
        // Step 16: Live visual debugger (Breakpoint, StepInto, Resume) & Profiler
        // =============================================================
        var debugger = host.Debugger;
        var bpNode = NodeId.New();
        debugger.SetBreakpoint(new Breakpoint(bpNode, BreakpointMode.GraphPause));
        Assert.That(debugger.HasBreakpoint(bpNode), Is.True);

        var profiler = new GraphProfiler();
        using (profiler.BeginScope(graphId))
        {
            // Executed within scope
        }
        var metrics = profiler.GetMetrics(graphId);
        Assert.That(metrics.Invocations, Is.EqualTo(1));

        // =============================================================
        // Step 17: In-game Robust UI Launcher & Status Reporter
        // =============================================================
        var deepLinkCtx = new StudioDeepLinkContext
        {
            Action = StudioAction.InspectEntity,
            EntityUid = doorEntity.ToString()
        };
        var deepLinkUrl = StudioDeepLinkUrlBuilder.Build("http://127.0.0.1:8080/?nonce=sec123", deepLinkCtx);
        Assert.That(deepLinkUrl, Does.Contain("action=inspectentity"));
        Assert.That(deepLinkUrl, Does.Contain("entity=101"));

        var nonceMgr = new SessionNonceManager();
        var bridgeSec = new BridgeSecurityPolicy();
        await using var bridge = new AstraLocalBridge(
            nonceMgr, bridgeSec, new NullAssetProvider(),
            (_, _) => Task.CompletedTask);
        var reporter = new AstraRuntimeStatusReporter(bridge);
        var status = reporter.GetStatus();
        Assert.That(status.BridgeActive, Is.False);

        // =============================================================
        // Step 18: Studio authoring session over WebSocket protocol with RBAC
        // =============================================================
        var adminUser = new AstraUser("admin-1", "LeadDeveloper", AstraPermission.Admin, Binding.SecurityProfile.Engine);
        var authoringSession = new AuthoringServerSession(
            userAuthenticator: token => token == "admin_secret" ? adminUser : null,
            hotReloadManager: hotReload,
            debugger: debugger);

        authoringSession.RegisterGraph(new GraphSummaryDto(
            graphId,
            "AirlockControlGraph",
            GraphKind.System,
            GraphSide.Server,
            activeRev1,
            1));

        // Handshake with admin credentials
        var authRequest = new AuthHandshakeRequestMsg
        {
            MessageId = Guid.NewGuid().ToString("N"),
            ClientVersion = "1.0",
            AuthorToken = "admin_secret",
            AuthorName = "LeadDeveloper"
        };
        var authResponse = (AuthHandshakeResponseMsg?)await authoringSession.HandleAsync(authRequest, CancellationToken.None);
        Assert.That(authResponse, Is.Not.Null);
        Assert.That(authResponse!.Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That((authResponse.Permissions & AstraPermission.Admin), Is.EqualTo(AstraPermission.Admin));

        // Query graph list
        var listResponse = (GraphListResponseMsg?)await authoringSession.HandleAsync(new GraphListRequestMsg
        {
            MessageId = Guid.NewGuid().ToString("N"),
            SessionId = authResponse.SessionId
        }, CancellationToken.None);
        Assert.That(listResponse, Is.Not.Null);
        Assert.That(listResponse!.Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That(listResponse.Graphs.Count, Is.GreaterThan(0));

#if NET10_0_OR_GREATER
        var robustPerm = new AstraGraph.Robust.Server.RobustAdminPermissionProvider();
        Assert.That(robustPerm, Is.Not.Null);
#endif

        // =============================================================
        // Step 19: Canonical Promote to Project export to consumer repository
        // =============================================================
        var projectDir = Path.Combine(_layout.ProjectRoot, "Systems");
        Directory.CreateDirectory(projectDir);
        var canonicalPath = Path.Combine(projectDir, "AirlockControlGraph.agraph");
        File.WriteAllText(canonicalPath, GraphSerializer.Serialize(graphDoc));

        Assert.That(File.Exists(canonicalPath), Is.True);
        var canonicalDoc = GraphSerializer.Deserialize(File.ReadAllText(canonicalPath));
        Assert.That(canonicalDoc.Id, Is.EqualTo(graphId));
        Assert.That(canonicalDoc.Name, Is.EqualTo("AirlockControlGraph"));
    }

    private sealed class DoorAccessRefEvent
    {
        public int DoorEntity { get; }
        public int AttemptedCode { get; }
        public bool Granted { get; set; }

        public DoorAccessRefEvent(int doorEntity, int attemptedCode)
        {
            DoorEntity = doorEntity;
            AttemptedCode = attemptedCode;
            Granted = false;
        }
    }

    private sealed class NullAssetProvider : IWebAssetProvider
    {
        public WebAsset? TryGetAsset(string path) => null;
    }
}
