using AstraGraph.Binding;
using AstraGraph.Core;
using AstraGraph.HotReload;
using AstraGraph.Persistence;
using AstraGraph.Runtime;
using AstraGraph.Runtime.Network;
using AstraGraph.State;
using AstraGraph.UI.Runtime;
using AstraGraph.VM;
using NUnit.Framework;

#if NET10_0_OR_GREATER
using AstraGraph.Robust.Client;
using AstraGraph.Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Timing;
using Robust.UnitTesting.Server;
#endif

namespace AstraGraph.Tests;

[TestFixture]
public sealed class ProductionIntegrationTests
{
    public static int Add(int left, int right) => left + right;

    [Test]
    public void BindingCatalog_IndexesRealMethod_AndVmInvokesIt()
    {
        var catalog = new BindingCatalog();
        var method = typeof(ProductionIntegrationTests).GetMethod(nameof(Add))!;
        var registered = catalog.RegisterMethod(method, "Probe.Add(int, int)");
        var resolved = catalog.FindMethod("Probe.Add(int, int)");

        Assert.That(resolved, Is.SameAs(registered));

        var pool = new ConstantPool();
        var name = pool.GetOrAddString("Probe.Add(int, int)");
        var left = pool.GetOrAddInt64(2);
        var right = pool.GetOrAddInt64(5);
        var instructions = new List<BytecodeInstruction>
        {
            new((byte)IrOpCode.LoadConst, 0, left, 0, 0),
            new((byte)IrOpCode.LoadConst, 1, right, 0, 0),
            new((byte)IrOpCode.CallNative, 2, name, 2, 0),
            new((byte)IrOpCode.Return, BytecodeInstruction.NoRegister, 2, 0, 0)
        };
        var function = new BytecodeFunction(pool.GetOrAddString("Main"), 3, 0, instructions);
        var program = new BytecodeProgram(GraphId.New(), RevisionId.New(), "hash", pool);
        program.EntryPoints.Add(function);

        var result = new AstraVm().Execute(program, function, hostServices: new CatalogHost(catalog));
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.ReturnValue.AsInt64(), Is.EqualTo(7));
    }

    [Test]
    public void NetworkTransport_DeliversManifestPackageAndReady()
    {
        var serverHost = new AstraGraphHost();
        var clientHost = new AstraGraphHost();
        var server = new AstraNetworkSyncService(serverHost);
        var client = new AstraNetworkSyncService(clientHost);
        var graphId = GraphId.New();
        Assert.That(new HotReloadManager(serverHost).Publish(Entry(graphId)).Success, Is.True);
        serverHost.Scheduler.RegisterSystem(new SystemRegistration(graphId, "Synced", [], []));

        var serverLink = new LoopbackAstraTransport();
        var clientLink = new LoopbackAstraTransport { Peer = serverLink };
        serverLink.Peer = clientLink;

        RequestMissingPackagesMessage? missing = null;
        clientLink.Received += payload =>
        {
            switch (AstraSyncFrames.Read(payload))
            {
                case JoinHandshakeMessage handshake:
                    missing = client.ProcessHandshake(handshake);
                    break;
                case GraphPackageMessage package:
                    client.ApplyGraphPackage(package);
                    break;
            }
        };

        serverLink.Send(AstraSyncFrames.Handshake(server.BuildHandshakeMessage(4)));
        Assert.That(missing, Is.Not.Null);
        Assert.That(missing!.MissingGraphIds, Does.Contain(graphId));

        serverLink.Send(AstraSyncFrames.Package(server.ExportGraphPackage(graphId)!));
        Assert.That(clientHost.GetProgram(graphId), Is.Not.Null);

        var ready = AstraNetworkSyncService.CreateReadyMessage(1, 4);
        Assert.That(ready.CurrentClientTick, Is.EqualTo(4));
    }

    [Test]
    public void Prediction_MismatchRollsBack_ThenReplaysNextTick()
    {
        var schema = new SchemaType(SchemaId.New(), "Hp", true, [new SchemaField(FieldId.New(), "Value", PrimitiveType.Int32)]);
        var store = new DynamicComponentStore();
        store.AddComponent(3, schema, [AstraValue.FromInt64(0)]);
        var reconciler = new PredictionReconciler(store);
        reconciler.RecordPredictedState(10, 3, schema.Id, [AstraValue.FromInt64(5)]);
        reconciler.RecordPredictedState(11, 3, schema.Id, [AstraValue.FromInt64(6)]);

        var result = reconciler.ReconcileServerState(10, 3, schema.Id, [AstraValue.FromInt64(9)]);
        Assert.That(result.Mispredicted, Is.True);
        Assert.That(store.GetComponent(3, schema.Id).GetField(0).AsInt64(), Is.EqualTo(9));

        reconciler.Replay(3, schema.Id, _ => [AstraValue.FromInt64(10)]);
        Assert.That(store.GetComponent(3, schema.Id).GetField(0).AsInt64(), Is.EqualTo(10));
    }

    [Test]
    public void BuiContract_ServerRevisionWins()
    {
        var state = new UiStateManager();
        var bridge = new AstraBuiBridge(state, _ => { });
        var contract = new AstraBuiContract(
            new Dictionary<string, object?> { ["hp"] = 30 },
            ["Fire"],
            ["HpChanged"],
            2,
            "hash");

        Assert.That(bridge.ApplyAuthoritative(contract), Is.True);
        Assert.That(state.GetVariable("hp"), Is.EqualTo(30));
        Assert.That(bridge.ApplyAuthoritative(contract with { Revision = 1, State = new Dictionary<string, object?> { ["hp"] = 1 } }), Is.False);
        Assert.That(state.GetVariable("hp"), Is.EqualTo(30));
    }

