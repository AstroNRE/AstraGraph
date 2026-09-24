using AstraGraph.Core;
using AstraGraph.UI.Html;
using AstraGraph.VM;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class UiListRowsTests
{
    [Test]
    public void Format_WritesIdTextAndDisabledFromStructFields()
    {
        var open = Part("barrel-1", "Barrel", false);
        var blocked = Part(PersistentObjectId.New().Value.ToString("D"), "Stock", true);
        var list = new AstraList([open, blocked]);

        var json = UiListRows.Format(list, "Id", "Text", "Disabled");

        Assert.That(json, Does.Contain("\"id\":\"barrel-1\""));
        Assert.That(json, Does.Contain("\"text\":\"Barrel\""));
        Assert.That(json, Does.Contain("\"disabled\":false"));
        Assert.That(json, Does.Contain("\"text\":\"Stock\""));
        Assert.That(json, Does.Contain("\"disabled\":true"));
        Assert.That(UiListRows.Field("{\"id\":\"barrel-1\"}", "id"), Is.EqualTo("barrel-1"));
        Assert.That(UiListRows.Field("{", "id"), Is.EqualTo(""));
        Assert.That(UiListRows.Format(null, "Id", "Text", "Disabled"), Is.EqualTo("[]"));
    }

    [Test]
    public void Graph_ReadsTheActionId()
    {
        var host = new DefaultVmHostServices();
        host.RegisterNativeMethod("Bui.Field", args =>
            AstraValue.FromString(UiListRows.Field(args[0].AsString(), args[1].AsString())));

        var graph = new GraphBuilder();
        var entry = graph.Node("EntryPoint", "Run", graph.ExecOut("Out"));
        var field = graph.Node("Bui.Field", "Id",
            graph.DataIn("Payload", "string", "{\"id\":\"barrel-1\"}"),
            graph.DataIn("Field", "string", "id"),
            graph.DataOut("Value", "string"));
        var done = graph.Node("Core.Return", "Return", graph.ExecIn("In"), graph.DataIn("Value", "string"));
        graph.Wire(entry, "Out", done, "In");
        graph.Wire(field, "Value", done, "Value");

        var analyzed = new SemanticAnalyzer(TypeRegistry.CreateDefault()).Analyze(graph.Document(GraphSide.Server));
        Assert.That(analyzed.Success, Is.True, analyzed.Diagnostics.ToString());
        var program = IrToBytecodeCompiler.Compile(AstToIrCompiler.Compile(analyzed.Program!), RevisionId.New(), "test");
        var result = new AstraVm().Execute(program, program.FindEntryPoint("Run")!, hostServices: host);
        Assert.That(result.Status, Is.EqualTo(VmExecutionStatus.Completed), result.Exception?.ToString());
        Assert.That(result.ReturnValue.AsString(), Is.EqualTo("barrel-1"));

        var predicted = new SemanticAnalyzer(TypeRegistry.CreateDefault()).Analyze(new GraphDocument
        {
            Name = "Predicted",
            Kind = GraphKind.System,
            Side = GraphSide.SharedPredicted,
            Nodes =
            [
                new NodeDocument
                {
                    Name = "State",
                    NodeType = "Bui.Set",
                    Pins = []
                }
            ]
        });
        Assert.That(predicted.Diagnostics.ToString(), Does.Contain("non-deterministic"));
    }

    private static AstraValue Part(string id, string text, bool disabled)
    {
        var part = new AstraStruct(SchemaId.New(), "Part");
        part.Set(FieldId.New(), "Id", AstraValue.FromString(id));
        part.Set(FieldId.New(), "Text", AstraValue.FromString(text));
        part.Set(FieldId.New(), "Disabled", AstraValue.FromBool(disabled));
        return AstraValue.FromObject(part);
    }

    private sealed class GraphBuilder
    {
        private readonly List<NodeDocument> _nodes = [];
        private readonly List<ConnectionDocument> _connections = [];

#pragma warning disable CA1822
        public PinSpec ExecIn(string name) => new(name, PinDirection.Input, PinKind.Execution, "", null);
        public PinSpec ExecOut(string name) => new(name, PinDirection.Output, PinKind.Execution, "", null);
        public PinSpec DataIn(string name, string type, string? value = null) => new(name, PinDirection.Input, PinKind.Data, type, value);
        public PinSpec DataOut(string name, string type) => new(name, PinDirection.Output, PinKind.Data, type, null);
#pragma warning restore CA1822

        public NodeDocument Node(string type, string name, params PinSpec[] pins)
        {
            var node = new NodeDocument
            {
                Name = name,
                NodeType = type,
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

        public GraphDocument Document(GraphSide side) => new()
        {
            Name = "Bench",
            Kind = GraphKind.System,
            Side = side,
            Nodes = _nodes,
            Connections = _connections
        };
    }

    private readonly record struct PinSpec(string Name, PinDirection Direction, PinKind Kind, string Type, string? DefaultValue);
}
