using AstraGraph.Core;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class DocumentModelTests
{
    [Test]
    public void StronglyTypedIdentifiers_EqualityAndSerialization()
    {
        var guid = Guid.NewGuid();
        var graphId = new GraphId(guid);
        var nodeId = new NodeId(guid);
        var pinId = new PinId(guid);
        var symbolId = new SymbolId(guid);
        var revisionId = new RevisionId(guid);
        var schemaId = new SchemaId(guid);
        var fieldId = new FieldId(guid);

        Assert.That(graphId.ToString(), Is.EqualTo(guid.ToString("D")));
        Assert.That(GraphId.FromString(guid.ToString("D")), Is.EqualTo(graphId));
        Assert.That(NodeId.FromString(guid.ToString("D")), Is.EqualTo(nodeId));
        Assert.That(PinId.FromString(guid.ToString("D")), Is.EqualTo(pinId));
        Assert.That(SymbolId.FromString(guid.ToString("D")), Is.EqualTo(symbolId));
        Assert.That(RevisionId.FromString(guid.ToString("D")), Is.EqualTo(revisionId));
        Assert.That(SchemaId.FromString(guid.ToString("D")), Is.EqualTo(schemaId));
        Assert.That(FieldId.FromString(guid.ToString("D")), Is.EqualTo(fieldId));

        Assert.That(GraphId.TryParse(guid.ToString(), out var parsed), Is.True);
        Assert.That(parsed, Is.EqualTo(graphId));
        Assert.That(GraphId.TryParse("invalid-guid", out _), Is.False);
    }

    [Test]
    public void GraphDocument_FullSerializationRoundtrip()
    {
        var node1Id = NodeId.New();
        var node2Id = NodeId.New();
        var pinOutExec = PinId.New();
        var pinInExec = PinId.New();
        var pinOutData = PinId.New();
        var pinInData = PinId.New();

        var document = new GraphDocument
        {
            Id = GraphId.New(),
            Name = "DollToMothroach",
            Kind = GraphKind.System,
            Side = GraphSide.Server,
            Metadata = new GraphMetadata
            {
                Author = "AstraAuthor",
                Description = "Transforms a doll into a mothroach after interaction",
                Version = "1.0.0",
                Tags = ["gameplay", "transformation"]
            },
            Variables =
            [
                new GraphVariableDocument
                {
                    Id = SymbolId.New(),
                    Name = "TransformDelaySeconds",
                    TypeName = "System.Single",
                    DefaultValue = "3.0",
                    IsPersistent = false,
                    IsReplicated = false
                }
            ],
            Nodes =
            [
                new NodeDocument
                {
                    Id = node1Id,
                    Name = "OnInteractEvent",
                    NodeType = "Event.InteractUsing",
                    Pins =
                    [
                        new PinDocument
                        {
                            Id = pinOutExec,
                            Name = "Out",
                            Direction = PinDirection.Output,
                            Kind = PinKind.Execution
                        },
                        new PinDocument
                        {
                            Id = pinOutData,
                            Name = "TargetEntity",
                            Direction = PinDirection.Output,
                            Kind = PinKind.Data,
                            DataType = "Robust.Shared.GameObjects.EntityUid"
                        }
                    ],
                    Properties = { ["FilterByTag"] = "Doll" }
                },
                new NodeDocument
                {
                    Id = node2Id,
                    Name = "SpawnMothroach",
                    NodeType = "Entity.Spawn",
                    Pins =
                    [
                        new PinDocument
                        {
                            Id = pinInExec,
                            Name = "In",
                            Direction = PinDirection.Input,
                            Kind = PinKind.Execution
                        },
                        new PinDocument
                        {
                            Id = pinInData,
                            Name = "PositionEntity",
                            Direction = PinDirection.Input,
                            Kind = PinKind.Data,
                            DataType = "Robust.Shared.GameObjects.EntityUid"
                        }
                    ],
                    Properties = { ["Prototype"] = "MobMothroach" }
                }
            ],
            Connections =
            [
                new ConnectionDocument
                {
                    FromNode = node1Id,
                    FromPin = pinOutExec,
                    ToNode = node2Id,
                    ToPin = pinInExec
                },
                new ConnectionDocument
                {
                    FromNode = node1Id,
                    FromPin = pinOutData,
                    ToNode = node2Id,
                    ToPin = pinInData
                }
            ],
            EditorLayout = new EditorLayoutDocument
            {
                ViewportX = 100,
                ViewportY = 200,
                Zoom = 1.25,
                NodePositions =
                {
                    [node1Id.ToString()] = new NodePosition(100, 150),
                    [node2Id.ToString()] = new NodePosition(450, 150)
                },
                Comments =
                [
                    new CommentBoxDocument
                    {
                        Title = "Transformation Section",
                        Text = "Triggers spawn when doll is activated",
                        X = 80,
                        Y = 100,
                        Width = 600,
                        Height = 250
                    }
                ]
            }
        };

        var json = GraphSerializer.Serialize(document, writeIndented: true);
        Assert.That(json, Is.Not.Null.And.Not.Empty);

        var deserialized = GraphSerializer.Deserialize(json);
        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized.Id, Is.EqualTo(document.Id));
        Assert.That(deserialized.Name, Is.EqualTo(document.Name));
        Assert.That(deserialized.Kind, Is.EqualTo(document.Kind));
        Assert.That(deserialized.Side, Is.EqualTo(document.Side));
        Assert.That(deserialized.Variables.Count, Is.EqualTo(1));
        Assert.That(deserialized.Nodes.Count, Is.EqualTo(2));
        Assert.That(deserialized.Connections.Count, Is.EqualTo(2));
        Assert.That(deserialized.EditorLayout.NodePositions.Count, Is.EqualTo(2));
        Assert.That(deserialized.EditorLayout.Comments.Count, Is.EqualTo(1));

        // Binary roundtrip
        var bytes = GraphSerializer.SerializeToUtf8Bytes(document);
        var fromBytes = GraphSerializer.Deserialize(bytes);
        Assert.That(fromBytes.Id, Is.EqualTo(document.Id));
    }

    [Test]
    public void SemanticHash_IsInvariantToLayoutChanges()
    {
        var node1Id = NodeId.New();
        var pin1Id = PinId.New();

        var doc1 = new GraphDocument
        {
            Id = GraphId.New(),
            Name = "TestGraph",
            Nodes =
            [
                new NodeDocument
                {
                    Id = node1Id,
                    Name = "TestNode",
                    NodeType = "Test.Type",
                    Pins = [new PinDocument { Id = pin1Id, Name = "Out", Direction = PinDirection.Output, Kind = PinKind.Execution }]
                }
            ],
            EditorLayout = new EditorLayoutDocument
            {
                ViewportX = 0,
                ViewportY = 0,
                Zoom = 1.0,
                NodePositions = { [node1Id.ToString()] = new NodePosition(10, 20) }
            }
        };

        // Create doc2 with identical semantic content but totally different layout, zoom, and comments
        var doc2 = new GraphDocument
        {
            Id = doc1.Id,
            Name = "TestGraph",
            Nodes =
            [
                new NodeDocument
                {
                    Id = node1Id,
                    Name = "TestNode",
                    NodeType = "Test.Type",
                    Pins = [new PinDocument { Id = pin1Id, Name = "Out", Direction = PinDirection.Output, Kind = PinKind.Execution }]
                }
            ],
            EditorLayout = new EditorLayoutDocument
            {
                ViewportX = 9999,
                ViewportY = 8888,
                Zoom = 2.5,
                NodePositions = { [node1Id.ToString()] = new NodePosition(500, 800) },
                Comments = [new CommentBoxDocument { Title = "Random Comment", Text = "Does not affect semantics", X = 0, Y = 0 }]
            }
        };

        var hash1 = AstraHash.ComputeSemanticHash(doc1);
        var hash2 = AstraHash.ComputeSemanticHash(doc2);

        Assert.That(hash1, Is.EqualTo(hash2), "Semantic hash MUST be identical regardless of visual layout or comments!");

        // Now modify a semantic property and verify hash DOES change
        doc2.Nodes[0].Properties["NewProperty"] = "ChangedValue";
        var hash3 = AstraHash.ComputeSemanticHash(doc2);
        Assert.That(hash3, Is.Not.EqualTo(hash1), "Semantic hash MUST change when semantic property is modified!");
    }
}
