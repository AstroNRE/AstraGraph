using AstraGraph.Core;
using AstraGraph.HotReload;
using AstraGraph.Runtime;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class HotReloadTests
{
    [Test]
    public void HotReloadManager_AtomicPublishAndRollback()
    {
        var host = new AstraGraphHost();
        var manager = new HotReloadManager(host);

        var graphId = GraphId.New();

        // Draft v1: Simple entry point
        var docV1 = new GraphDocument
        {
            Id = graphId,
            Name = "LiveGameLogic",
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

        // 1. Publish v1
        var pub1 = manager.Publish(docV1, author: "AuthorA", message: "Initial commit");
        Assert.That(pub1.Success, Is.True);
        Assert.That(pub1.PublishedRevisionId, Is.Not.Null);

        var activeV1 = host.GetProgram(graphId);
        Assert.That(activeV1, Is.Not.Null);
        Assert.That(activeV1!.Revision, Is.EqualTo(pub1.PublishedRevisionId!.Value));

        // 2. Draft v2: Update graph with second node
        var docV2 = new GraphDocument
        {
            Id = graphId,
            Name = "LiveGameLogic",
            Nodes =
            [
                new NodeDocument
                {
                    Id = NodeId.New(),
                    Name = "OnTick",
                    NodeType = "Event.Tick",
                    Pins = [new PinDocument { Id = PinId.New(), Name = "Out", Direction = PinDirection.Output, Kind = PinKind.Execution }]
                },
                new NodeDocument
                {
                    Id = NodeId.New(),
                    Name = "SecondNode",
                    NodeType = "Event.Secondary",
                    Pins = [new PinDocument { Id = PinId.New(), Name = "Out", Direction = PinDirection.Output, Kind = PinKind.Execution }]
                }
            ]
        };

        var pub2 = manager.Publish(docV2, author: "AuthorA", message: "Added second event");
        Assert.That(pub2.Success, Is.True);

        var activeV2 = host.GetProgram(graphId);
        Assert.That(activeV2!.Revision, Is.EqualTo(pub2.PublishedRevisionId!.Value));

        var history = manager.GetRevisionHistory(graphId);
        Assert.That(history.Count, Is.EqualTo(2));
        Assert.That(history[1].ParentRevisionId, Is.EqualTo(activeV1.Revision));

        // 3. Rollback to LKG
        var rolledBack = manager.Rollback(graphId);
        Assert.That(rolledBack, Is.True);

        var activeAfterRollback = host.GetProgram(graphId);
        Assert.That(activeAfterRollback!.Revision, Is.EqualTo(activeV1.Revision));
    }

    [Test]
    public void HotReloadManager_InvalidDraft_DoesNotOverwriteRunningRevision()
    {
        var host = new AstraGraphHost();
        var manager = new HotReloadManager(host);
        var graphId = GraphId.New();

        // Valid v1
        var docV1 = new GraphDocument
        {
            Id = graphId,
            Name = "ValidV1",
            Nodes = [new NodeDocument { Id = NodeId.New(), Name = "OnStart", NodeType = "Event.Start", Pins = [new PinDocument { Id = PinId.New(), Direction = PinDirection.Output, Kind = PinKind.Execution }] }]
        };
        var pub1 = manager.Publish(docV1);
        Assert.That(pub1.Success, Is.True);
        var initialRevision = host.GetProgram(graphId)!.Revision;

        // Invalid draft: connect Execution to Data pin
        var n1 = NodeId.New();
        var n2 = NodeId.New();
        var p1 = PinId.New();
        var p2 = PinId.New();

        var brokenDraft = new GraphDocument
        {
            Id = graphId,
            Name = "BrokenDraft",
            Nodes =
            [
                new NodeDocument { Id = n1, Name = "N1", Pins = [new PinDocument { Id = p1, Direction = PinDirection.Output, Kind = PinKind.Execution }] },
                new NodeDocument { Id = n2, Name = "N2", Pins = [new PinDocument { Id = p2, Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "int32" }] }
            ],
            Connections = [new ConnectionDocument { FromNode = n1, FromPin = p1, ToNode = n2, ToPin = p2 }]
        };

        var pub2 = manager.Publish(brokenDraft);
        Assert.That(pub2.Success, Is.False);
        Assert.That(pub2.Diagnostics.HasErrors, Is.True);

        // Host still runs initial revision!
        Assert.That(host.GetProgram(graphId)!.Revision, Is.EqualTo(initialRevision));
    }
}
