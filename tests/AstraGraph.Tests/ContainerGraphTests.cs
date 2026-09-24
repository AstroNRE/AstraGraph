using AstraGraph.Core;
using AstraGraph.VM;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class ContainerGraphTests
{
    [Test]
    public void Insert_CallsTheContainerBindingOnce()
    {
        var host = new DefaultVmHostServices();
        var calls = 0;
        host.RegisterNativeMethod("Container.Insert", args =>
        {
            calls++;
            Assert.That(args[0].AsInt32(), Is.EqualTo(7));
            Assert.That(args[1].AsString(), Is.EqualTo("storage"));
            Assert.That(args[2].AsInt32(), Is.EqualTo(3));
            return AstraValue.FromBool(true);
        });

        var graph = new GraphBuilder();
        var entry = graph.Node("EntryPoint", "Run", graph.ExecOut("Out"));
        var insert = graph.Node("Container.Insert", "Insert",
            graph.ExecIn("In"), graph.ExecOut("Out"),
            graph.DataIn("Owner", "int32", "7"),
            graph.DataIn("Container", "string", "storage"),
            graph.DataIn("Item", "int32", "3"),
            graph.DataOut("Success", "bool"));
        var done = graph.Node("Core.Return", "Return", graph.ExecIn("In"), graph.DataIn("Value", "bool"));
        graph.Wire(entry, "Out", insert, "In");
        graph.Wire(insert, "Out", done, "In");
        graph.Wire(insert, "Success", done, "Value");

        var analyzed = new SemanticAnalyzer(TypeRegistry.CreateDefault()).Analyze(graph.Document());
        Assert.That(analyzed.Success, Is.True, analyzed.Diagnostics.ToString());
        var program = IrToBytecodeCompiler.Compile(AstToIrCompiler.Compile(analyzed.Program!), RevisionId.New(), "test");
        var function = program.FindEntryPoint("Run");
        var result = new AstraVm().Execute(program, function!, hostServices: host);
        Assert.That(result.Status, Is.EqualTo(VmExecutionStatus.Completed), result.Exception?.ToString());
        Assert.That(result.ReturnValue.AsBool(), Is.True);
        Assert.That(calls, Is.EqualTo(1));
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

        public GraphDocument Document() => new()
        {
            Name = "Container",
            Kind = GraphKind.System,
            Side = GraphSide.Server,
            Nodes = _nodes,
            Connections = _connections
        };
    }

    private readonly record struct PinSpec(string Name, PinDirection Direction, PinKind Kind, string Type, string? DefaultValue);
}
