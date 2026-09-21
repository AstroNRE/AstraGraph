using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using AstraGraph.Core;
using AstraGraph.Persistence;
using AstraGraph.Persistence.Audit;
using AstraGraph.Persistence.Cache;
using AstraGraph.Persistence.Discovery;
using AstraGraph.Persistence.State;
using AstraGraph.State;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class PersistenceTests
{
    private string _testDir = null!;
    private StorageLayout _layout = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "AstraGraph_PersistenceTests_" + Guid.NewGuid().ToString("N"));
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
    public void StorageLayout_CreatesAllDirectories_AndCleansTempFiles()
    {
        Assert.That(Directory.Exists(_layout.LiveDirectory), Is.True);
        Assert.That(Directory.Exists(_layout.OverridesDirectory), Is.True);
        Assert.That(Directory.Exists(_layout.HistoryDirectory), Is.True);
        Assert.That(Directory.Exists(_layout.CacheDirectory), Is.True);
        Assert.That(Directory.Exists(_layout.StateDirectory), Is.True);
        Assert.That(Directory.Exists(_layout.BackupsDirectory), Is.True);
        Assert.That(Directory.Exists(_layout.AuditDirectory), Is.True);

        // Create dummy orphan temp file
        var orphanPath = Path.Combine(_layout.LiveDirectory, "orphan.1234.tmp");
        File.WriteAllText(orphanPath, "abandoned write data");
        Assert.That(File.Exists(orphanPath), Is.True);

        var cleaned = _layout.CleanOrphanTempFiles();
        Assert.That(cleaned, Is.EqualTo(1));
        Assert.That(File.Exists(orphanPath), Is.False);
    }

    [Test]
    public void AtomicFileStore_WritesAtomically_CreatesBackup_AndFailsOnInvalidValidation()
    {
        var targetPath = Path.Combine(_layout.LiveDirectory, "test.txt");
        AtomicFileStore.WriteAllTextAtomic(targetPath, "Initial content", _layout.BackupsDirectory);
        Assert.That(File.ReadAllText(targetPath), Is.EqualTo("Initial content"));

        // Update file, should generate a backup
        AtomicFileStore.WriteAllTextAtomic(targetPath, "Updated content", _layout.BackupsDirectory);
        Assert.That(File.ReadAllText(targetPath), Is.EqualTo("Updated content"));

        var backupFiles = Directory.GetFiles(_layout.BackupsDirectory, "test.txt.*.bak");
        Assert.That(backupFiles.Length, Is.EqualTo(1));
        Assert.That(File.ReadAllText(backupFiles[0]), Is.EqualTo("Initial content"));

        // Failing validator should not overwrite target and should purge temp file
        Assert.Throws<InvalidOperationException>(() =>
        {
            AtomicFileStore.WriteAllTextAtomic(
                targetPath,
                "Corrupt content",
                _layout.BackupsDirectory,
                validator: _ => throw new InvalidOperationException("Validation failed"));
        });

        Assert.That(File.ReadAllText(targetPath), Is.EqualTo("Updated content"));
        var tempFiles = Directory.GetFiles(_layout.LiveDirectory, "*.tmp");
        Assert.That(tempFiles, Is.Empty);
    }

    [Test]
    public void CompilationCache_StoresAndRetrieves_WithIntegrityCheck()
    {
        var cache = new CompilationCache(_layout);
        var key = new CompilationCacheKey(
            SemanticHash: "abc123semantic",
            CompilerVersion: "1.0.0",
            RuntimeVersion: "1.0.0",
            BindingCatalogHash: "xyz789catalog",
            TargetSide: "Server");

        var payload = Encoding.UTF8.GetBytes("compiled_bytecode_data_bytes");
        cache.Store(key, payload);

        var hit = cache.TryGet(key, out var retrieved);
        Assert.That(hit, Is.True);
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved, Is.EqualTo(payload));

        // Different key should miss
        var differentKey = key with { SemanticHash = "different_hash" };
        Assert.That(cache.TryGet(differentKey, out _), Is.False);

        // Evict
        Assert.That(cache.Evict(key), Is.True);
        Assert.That(cache.TryGet(key, out _), Is.False);
    }

    [Test]
    public void AuditLogger_AppendsAndReadsStructuredRecords()
    {
        var logger = new AuditLogger(_layout);
        var graphId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var rec1 = new AuditRecord(
            now.AddMinutes(-5),
            "SaveDraft",
            graphId,
            null,
            "Admin1",
            "Saved draft",
            "source_hash_1",
            true);

        var rec2 = new AuditRecord(
            now,
            "PublishRevision",
            graphId,
            Guid.NewGuid(),
            "Admin2",
            "Published revision 2",
            "source_hash_2",
            true);

        logger.Append(rec1);
        logger.Append(rec2);

        var queried = logger.ReadRecords(now.AddHours(-1), now.AddHours(1));
        Assert.That(queried.Count, Is.EqualTo(2));
        Assert.That(queried[0].Action, Is.EqualTo("SaveDraft"));
        Assert.That(queried[1].Action, Is.EqualTo("PublishRevision"));
    }

    [Test]
    public void BootstrapLoader_AppliesPrecedence_AndPromotesLiveGraph()
    {
        var loader = new BootstrapLoader(_layout);

        // 1. Create a project resource
        var projectGraphId = GraphId.New();
        var projectDoc = new GraphDocument
        {
            Id = projectGraphId,
            Name = "BaseProjectMechanic",
            Kind = GraphKind.System,
            Side = GraphSide.Server
        };
        var projectCategoryDir = _layout.GetProjectCategoryPath(GraphCategory.Mechanics);
        Directory.CreateDirectory(projectCategoryDir);
        File.WriteAllText(
            Path.Combine(projectCategoryDir, "BaseProjectMechanic.agraph"),
            GraphSerializer.Serialize(projectDoc));

        // 2. Create a live graph
        var liveGraphId = GraphId.New();
        var liveDoc = new GraphDocument
        {
            Id = liveGraphId,
            Name = "LiveAddition",
            Kind = GraphKind.System,
            Side = GraphSide.Server
        };
        loader.SaveLive(liveDoc, "LiveAddition.agraph", author: "LiveTester");

        // 3. Create an override for the project graph
        var overrideDoc = new GraphDocument
        {
            Id = projectGraphId, // Same ID!
            Name = "BaseProjectMechanic_Overridden",
            Kind = GraphKind.System,
            Side = GraphSide.Server
        };
        loader.SaveOverride(overrideDoc, "BaseProjectMechanic.agraph", baseRelativePath: "Mechanics/BaseProjectMechanic.agraph");

        // Discover all
        var discovered = loader.DiscoverAll();
        Assert.That(discovered.Count, Is.EqualTo(2));

        // Project graph should be superseded by the override
        var resolvedProject = discovered.First(g => g.Id == projectGraphId);
        Assert.That(resolvedProject.Origin, Is.EqualTo(GraphOrigin.Override));
        Assert.That(resolvedProject.Document.Name, Is.EqualTo("BaseProjectMechanic_Overridden"));

        // Live graph should be discovered as Live
        var resolvedLive = discovered.First(g => g.Id == liveGraphId);
        Assert.That(resolvedLive.Origin, Is.EqualTo(GraphOrigin.Live));

        // Promote live graph to project
        var promoted = loader.PromoteToProject(resolvedLive, GraphCategory.Mechanics, "LiveAddition.agraph", removeSourceFromLive: true);
        Assert.That(promoted.Origin, Is.EqualTo(GraphOrigin.ProjectResource));
        Assert.That(File.Exists(resolvedLive.FullPath), Is.False);
        Assert.That(File.Exists(promoted.FullPath), Is.True);
    }

    [Test]
    public void PersistentStateStore_SavesAndRestoresRuntimeStateWithCrc32()
    {
        var stateStore = new AstraStateStore();
        var graphId = GraphId.New();
        var sym1 = SymbolId.New();
        var sym2 = SymbolId.New();
        var sym3 = SymbolId.New();
        var sym4 = SymbolId.New();

        stateStore.SetVariable(graphId, sym1, "Health", AstraValue.FromDouble(100.5), isPersistent: true);
        stateStore.SetVariable(graphId, sym2, "PlayerEntity", AstraValue.FromEntityUid(42), isPersistent: true);
        stateStore.SetVariable(graphId, sym3, "Position", AstraValue.FromVector2(new Vector2(10.5f, 20.25f)), isPersistent: true);
        stateStore.SetVariable(graphId, sym4, "SessionName", AstraValue.FromObject("Round42"), isPersistent: true);
        // Non-persistent variable
        stateStore.SetVariable(graphId, SymbolId.New(), "TempTicks", AstraValue.FromInt64(999), isPersistent: false);

        var persistentStore = new PersistentStateStore(_layout);
        persistentStore.SaveState("RoundSnapshot", stateStore);

        var snapshots = persistentStore.ListSnapshots();
        Assert.That(snapshots, Does.Contain("RoundSnapshot"));

        // Restore into fresh state store
        var freshStateStore = new AstraStateStore();
        var restored = persistentStore.RestoreState("RoundSnapshot", freshStateStore);
        Assert.That(restored, Is.True);

        Assert.That(freshStateStore.GetVariable(graphId, sym1, "Health").AsDouble(), Is.EqualTo(100.5));
        Assert.That(freshStateStore.GetVariable(graphId, sym2, "PlayerEntity").AsEntityUid(), Is.EqualTo(42));
        Assert.That(freshStateStore.GetVariable(graphId, sym3, "Position").AsVector2(), Is.EqualTo(new Vector2(10.5f, 20.25f)));
        Assert.That((string)freshStateStore.GetVariable(graphId, sym4, "SessionName").AsObject()!, Is.EqualTo("Round42"));
        Assert.That(freshStateStore.GetVariable(graphId, SymbolId.Empty, "TempTicks"), Is.EqualTo(AstraValue.Null));

        // Delete state
        Assert.That(persistentStore.DeleteState("RoundSnapshot"), Is.True);
        Assert.That(persistentStore.ListSnapshots(), Does.Not.Contain("RoundSnapshot"));
    }
}
