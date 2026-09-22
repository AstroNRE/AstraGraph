using AstraGraph.Core;
using AstraGraph.HotReload;
using AstraGraph.Persistence;
using AstraGraph.Persistence.Discovery;
using AstraGraph.Persistence.State;
using AstraGraph.Runtime;
using AstraGraph.State;
using NUnit.Framework;

#if NET10_0_OR_GREATER
using AstraGraph.Robust.Server;
#endif

namespace AstraGraph.Tests;

[TestFixture]
public sealed class TransactionalHotReloadTests
{
    private string _tempDir = default!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AstraGraph_BootstrapTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
            }
        }
    }

    [Test]
    public void QueuePublish_ExecutesStrictlyAtSimulationTickBoundary()
    {
        var host = new AstraGraphHost();
        var hotReload = new HotReloadManager(host);

        var graphId = GraphId.New();
        var draft = new GraphDocument
        {
            Id = graphId,
            Name = "TickSystem",
            Kind = GraphKind.System,
            Side = GraphSide.Server,
            Nodes = [],
            Connections = []
        };

        // 1. Queue publication off-tick
        var tx = hotReload.QueuePublish(draft, author: "Dev", message: "Deferred publish");

        // Verify graph is NOT active immediately
        Assert.That(host.GetProgram(graphId), Is.Null, "Program should not be registered before tick boundary!");
        Assert.That(tx.Status, Is.EqualTo(PublishTransactionStatus.Queued));

        // 2. Simulation reaches tick boundary
        hotReload.ProcessPendingTransactions(currentTick: 1, currentTime: 0.016);

        // Verify program is now active
        Assert.That(host.GetProgram(graphId), Is.Not.Null, "Program should be registered after tick processing!");
        Assert.That(tx.Status, Is.EqualTo(PublishTransactionStatus.Committed));
    }

    [Test]
    public void Rollback_RevertsCodeStateAndSchemaAtomically()
    {
        var componentStore = new DynamicComponentStore();
        var host = new AstraGraphHost(components: componentStore);
        var hotReload = new HotReloadManager(host);

        var graphId = GraphId.New();
        var schemaId = SchemaId.New();
        var countFieldId = FieldId.New();
        var bonusFieldId = FieldId.New();

        // 1. Schema V1 (Count)
        var countField = new SchemaField(countFieldId, "Count", PrimitiveType.Int64);
        var schemaV1 = new SchemaType(schemaId, "PlayerStats", true, [countField]);
        hotReload.RegisterActiveSchema(schemaV1);

        var draftV1 = new GraphDocument
        {
            Id = graphId,
            Name = "StatsGraph",
            Kind = GraphKind.System,
            Side = GraphSide.Server,
            Nodes = [],
            Connections = []
        };

        var pubV1 = hotReload.Publish(draftV1, author: "Dev", message: "V1", declaredSchema: schemaV1);
        Assert.That(pubV1.Success, Is.True);
        var rev1 = pubV1.PublishedRevisionId!.Value;

        // Add instance for entity 42
        var storage = componentStore.AddComponent(42, schemaV1, [AstraValue.FromInt64(100L)]);
        Assert.That(storage.GetField(countFieldId).AsInt64(), Is.EqualTo(100L));

        // 2. Schema V2 (Count + Bonus)
        var bonusField = new SchemaField(bonusFieldId, "Bonus", PrimitiveType.Int64, DefaultValue: "25");
        var schemaV2 = new SchemaType(schemaId, "PlayerStats", true, [countField, bonusField]);

        var draftV2 = new GraphDocument
        {
            Id = graphId,
            Name = "StatsGraph",
            Kind = GraphKind.System,
            Side = GraphSide.Server,
            Nodes = [],
            Connections = []
        };

        var pubV2 = hotReload.Publish(draftV2, author: "Dev", message: "V2", declaredSchema: schemaV2);
        Assert.That(pubV2.Success, Is.True);

        // Verify entity has V2 schema with bonus default
        var storageV2 = componentStore.GetComponent(42, schemaId);
        Assert.That(storageV2.GetField(countFieldId).AsInt64(), Is.EqualTo(100L));
        Assert.That(storageV2.GetField(bonusFieldId).AsInt64(), Is.EqualTo(25L));

        // 3. Rollback to V1
        var rollbackResult = hotReload.Rollback(graphId, rev1);
        Assert.That(rollbackResult.Success, Is.True);

        // Verify schema reverted to V1
        var activeSchema = hotReload.GetActiveSchema(schemaId);
        Assert.That(activeSchema, Is.Not.Null);
        Assert.That(activeSchema!.FieldCount, Is.EqualTo(1));
        Assert.That(activeSchema.Fields[0].Name, Is.EqualTo("Count"));

        // Verify entity storage was migrated back in-place
        var rolledBackStorage = componentStore.GetComponent(42, schemaId);
        Assert.That(rolledBackStorage.FieldCount, Is.EqualTo(1));
        Assert.That(rolledBackStorage.GetField(countFieldId).AsInt64(), Is.EqualTo(100L));
    }

#if NET10_0_OR_GREATER
    [Test]
    public void ServerRestart_BootstrapPipeline_RecoversGraphsAndPersistentStateAutomatically()
    {
        var projectRoot = Path.Combine(_tempDir, "Resources", "AstraGraph");
        var dataRoot = Path.Combine(_tempDir, "data", "AstraGraph");
        var layout = new StorageLayout(projectRoot, dataRoot);
        layout.EnsureDirectories();

        var bootstrapLoader = new BootstrapLoader(layout);
        var stateStore = new PersistentStateStore(layout);

        var graphId = GraphId.New();
        var symbolId = SymbolId.New();

        // 1. Initial server run: save live graph and persistent state
        var draft = new GraphDocument
        {
            Id = graphId,
            Name = "PersistentServerSystem",
            Kind = GraphKind.System,
            Side = GraphSide.Server,
            Nodes = [],
            Connections = []
        };

        bootstrapLoader.SaveLive(draft, "Systems/PersistentServerSystem.agraph", author: "Admin");

        var memoryState = new AstraStateStore();
        memoryState.SetVariable(graphId, symbolId, "GlobalServerScore", AstraValue.FromInt64(9999L), isPersistent: true);
        stateStore.SaveState("default", memoryState);

        // 2. SIMULATE COMPLETE SERVER CRASH & RESTART:
        // Wipe memory stores completely
        memoryState.ClearAll();

        // Create a new server system with a clean host
        var newServerSystem = new ServerAstraGraphSystem(layout);
        newServerSystem.ExecuteBootstrap();

        // 3. Verify graph is automatically loaded and active in the new host
        var restoredProgram = newServerSystem.Host.GetProgram(graphId);
        Assert.That(restoredProgram, Is.Not.Null, "Graph should be automatically bootstrapped on startup without manual publish!");
        Assert.That(restoredProgram!.Id, Is.EqualTo(graphId));

        // 4. Verify persistent state is automatically restored
        var scoreVal = newServerSystem.Host.State.GetVariable(graphId, symbolId, "GlobalServerScore");
        Assert.That(scoreVal.AsInt64(), Is.EqualTo(9999L), "Persistent server state must survive restart!");
    }
#endif
}
