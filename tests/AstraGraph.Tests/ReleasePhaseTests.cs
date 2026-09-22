using AstraGraph.Binding;
using AstraGraph.Core;
using AstraGraph.Editor.Protocol;
using AstraGraph.HotReload;
using AstraGraph.Persistence;
using AstraGraph.Persistence.Discovery;
using AstraGraph.Runtime;
using AstraGraph.Runtime.Integration;
using AstraGraph.Runtime.Security;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class ReleasePhaseTests
{
    [Test]
    public void SharedPredicted_PublishRejectsUnlistedNativeCall()
    {
        var host = new AstraGraphHost();
        var manager = new HotReloadManager(host);
        var entryId = NodeId.New();
        var callId = NodeId.New();
        var from = PinId.New();
        var to = PinId.New();
        var document = new GraphDocument
        {
            Id = GraphId.New(),
            Name = "PredictedDelete",
            Side = GraphSide.SharedPredicted,
            Nodes =
            [
                new NodeDocument
                {
                    Id = entryId,
                    Name = "OnTick",
                    NodeType = "Event.Tick",
                    Pins = [new PinDocument { Id = from, Name = "Out", Direction = PinDirection.Output, Kind = PinKind.Execution }]
                },
                new NodeDocument
                {
                    Id = callId,
                    Name = "Delete",
                    NodeType = "Native.Call",
                    Properties = new Dictionary<string, string> { ["Method"] = "Entity.Delete" },
                    Pins = [new PinDocument { Id = to, Name = "In", Direction = PinDirection.Input, Kind = PinKind.Execution }]
                }
            ],
            Connections = [new ConnectionDocument { FromNode = entryId, FromPin = from, ToNode = callId, ToPin = to }]
        };

        var result = manager.Publish(document, author: "dev", message: "unsafe");
        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics.Any(error => error.Code == "PRED001"), Is.True);
        Assert.That(host.GetProgram(document.Id), Is.Null);
    }

    [Test]
    public void Rollback_RestoresPreviousDocumentFromDiskWhenMemoryIsEmpty()
    {
        var root = Path.Combine(Path.GetTempPath(), "agraph-disk-" + Guid.NewGuid().ToString("N"));
        var layout = new StorageLayout(Path.Combine(root, "project"), Path.Combine(root, "data"));
        var archive = new RevisionArchive(layout);
        var graphId = GraphId.New();
        var first = Entry(graphId, "First");
        var second = Entry(graphId, "Second");

        var original = new HotReloadManager(new AstraGraphHost(), archive: archive);
        Assert.That(original.Publish(first, author: "dev", message: "v1").Success, Is.True);
        Assert.That(original.Publish(second, author: "dev", message: "v2").Success, Is.True);

        var previous = archive.TryLoadPrevious(graphId);
        Assert.That(previous, Is.Not.Null);
        Assert.That(previous!.Nodes[0].Name, Is.EqualTo("First"));

        var restarted = new HotReloadManager(new AstraGraphHost(), archive: archive);
        var rolled = restarted.Rollback(graphId);
        Assert.That(rolled.Success, Is.True);
        Assert.That(restarted.Host.GetProgram(graphId), Is.Not.Null);
    }

    [Test]
    public void DollToMothroach_SampleFile_DeserializesAsSystemGraph()
    {
        var path = FindSample();
        var document = GraphSerializer.Deserialize(File.ReadAllText(path));
        Assert.That(document.Name, Is.EqualTo("DollToMothroach"));
        Assert.That(document.Kind, Is.EqualTo(GraphKind.System));
        Assert.That(document.Nodes.Any(node => node.NodeType == "Event.Interact"), Is.True);
    }

    [Test]
    public async Task StudioProtocol_SavesFetchesDraft_AndPushesDeadmin()
    {
        var user = new AstraUser("dev", "dev", AstraPermission.EditDrafts | AstraPermission.Compile | AstraPermission.Debug, SecurityProfile.Gameplay);
        var session = new AuthoringServerSession(_ => user);
        SessionUpdatedMsg? pushed = null;
        session.SubscribeOutbound(message => pushed = message as SessionUpdatedMsg);

        var handshake = session.HandleHandshake(new AuthHandshakeRequest("1.0.0", "token", "dev"));
        var graphId = GraphId.New();
        var saved = await session.HandleAsync(new DraftSaveRequestMsg
        {
            SessionId = handshake.SessionId,
            GraphId = graphId,
            DraftJson = "{\"name\":\"Saved\"}",
            AuthorMessage = "save"
        }, CancellationToken.None);

        var fetched = await session.HandleAsync(new GraphFetchRequestMsg
        {
            SessionId = handshake.SessionId,
            GraphId = graphId
        }, CancellationToken.None);

        Assert.That(saved, Is.InstanceOf<DraftSaveResponseMsg>());
        Assert.That(((DraftSaveResponseMsg)saved!).Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That(((GraphFetchResponseMsg)fetched!).DraftJson, Does.Contain("Saved"));

        session.ReplaceUser("dev", null);
        Assert.That(pushed, Is.Not.Null);
        Assert.That(pushed!.Permissions, Is.EqualTo(AstraPermission.None));
    }

    [Test]
    public void GraphFault_RecordsIsolatedDispatchFailure()
    {
        var host = new AstraGraphHost();
        var graphId = GraphId.New();
        host.EventRouter.Subscribe(null, typeof(string), graphId, "boom", (_, _) => throw new InvalidOperationException("graph failed"));
        host.EventRouter.DispatchEvent(null, "tick");
        Assert.That(host.Faults.Latest, Is.Not.Null);
        Assert.That(host.Faults.Latest!.GraphId, Is.EqualTo(graphId));
        Assert.That(host.Faults.Latest.Count, Is.EqualTo(1));
    }

    [Test]
    public void Bootstrap_RequiredGraphFailure_Aborts_OptionalFailure_Continues()
    {
        var root = Path.Combine(Path.GetTempPath(), "agraph-boot-" + Guid.NewGuid().ToString("N"));
        var layout = new StorageLayout(Path.Combine(root, "project"), Path.Combine(root, "data"));
        var loader = new BootstrapLoader(layout);
        var required = PredictedDelete(required: true);
        loader.SaveLive(required, "required.agraph");
        var host = new AstraGraphHost();
        var service = new AstraBootstrapService(host, new HotReloadManager(host), loader);

        Assert.Throws<InvalidOperationException>(() => service.Activate());

        var optionalRoot = Path.Combine(Path.GetTempPath(), "agraph-boot-" + Guid.NewGuid().ToString("N"));
        var optionalLayout = new StorageLayout(Path.Combine(optionalRoot, "project"), Path.Combine(optionalRoot, "data"));
        var optionalLoader = new BootstrapLoader(optionalLayout);
        optionalLoader.SaveLive(PredictedDelete(required: false), "optional.agraph");
        var optionalHost = new AstraGraphHost();
        var optional = new AstraBootstrapService(optionalHost, new HotReloadManager(optionalHost), optionalLoader);
        Assert.That(optional.Activate(), Is.EqualTo(0));
        Assert.That(optional.LastWarning, Is.Not.Null);
    }

    private static GraphDocument PredictedDelete(bool required)
    {
        var entryId = NodeId.New();
        var callId = NodeId.New();
        var from = PinId.New();
        var to = PinId.New();
        return new GraphDocument
        {
            Id = GraphId.New(),
            Name = required ? "RequiredDelete" : "OptionalDelete",
            Side = GraphSide.SharedPredicted,
            Metadata = new GraphMetadata { CustomAttributes = new Dictionary<string, string> { ["required"] = required ? "true" : "false" } },
            Nodes =
            [
                new NodeDocument
                {
                    Id = entryId,
                    Name = "OnTick",
                    NodeType = "Event.Tick",
                    Pins = [new PinDocument { Id = from, Name = "Out", Direction = PinDirection.Output, Kind = PinKind.Execution }]
                },
                new NodeDocument
                {
                    Id = callId,
                    Name = "Delete",
                    NodeType = "Native.Call",
                    Properties = new Dictionary<string, string> { ["Method"] = "Entity.Delete" },
                    Pins = [new PinDocument { Id = to, Name = "In", Direction = PinDirection.Input, Kind = PinKind.Execution }]
                }
            ],
            Connections = [new ConnectionDocument { FromNode = entryId, FromPin = from, ToNode = callId, ToPin = to }]
        };
    }

    [Test]
    public void Compatibility_RejectsDynamicNativeOrdering()
    {
        var manifest = new EngineCompatibilityManifest
        {
            EngineFamily = "RobustToolbox",
            CompatibilityProfile = "robust-api-v1",
            TestedRobustCommit = "abc",
            EngineApiVersion = "1",
            DynamicNativeSystemOrdering = true
        };

        Assert.Throws<EngineCompatibilityException>(() =>
            new EngineCompatibilityService(manifest).EnsureCompatible(
                new EngineCompatibilityReport("abc", "1", ["type-event-subscribe"])));
    }

    private static NodeDocument EntryNode(string name) => new()
    {
        Id = NodeId.New(),
        Name = name,
        NodeType = "Event.Tick",
        Pins = [new PinDocument { Id = PinId.New(), Name = "Out", Direction = PinDirection.Output, Kind = PinKind.Execution }]
    };

    private static GraphDocument Entry(GraphId id, string name) => new()
    {
        Id = id,
        Name = "Live",
        Nodes = [EntryNode(name)]
    };

    private static string FindSample()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "samples", "ContentIntegration", "Resources", "AstraGraph", "DollToMothroach.agraph");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("DollToMothroach.agraph was not found.");
    }
}