#if NET10_0_OR_GREATER
    [Test]
    public void PlayableLifecycle_PublishEditRollbackAndRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-life-" + Guid.NewGuid().ToString("N"));
        var layout = new StorageLayout(Path.Combine(root, "Resources", "AstraGraph"), Path.Combine(root, "data", "AstraGraph"));
        var graphId = GraphId.New();
        var symbol = SymbolId.New();
        var document = Entry(graphId);

        var first = new AstraGraph.Robust.Server.ServerAstraGraphSystem(layout);
        first.ExecuteBootstrap();
        var published = first.HotReloadManager.Publish(document, author: "dev", message: "initial");
        Assert.That(published.Success, Is.True);
        first.Host.State.SetVariable(graphId, symbol, "Score", AstraValue.FromInt64(4), isPersistent: true);
        first.SavePersistentState();

        var restarted = new AstraGraph.Robust.Server.ServerAstraGraphSystem(layout);
        restarted.ExecuteBootstrap();
        var restored = restarted.Host.GetProgram(graphId);
        Assert.That(restored, Is.Not.Null);
        Assert.That(restarted.Host.State.GetVariable(graphId, symbol, "Score").AsInt64(), Is.EqualTo(4));

        var edited = restarted.HotReloadManager.Publish(document, author: "dev", message: "hot edit");
        Assert.That(edited.PublishedRevisionId, Is.Not.EqualTo(restored!.Revision));
        Assert.That(restarted.HotReloadManager.Rollback(graphId).Success, Is.True);
        Assert.That(restarted.Host.GetProgram(graphId)!.Revision, Is.EqualTo(restored.Revision));
    }

    [Test]
    public void RobustNetManager_CarriesAstraSyncFrames()
    {
        var simulation = RobustServerSimulation.NewSimulation().InitializeInstance();
        var transport = new RobustNetManagerTransport(simulation.Resolve<INetManager>());
        var handshake = AstraSyncFrames.Handshake(new AstraNetworkSyncService(new AstraGraphHost()).BuildHandshakeMessage(1));
        Assert.DoesNotThrow(() => transport.Send(handshake));
        Assert.That(simulation.Resolve<INetManager>().IsServer, Is.True);
    }

    [Test]
    public void RobustPrediction_UsesEngineTick_ThenRollsBackAndReplays()
    {
        var simulation = RobustServerSimulation.NewSimulation().InitializeInstance();
        var timing = simulation.Resolve<IGameTiming>();
        var schema = new SchemaType(SchemaId.New(), "Hp", true, [new SchemaField(FieldId.New(), "Value", PrimitiveType.Int32)]);
        var store = new DynamicComponentStore();
        store.AddComponent(8, schema, [AstraValue.FromInt64(0)]);
        var adapter = new RobustPredictionAdapter(timing, new PredictionReconciler(store));
        var tick = Math.Max(adapter.CurrentTick, 1);

        adapter.PredictAt(tick, 8, schema.Id, [AstraValue.FromInt64(5)]);
        adapter.PredictAt(tick + 1, 8, schema.Id, [AstraValue.FromInt64(6)]);
        var result = adapter.ApplyAuthoritative(tick, 8, schema.Id, [AstraValue.FromInt64(9)]);

        Assert.That(result.Mispredicted, Is.True);
        Assert.That(store.GetComponent(8, schema.Id).GetField(0).AsInt64(), Is.EqualTo(9));
        adapter.Replay(8, schema.Id, _ => [AstraValue.FromInt64(10)]);
        Assert.That(store.GetComponent(8, schema.Id).GetField(0).AsInt64(), Is.EqualTo(10));
        Assert.That(adapter.CurrentTick, Is.EqualTo((int)timing.CurTick.Value));
    }

    [Test]
    public void RobustBui_AppliesAuthoritativeStateAndClientMessage()
    {
        var state = new UiStateManager();
        var bridge = new AstraBuiBridge(state, _ => { });
        var engineState = new AstraBuiState
        {
            Revision = 2,
            ContractHash = "hash",
            Values = new Dictionary<string, string> { ["hp"] = "30" },
            ClientActions = ["Fire"],
            ServerNotifications = ["HpChanged"]
        };

        Assert.That(engineState, Is.InstanceOf<BoundUserInterfaceState>());
        Assert.That(AstraBuiRobustAdapter.Apply(bridge, engineState), Is.True);
        Assert.That(state.GetVariable("hp"), Is.EqualTo("30"));
        engineState.Revision = 1;
        Assert.That(AstraBuiRobustAdapter.Apply(bridge, engineState), Is.False);

        var message = new AstraBuiUiMessage { Action = "Fire", Payload = "1" };
        Assert.That(message, Is.InstanceOf<BoundUserInterfaceMessage>());
        Assert.That(AstraBuiRobustAdapter.ToBuiMessage(message).Action, Is.EqualTo("Fire"));
    }

    [Test]
    public void PublishedGraph_RunsEntryPointThroughRobustEventBus()
    {
        var host = new AstraGraphHost();
        var graphId = GraphId.New();
        var document = Entry(graphId, "OnDamage", typeof(PublishedProbeEvent).AssemblyQualifiedName!);
        Assert.That(new HotReloadManager(host).Publish(document).Success, Is.True);
        Assert.That(host.EventRouter.GetSubscriptionCount(typeof(PublishedProbeEvent)), Is.EqualTo(1));

        PublishedEventSystem.Router = host.EventRouter;
        var simulation = RobustServerSimulation.NewSimulation()
            .RegisterEntitySystems(factory => factory.LoadExtraSystemType<PublishedEventSystem>())
            .InitializeInstance();
        var ev = new PublishedProbeEvent { Damage = 10 };
        simulation.Resolve<IEntityManager>().EventBus.RaiseEvent(EventSource.Local, ref ev);

        Assert.That(host.ExecutedEntryPoints, Is.EqualTo(1));
        Assert.That(ev.Damage, Is.EqualTo(10));
    }

    [ByRefEvent]
    public struct PublishedProbeEvent
    {
        public int Damage { get; set; }
    }

    public sealed class PublishedEventSystem : EntitySystem
    {
        public static GraphEventRouter Router { get; set; } = null!;

        public override void Initialize()
        {
            base.Initialize();
            new RobustEventBusSubscriptionAdapter(Router)
                .RegisterBroadcastSubscription<PublishedProbeEvent>(this, GraphId.New(), "OnDamage");
        }
    }

    [Test]
    public void MixedQuery_UsesRealNativeComponentAndAstraSchema()
    {
        var simulation = RobustServerSimulation.NewSimulation().InitializeInstance();
        var entities = simulation.Resolve<EntityManager>();
        var uid = entities.Spawn();
        var schema = new SchemaType(SchemaId.New(), "Marker", true, [new SchemaField(FieldId.New(), "N", PrimitiveType.Int32)]);
        var store = new DynamicComponentStore();
        store.AddComponent(new AstraEntityId((int)uid), schema);
        var engine = new MixedQueryEngine(store, new RobustEcsQueryBridge(entities));

        var results = engine.Execute(new QueryDescriptor([schema.Id], [], [typeof(MetaDataComponent)], []));
        Assert.That(results.Select(result => result.EntityUid.Value), Does.Contain((int)uid));
    }
