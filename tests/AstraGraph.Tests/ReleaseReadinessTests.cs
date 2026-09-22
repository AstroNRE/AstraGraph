using AstraGraph.Binding;
using AstraGraph.Core;
using AstraGraph.Editor.Bridge;
using AstraGraph.HotReload;
using AstraGraph.Persistence;
using AstraGraph.Runtime;
using AstraGraph.Runtime.Integration;
using AstraGraph.Runtime.Security;
using AstraGraph.VM;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class ReleaseReadinessTests
{
    private sealed class HandledEvent
    {
        public bool Handled { get; set; }
    }

    [Test]
    public void Publish_BindsNamedEvent_AndIsolatesHandlerFailure()
    {
        var host = new AstraGraphHost();
        var manager = new HotReloadManager(host);
        var graphId = GraphId.New();
        var doc = EntryDocument(graphId, "BoundEvent");

        var published = manager.Publish(doc, author: "dev", message: "bind");
        Assert.That(published.Success, Is.True);

        host.EventRouter.Subscribe(
            null,
            typeof(HandledEvent),
            graphId,
            "boom",
            (_, _) => throw new InvalidOperationException("graph failed"));

        Assert.That(host.EventRouter.GetSubscriptionCount(typeof(HandledEvent)), Is.EqualTo(1));
        Assert.DoesNotThrow(() => host.EventRouter.DispatchEvent(null, new HandledEvent()));
        Assert.That(host.EventRouter.LastDispatchError, Is.Not.Null);
    }

    [Test]
    public void Publish_IncompatibleSchema_KeepsRunningRevision()
    {
        var host = new AstraGraphHost();
        var manager = new HotReloadManager(host);
        var graphId = GraphId.New();
        var doc = EntryDocument(graphId, "SchemaGraph");
        var schemaId = SchemaId.New();
        var fieldId = FieldId.New();
        var v1 = new SchemaType(schemaId, "Comp", true, [new SchemaField(fieldId, "Data", PrimitiveType.Int32)]);
        var v2 = new SchemaType(schemaId, "Comp", true, [new SchemaField(fieldId, "Data", PrimitiveType.String)]);

        Assert.That(manager.Publish(doc, declaredSchema: v1).Success, Is.True);
        var revision = host.GetProgram(graphId)!.Revision;

        var failed = manager.Publish(doc, message: "bad migration", declaredSchema: v2);
        Assert.That(failed.Success, Is.False);
        Assert.That(host.GetProgram(graphId)!.Revision, Is.EqualTo(revision));
    }

    [Test]
    public void Publish_WritesLiveArchive()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-archive-" + Guid.NewGuid().ToString("N"));
        var layout = new StorageLayout(Path.Combine(root, "project"), Path.Combine(root, "data"));
        var host = new AstraGraphHost();
        var manager = new HotReloadManager(host, archive: new RevisionArchive(layout));
        var doc = EntryDocument(GraphId.New(), "DollToMothroach");

        Assert.That(manager.Publish(doc, author: "dev", message: "live").Success, Is.True);
        Assert.That(File.Exists(layout.GetLivePath("DollToMothroach.agraph")), Is.True);
        Assert.That(Directory.GetFiles(layout.HistoryDirectory, "*.revision.json"), Has.Length.EqualTo(1));
    }

    [Test]
    public void RefEvent_FieldWriteBack_UpdatesOriginalInstance()
    {
        var ev = new HandledEvent();
        Assert.That(GraphEventRouter.TryWriteHandled(ev, true), Is.True);
        Assert.That(ev.Handled, Is.True);
        Assert.That(new AstraGraphHost().EventRouter.DispatchEvent(null, ev), Is.True);
    }

    [Test]
    public void Scheduler_IsolatesFailingSystem()
    {
        var scheduler = new GraphScheduler();
        scheduler.RegisterSystem(new SystemRegistration(GraphId.New(), "Broken", [], [], 0, (_, _) => throw new InvalidOperationException("tick")));
        var ran = false;
        scheduler.RegisterSystem(new SystemRegistration(GraphId.New(), "Next", [], ["Broken"], 0, (_, _) => ran = true));

        Assert.DoesNotThrow(() => scheduler.Update(0, 1));
        Assert.That(scheduler.LastUpdateError, Is.Not.Null);
        Assert.That(ran, Is.True);
    }

    [Test]
    public void FixedPhaseHook_RecordsNativeOrderAsApproximate()
    {
        var hook = new FixedPhaseScheduleHook();
        var ran = false;
        hook.Register("DoorGraph", ["SharedDoorSystem"], [], (_, _) => ran = true);
        hook.Run(0.1, 3);
        Assert.That(ran, Is.True);
        Assert.That(hook.ApproximateOrderNotes, Has.Count.EqualTo(1));
        Assert.That(hook.ApproximateOrderNotes[0], Does.Contain("DynamicNativeSystemOrdering is false"));
    }

    [Test]
    public void PermissionPolicy_FailClosed_DeniesGate()
    {
        var policy = PolicyPermissionProvider.AllowLocalAuthor("astro");
        Assert.That(policy.CanEnterAstra("astro"), Is.True);
        Assert.That(policy.HasPermission("astro", AstraPermission.PublishServer), Is.True);
        policy.FailClosedNow();
        Assert.That(policy.CanEnterAstra("astro"), Is.False);
        Assert.That(policy.GetEffectivePermissions("astro"), Is.EqualTo(AstraPermission.None));
    }

    [Test]
    public void StudioAssets_ServeIndex_AndRejectTraversal()
    {
        var root = FindStudioRoot();
        var provider = new DirectoryWebAssetProvider(root);
        var index = provider.TryGetAsset("/index.html");
        Assert.That(index, Is.Not.Null);
        Assert.That(index!.ContentType, Does.Contain("text/html"));
        var html = System.Text.Encoding.UTF8.GetString(index.Content);
        Assert.That(html, Does.Contain("canvas"));
        Assert.That(html, Does.Contain("Publish"));
        var script = File.ReadAllText(Path.Combine(root, "studio.js"));
        Assert.That(script, Does.Contain("draft.publish.request"));
        Assert.That(script, Does.Not.Contain("fetch(\"/api/status\")"));
        Assert.That(provider.TryGetAsset("/../Compatibility.json"), Is.Null);
    }

    [Test]
    public void Facade_RemoveEntity_ClearsComponentsAndContinuations()
    {
        var host = new AstraGraphHost();
        var facade = new AstraGraphFacade(host, new HotReloadManager(host), new BindingCatalog(), PolicyPermissionProvider.AllowLocalAuthor("astro"));
        var schema = new SchemaType(SchemaId.New(), "Marker", true, [new SchemaField(FieldId.New(), "N", PrimitiveType.Int32)]);
        host.Components.AddComponent(new AstraEntityId(7), schema);
        var program = new BytecodeProgram(GraphId.New(), RevisionId.New(), "h", new ConstantPool());
        var func = new BytecodeFunction(0, 1, 0, [new BytecodeInstruction((byte)IrOpCode.Return, BytecodeInstruction.NoRegister, -1, 0, 0)]);
        host.Continuations.Schedule(new ContinuationFrame(program.Id, program, func, new AstraValue[1], Guid.NewGuid(), 0, ContinuationCondition.DelaySeconds(0, 10), new AstraEntityId(7)));

        facade.RemoveEntity(new AstraEntityId(7));

        Assert.That(host.Components.HasComponent(new AstraEntityId(7), schema.Id), Is.False);
        Assert.That(host.Continuations.ActiveCount, Is.EqualTo(0));
    }

    [Test]
    public void CacheKey_ChangesWhenEngineApiVersionChanges()
    {
        var left = new AstraGraph.Persistence.Cache.CompilationCacheKey("s", "c", "r", "b", "Server", EngineApiVersion: "1");
        var right = left with { EngineApiVersion = "2" };
        Assert.That(left.ComputeKeyString(), Is.Not.EqualTo(right.ComputeKeyString()));
    }

    private static GraphDocument EntryDocument(GraphId graphId, string name) => new()
    {
        Id = graphId,
        Name = name,
        Nodes =
        [
            new NodeDocument
            {
                Id = NodeId.New(),
                Name = "OnTick",
                NodeType = "Event.Tick",
                Pins = [new PinDocument { Id = PinId.New(), Name = "Out", Direction = PinDirection.Output, Kind = PinKind.Execution }]
            }
        ]
    };

    private static string FindStudioRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "AstraGraph.StudioWeb", "wwwroot");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("AstraGraph.StudioWeb/wwwroot was not found.");
    }
}
