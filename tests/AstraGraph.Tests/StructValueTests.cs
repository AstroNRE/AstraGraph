using System.Text;
using AstraGraph.Core;
using NUnit.Framework;
using AstraGraph.Persistence;
using AstraGraph.Persistence.State;
using AstraGraph.State;
using AstraGraph.VM;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class StructValueTests
{
    [Test]
    public void Graph_SetsAndReadsAStructField()
    {
        var value = Run(FieldGraph());
        Assert.That(value.AsInt64(), Is.EqualTo(9));
    }

    [Test]
    public void Graph_AddsAStructToAListAndReadsItBack()
    {
        var value = Run(ListGraph());
        Assert.That(value.AsInt64(), Is.EqualTo(9));
    }

    [Test]
    public void Graph_CopyDoesNotShareFields()
    {
        var value = Run(CopyGraph());
        Assert.That(value.AsInt64(), Is.EqualTo(9));
    }

    [Test]
    public void NestedSchemaField_ResolvesTheInnerStruct()
    {
        var partId = SchemaId.New();
        var document = new GraphDocument
        {
            Name = "Schemas",
            Kind = GraphKind.Library,
            Schemas =
            [
                new ComponentSchemaDocument
                {
                    Name = "Assembly",
                    Kind = "Struct",
                    Fields = [new ComponentFieldDocument { Name = "core", TypeName = "Part" }]
                },
                new ComponentSchemaDocument
                {
                    Id = partId,
                    Name = "Part",
                    Kind = "Struct",
                    Fields = [new ComponentFieldDocument { Name = "caliber", TypeName = "int32" }]
                }
            ],
            Nodes =
            [
                new NodeDocument
                {
                    Name = "Run",
                    NodeType = "EntryPoint",
                    Pins = [new PinDocument { Name = "Out", Direction = PinDirection.Output, Kind = PinKind.Execution }]
                }
            ]
        };

        var registry = TypeRegistry.CreateDefault();
        var analyzed = new SemanticAnalyzer(registry).Analyze(document);
        Assert.That(analyzed.Success, Is.True, analyzed.Diagnostics.ToString());
        Assert.That(registry.TryGetType("Assembly", out var assembly), Is.True);
        var field = ((SchemaType)assembly!).FindField("core");
        Assert.That(field, Is.Not.Null);
        Assert.That(((SchemaType)field!.Type).Id, Is.EqualTo(partId));
    }

    [Test]
    public void SetField_WritesAComponentByName()
    {
        AstraValue written = AstraValue.Null;
        var component = new SchemaComponentValue("Part", true, _ => AstraValue.Null, (_, value) => written = value);
        var fieldId = FieldId.New();
        AstraValues.SetField(AstraValue.FromObject(component), $"{fieldId.Value:D}|caliber", AstraValue.FromInt64(4));
        Assert.That(written.AsInt64(), Is.EqualTo(4));
    }

    [Test]
    public void PersistentState_RoundTripsStructListAndSkipsABadObject()
    {
        var root = Path.Combine(Path.GetTempPath(), "AstraGraph_StructState_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var layout = new StorageLayout(Path.Combine(root, "Resources", "AstraGraph"), Path.Combine(root, "data", "AstraGraph"));
            layout.EnsureDirectories();
            var store = new PersistentStateStore(layout);
            var graphId = GraphId.New();
            var structId = SymbolId.New();
            var stringId = SymbolId.New();
            var junkId = SymbolId.New();
            var schemaId = SchemaId.New();
            var fieldId = FieldId.New();
            var part = new AstraStruct(schemaId, "Part");
            part.Set(fieldId, "caliber", AstraValue.FromInt64(9));
            var parts = new AstraList();
            parts.Add(AstraValue.FromObject(part));
            var gun = new AstraStruct(SchemaId.New(), "Gun");
            gun.Set(FieldId.New(), "parts", AstraValue.FromObject(parts));

            var state = new AstraStateStore();
            state.SetVariable(graphId, structId, "Gun", AstraValue.FromObject(gun), isPersistent: true);
            state.SetVariable(graphId, stringId, "Name", AstraValue.FromObject("kept"), isPersistent: true);
            state.SetVariable(graphId, junkId, "Junk", AstraValue.FromObject(new object()), isPersistent: true);
            store.SaveState("bench", state);

            var restored = new AstraStateStore();
            Assert.That(store.RestoreState("bench", restored), Is.True);
            Assert.That(restored.GetVariable(graphId, stringId, "Name").AsString(), Is.EqualTo("kept"));
            Assert.That(restored.GetVariable(graphId, junkId, "Junk").Type, Is.EqualTo(AstraValueType.Null));
            var gunValue = restored.GetVariable(graphId, structId, "Gun").AsObject() as AstraStruct;
            Assert.That(gunValue, Is.Not.Null);
            var restoredParts = gunValue!.Get(FieldId.Empty, "parts").AsObject() as AstraList;
            Assert.That(restoredParts, Is.Not.Null);
            var restoredPart = restoredParts!.Get(0).AsObject() as AstraStruct;
            Assert.That(restoredPart, Is.Not.Null);
            Assert.That(restoredPart!.SchemaId, Is.EqualTo(schemaId));
            Assert.That(restoredPart.Get(fieldId, "bore").AsInt64(), Is.EqualTo(9));

            var copy = restoredPart.Copy();
            copy.Set(fieldId, "bore", AstraValue.FromInt64(1));
            Assert.That(restoredPart.Get(fieldId, "caliber").AsInt64(), Is.EqualTo(9));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public void PersistentState_ReadsVersion1AndIsolatesABrokenValue()
    {
        var root = Path.Combine(Path.GetTempPath(), "AstraGraph_StructState_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var layout = new StorageLayout(Path.Combine(root, "Resources", "AstraGraph"), Path.Combine(root, "data", "AstraGraph"));
            layout.EnsureDirectories();
            var store = new PersistentStateStore(layout);
            var graphId = GraphId.New();
            var symbolId = SymbolId.New();
            WriteVersion1(layout.GetStatePath("legacy"), graphId, symbolId, "SessionName", "Round42");

            var restored = new AstraStateStore();
            Assert.That(store.RestoreState("legacy", restored), Is.True);
            Assert.That(restored.GetVariable(graphId, symbolId, "SessionName").AsString(), Is.EqualTo("Round42"));

            var brokenGraph = GraphId.New();
            var brokenSymbol = SymbolId.New();
            var keptSymbol = SymbolId.New();
            WriteBrokenVersion2(layout.GetStatePath("broken"), brokenGraph, brokenSymbol, keptSymbol);
            var fresh = new AstraStateStore();
            Assert.That(store.RestoreState("broken", fresh), Is.True);
            Assert.That(fresh.GetVariable(brokenGraph, brokenSymbol, "Broken").Type, Is.EqualTo(AstraValueType.Null));
            Assert.That(fresh.GetVariable(brokenGraph, keptSymbol, "Kept").AsInt64(), Is.EqualTo(5));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static AstraValue Run(GraphDocument document)
    {
        var analyzed = new SemanticAnalyzer(TypeRegistry.CreateDefault()).Analyze(document);
        Assert.That(analyzed.Success, Is.True, analyzed.Diagnostics.ToString());
        var program = IrToBytecodeCompiler.Compile(AstToIrCompiler.Compile(analyzed.Program!), RevisionId.New(), "test");
        var function = program.FindEntryPoint("Run");
        Assert.That(function, Is.Not.Null);
        var result = new AstraVm().Execute(program, function!, hostServices: new DefaultVmHostServices());
        Assert.That(result.Status, Is.EqualTo(VmExecutionStatus.Completed), result.Exception?.ToString());
        return result.ReturnValue;
    }

    private static GraphDocument FieldGraph()
    {
        var graph = new GraphBuilder();
        graph.Schemas.Add(PartSchema());
        var entry = graph.Node("EntryPoint", "Run", null, graph.ExecOut("Out"));
        var make = graph.Node("Schema.Make", "Make", new Dictionary<string, string> { ["Schema"] = "Part" }, graph.DataOut("Value", "Part"));
        var set = graph.Node("Schema.SetField", "Set", Props(),
            graph.ExecIn("In"), graph.ExecOut("Out"),
            graph.DataIn("Target", "Part"), graph.DataIn("Value", "int32", "9"), graph.DataOut("Result", "Part"));
        var read = graph.Node("Schema.GetField", "Read", Props(),
            graph.DataIn("Component", "Part"), graph.DataOut("Value", "int32"));
        var done = graph.Node("Core.Return", "Return", null, graph.ExecIn("In"), graph.DataIn("Value", "int32"));
        graph.Wire(entry, "Out", set, "In");
        graph.Wire(set, "Out", done, "In");
        graph.Wire(make, "Value", set, "Target");
        graph.Wire(set, "Result", read, "Component");
        graph.Wire(read, "Value", done, "Value");
        return graph.Document("Field");
    }

    private static GraphDocument ListGraph()
    {
        var graph = new GraphBuilder();
        graph.Schemas.Add(PartSchema());
        var entry = graph.Node("EntryPoint", "Run", null, graph.ExecOut("Out"));
        var make = graph.Node("Schema.Make", "Make", new Dictionary<string, string> { ["Schema"] = "Part" }, graph.DataOut("Value", "Part"));
        var set = graph.Node("Schema.SetField", "Set", Props(),
            graph.ExecIn("In"), graph.ExecOut("Out"),
            graph.DataIn("Target", "Part"), graph.DataIn("Value", "int32", "9"), graph.DataOut("Result", "Part"));
        var create = graph.Node("List.Create", "Parts", null, graph.DataOut("List", "object"));
        var add = graph.Node("List.Add", "Add", null,
            graph.ExecIn("In"), graph.ExecOut("Out"),
            graph.DataIn("List", "object"), graph.DataIn("Item", "Part"), graph.DataOut("List", "object"));
        var get = graph.Node("List.Get", "First", null,
            graph.DataIn("List", "object"), graph.DataIn("Index", "int32", "0"), graph.DataOut("Value", "Part"));
        var read = graph.Node("Schema.GetField", "Read", Props(),
            graph.DataIn("Component", "Part"), graph.DataOut("Value", "int32"));
        var done = graph.Node("Core.Return", "Return", null, graph.ExecIn("In"), graph.DataIn("Value", "int32"));
        graph.Wire(entry, "Out", set, "In");
        graph.Wire(set, "Out", add, "In");
        graph.Wire(add, "Out", done, "In");
        graph.Wire(make, "Value", set, "Target");
        graph.Wire(set, "Result", add, "Item");
        graph.Wire(create, "List", add, "List");
        graph.Wire(add, "List", get, "List");
        graph.Wire(get, "Value", read, "Component");
        graph.Wire(read, "Value", done, "Value");
        return graph.Document("List");
    }

    private static GraphDocument CopyGraph()
    {
        var graph = new GraphBuilder();
        graph.Schemas.Add(PartSchema());
        var entry = graph.Node("EntryPoint", "Run", null, graph.ExecOut("Out"));
        var make = graph.Node("Schema.Make", "Make", new Dictionary<string, string> { ["Schema"] = "Part" }, graph.DataOut("Value", "Part"));
        var set = graph.Node("Schema.SetField", "Set", Props(),
            graph.ExecIn("In"), graph.ExecOut("Out"),
            graph.DataIn("Target", "Part"), graph.DataIn("Value", "int32", "9"), graph.DataOut("Result", "Part"));
        var copy = graph.Node("Schema.Copy", "Copy", null, graph.DataIn("Value", "Part"), graph.DataOut("Copy", "Part"));
        var rewrite = graph.Node("Schema.SetField", "Rewrite", Props(),
            graph.ExecIn("In"), graph.ExecOut("Out"),
            graph.DataIn("Target", "Part"), graph.DataIn("Value", "int32", "1"), graph.DataOut("Result", "Part"));
        var read = graph.Node("Schema.GetField", "Read", Props(),
            graph.DataIn("Component", "Part"), graph.DataOut("Value", "int32"));
        var done = graph.Node("Core.Return", "Return", null, graph.ExecIn("In"), graph.DataIn("Value", "int32"));
        graph.Wire(entry, "Out", set, "In");
        graph.Wire(set, "Out", rewrite, "In");
        graph.Wire(rewrite, "Out", done, "In");
        graph.Wire(make, "Value", set, "Target");
        graph.Wire(set, "Result", copy, "Value");
        graph.Wire(copy, "Copy", rewrite, "Target");
        graph.Wire(set, "Result", read, "Component");
        graph.Wire(read, "Value", done, "Value");
        return graph.Document("Copy");
    }

    private static ComponentSchemaDocument PartSchema() => new()
    {
        Name = "Part",
        Kind = "Struct",
        Fields = [new ComponentFieldDocument { Name = "caliber", TypeName = "int32" }]
    };

    private static Dictionary<string, string> Props() => new() { ["Schema"] = "Part", ["Field"] = "caliber" };

    private static void WriteVersion1(string path, GraphId graphId, SymbolId symbolId, string name, string text)
    {
        using var payloadMs = new MemoryStream();
        using (var writer = new BinaryWriter(payloadMs, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(1);
            writer.Write(graphId.Value.ToByteArray());
            writer.Write(symbolId.Value.ToByteArray());
            writer.Write(name);
            writer.Write((byte)AstraValueType.Object);
            writer.Write((byte)1);
            writer.Write(text);
        }

        WriteSnapshot(path, 1, payloadMs.ToArray());
    }

    private static void WriteBrokenVersion2(string path, GraphId graphId, SymbolId brokenId, SymbolId keptId)
    {
        using var payloadMs = new MemoryStream();
        using (var writer = new BinaryWriter(payloadMs, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(2);
            writer.Write(graphId.Value.ToByteArray());
            writer.Write(brokenId.Value.ToByteArray());
            writer.Write("Broken");
            writer.Write((byte)AstraValueType.Object);
            writer.Write(1);
            writer.Write((byte)2);
            writer.Write(graphId.Value.ToByteArray());
            writer.Write(keptId.Value.ToByteArray());
            writer.Write("Kept");
            writer.Write((byte)AstraValueType.Int64);
            writer.Write(8);
            writer.Write(5L);
        }

        WriteSnapshot(path, 2, payloadMs.ToArray());
    }

    private static void WriteSnapshot(string path, uint version, byte[] payload)
    {
        using var final = new MemoryStream();
        using (var writer = new BinaryWriter(final, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0x54534741u);
            writer.Write(version);
            writer.Write(payload.Length);
            writer.Write(payload);
            writer.Write(ChecksumUtility.ComputeCrc32(payload));
        }

        File.WriteAllBytes(path, final.ToArray());
    }

    private sealed class GraphBuilder
    {
        public List<ComponentSchemaDocument> Schemas { get; } = [];
        private readonly List<NodeDocument> _nodes = [];
        private readonly List<ConnectionDocument> _connections = [];

        public static PinSpec ExecIn(string name) => new(name, PinDirection.Input, PinKind.Execution, "", null);
        public static PinSpec ExecOut(string name) => new(name, PinDirection.Output, PinKind.Execution, "", null);
        public static PinSpec DataIn(string name, string type, string? value = null) => new(name, PinDirection.Input, PinKind.Data, type, value);
        public static PinSpec DataOut(string name, string type) => new(name, PinDirection.Output, PinKind.Data, type, null);

        public NodeDocument Node(string type, string name, Dictionary<string, string>? properties, params PinSpec[] pins)
        {
            var node = new NodeDocument
            {
                Name = name,
                NodeType = type,
                Properties = properties ?? [],
                Pins = pins.Select(pin => new PinDocument
                {
                    Name = pin.Name,
                    Direction = pin.Direction,
                    Kind = pin.Kind,
                    DataType = pin.Type,
                    DefaultValue = pin.DefaultValue
                }).ToList()
            };
            _nodes.Add(node);
            return node;
        }

        public void Wire(NodeDocument from, string fromPin, NodeDocument to, string toPin)
        {
            _connections.Add(new ConnectionDocument
            {
                FromNode = from.Id,
                FromPin = from.FindPin(fromPin, PinDirection.Output)!.Id,
                ToNode = to.Id,
                ToPin = to.FindPin(toPin, PinDirection.Input)!.Id
            });
        }

        public GraphDocument Document(string name) => new()
        {
            Name = name,
            Kind = GraphKind.System,
            Side = GraphSide.Server,
            Schemas = Schemas,
            Nodes = _nodes,
            Connections = _connections
        };
    }

    private readonly record struct PinSpec(string Name, PinDirection Direction, PinKind Kind, string Type, string? DefaultValue);
}
