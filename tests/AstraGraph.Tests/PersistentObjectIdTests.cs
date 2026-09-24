using AstraGraph.Core;
using AstraGraph.Persistence;
using AstraGraph.Persistence.State;
using AstraGraph.Runtime.Network;
using AstraGraph.State;
using AstraGraph.VM;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class PersistentObjectIdTests
{
    [Test]
    public void Identity_IsNotAnEntityUid()
    {
        var id = PersistentObjectId.New();
        var value = AstraValue.FromPersistentId(id);
        Assert.That(value.Type, Is.EqualTo(AstraValueType.PersistentId));
        Assert.That(value.AsPersistentId(), Is.EqualTo(id));
        Assert.That(value, Is.EqualTo(AstraValue.FromPersistentId(id)));
        Assert.That(value, Is.Not.EqualTo(AstraValue.FromEntityUid(1)));
        Assert.That(id.IsEmpty, Is.False);
    }

    [Test]
    public void Graph_StoresANewIdOnAStruct()
    {
        var first = Run(IdGraph()).AsPersistentId();
        var second = Run(IdGraph()).AsPersistentId();
        Assert.That(first.IsEmpty, Is.False);
        Assert.That(second, Is.Not.EqualTo(first));
    }

    [Test]
    public void PredictedGraph_RejectsANewId()
    {
        var document = new GraphDocument
        {
            Name = "Predicted",
            Side = GraphSide.SharedPredicted,
            Nodes =
            [
                new NodeDocument
                {
                    Name = "New",
                    NodeType = "PersistentId.New",
                    Pins = [new PinDocument { Name = "Out", Direction = PinDirection.Output, Kind = PinKind.Execution }]
                }
            ]
        };

        var analyzed = new SemanticAnalyzer(TypeRegistry.CreateDefault()).Analyze(document);
        Assert.That(analyzed.Success, Is.False);
        Assert.That(analyzed.Diagnostics.ToString(), Does.Contain("non-deterministic"));
    }

    [Test]
    public void CopyAndSave_KeepTheSameId()
    {
        var fieldId = FieldId.New();
        var id = PersistentObjectId.New();
        var item = new AstraStruct(SchemaId.New(), "Tool");
        item.Set(fieldId, "id", AstraValue.FromPersistentId(id));
        var copy = item.Copy();
        copy.Set(fieldId, "id", AstraValue.FromPersistentId(PersistentObjectId.New()));
        Assert.That(item.Get(fieldId, "id").AsPersistentId(), Is.EqualTo(id));

        var root = Path.Combine(Path.GetTempPath(), "AstraGraph_PersistentId_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var layout = new StorageLayout(Path.Combine(root, "Resources", "AstraGraph"), Path.Combine(root, "data", "AstraGraph"));
            layout.EnsureDirectories();
            var store = new PersistentStateStore(layout);
            var graphId = GraphId.New();
            var symbolId = SymbolId.New();
            var state = new AstraStateStore();
            state.SetVariable(graphId, symbolId, "Tool", AstraValue.FromObject(item), isPersistent: true);
            store.SaveState("item", state);

            var restored = new AstraStateStore();
            Assert.That(store.RestoreState("item", restored), Is.True);
            var value = restored.GetVariable(graphId, symbolId, "Tool").AsObject() as AstraStruct;
            Assert.That(value, Is.Not.Null);
            Assert.That(value!.Get(fieldId, "serial").AsPersistentId(), Is.EqualTo(id));
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
    public void Replication_RoundTripsTheId()
    {
        var fieldId = FieldId.New();
        var schema = new SchemaType(SchemaId.New(), "Item", false, [new SchemaField(fieldId, "id", PersistentIdType.Instance)]);
        var storage = new PackedFieldStorage(schema);
        var id = PersistentObjectId.New();
        storage.SetField(fieldId, AstraValue.FromPersistentId(id));

        var restored = DeltaReplicationManager.DeserializeComponent(DeltaReplicationManager.SerializeComponent(storage), schema);
        Assert.That(restored.GetField(fieldId).AsPersistentId(), Is.EqualTo(id));
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

    private static GraphDocument IdGraph()
    {
        var graph = new GraphBuilder();
        graph.Schemas.Add(new ComponentSchemaDocument
        {
            Name = "Part",
            Kind = "Struct",
            Fields = [new ComponentFieldDocument { Name = "id", TypeName = "PersistentObjectId" }]
        });
        var entry = graph.Node("EntryPoint", "Run", null, graph.ExecOut("Out"));
        var created = graph.Node("PersistentId.New", "New", null, graph.ExecIn("In"), graph.ExecOut("Out"), graph.DataOut("Id", "PersistentObjectId"));
        var make = graph.Node("Schema.Make", "Make", new Dictionary<string, string> { ["Schema"] = "Part" }, graph.DataOut("Value", "Part"));
        var set = graph.Node("Schema.SetField", "Set", new Dictionary<string, string> { ["Schema"] = "Part", ["Field"] = "id" },
            graph.ExecIn("In"), graph.ExecOut("Out"),
            graph.DataIn("Target", "Part"), graph.DataIn("Value", "PersistentObjectId"), graph.DataOut("Result", "Part"));
        var read = graph.Node("Schema.GetField", "Read", new Dictionary<string, string> { ["Schema"] = "Part", ["Field"] = "id" },
            graph.DataIn("Component", "Part"), graph.DataOut("Value", "PersistentObjectId"));
        var done = graph.Node("Core.Return", "Return", null, graph.ExecIn("In"), graph.DataIn("Value", "PersistentObjectId"));
        graph.Wire(entry, "Out", created, "In");
        graph.Wire(created, "Out", set, "In");
        graph.Wire(set, "Out", done, "In");
        graph.Wire(created, "Id", set, "Value");
        graph.Wire(make, "Value", set, "Target");
        graph.Wire(set, "Result", read, "Component");
        graph.Wire(read, "Value", done, "Value");
        return graph.Document();
    }

    private sealed class GraphBuilder
    {
        public List<ComponentSchemaDocument> Schemas { get; } = [];
        private readonly List<NodeDocument> _nodes = [];
        private readonly List<ConnectionDocument> _connections = [];

#pragma warning disable CA1822
        public PinSpec ExecIn(string name) => new(name, PinDirection.Input, PinKind.Execution, "", null);
        public PinSpec ExecOut(string name) => new(name, PinDirection.Output, PinKind.Execution, "", null);
        public PinSpec DataIn(string name, string type) => new(name, PinDirection.Input, PinKind.Data, type, null);
        public PinSpec DataOut(string name, string type) => new(name, PinDirection.Output, PinKind.Data, type, null);
#pragma warning restore CA1822

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

        public GraphDocument Document() => new()
        {
            Name = "Identity",
            Kind = GraphKind.System,
            Side = GraphSide.Server,
            Schemas = Schemas,
            Nodes = _nodes,
            Connections = _connections
        };
    }

    private readonly record struct PinSpec(string Name, PinDirection Direction, PinKind Kind, string Type, string? DefaultValue);
}