#endif

    private static GraphDocument Entry(GraphId graphId, string entryName = "OnTick", string? eventType = null) => new()
    {
        Id = graphId,
        Name = "Lifecycle",
        Kind = GraphKind.System,
        Nodes =
        [
            new NodeDocument
            {
                Id = NodeId.New(),
                Name = entryName,
                NodeType = "Event.Tick",
                Properties = eventType == null ? [] : new Dictionary<string, string> { ["eventType"] = eventType },
                Pins = [new PinDocument { Id = PinId.New(), Name = "Out", Direction = PinDirection.Output, Kind = PinKind.Execution }]
            }
        ]
    };

    private sealed class CatalogHost : IVmHostServices
    {
        private readonly BindingCatalog _catalog;

        public CatalogHost(BindingCatalog catalog) => _catalog = catalog;

        public AstraValue GetVariable(SymbolId variableId, string name) => AstraValue.Null;

        public void SetVariable(SymbolId variableId, string name, AstraValue value)
        {
        }

        public AstraValue CallNative(string methodDescriptor, IReadOnlyList<AstraValue> arguments)
        {
            var method = _catalog.FindMethod(methodDescriptor) ?? throw new MissingMethodException(methodDescriptor);
            return method.Invoker(arguments.ToArray());
        }

        public AstraValue GetComponent(AstraEntityId entityUid, string componentTypeName) => AstraValue.Null;

        public bool HasComponent(AstraEntityId entityUid, string componentTypeName) => false;

        public void SetComponentField(AstraEntityId entityUid, string schemaIdAndFieldId, AstraValue value)
        {
        }
    }
}
