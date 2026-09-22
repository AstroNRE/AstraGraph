using AstraGraph.Core;
using AstraGraph.HotReload;
using AstraGraph.Persistence;
using AstraGraph.Runtime;
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
