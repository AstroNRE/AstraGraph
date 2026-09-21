using AstraGraph.Core;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class SemanticAnalyzerTests
{
    private TypeRegistry _registry = null!;
    private SemanticAnalyzer _analyzer = null!;

    [SetUp]
    public void SetUp()
    {
        _registry = TypeRegistry.CreateDefault();
        _analyzer = new SemanticAnalyzer(_registry);
    }

    [Test]
    public void Analyze_ValidBranchingGraph_ProducesAstProgram()
    {
        var entryNodeId = NodeId.New();
        var branchNodeId = NodeId.New();
        var assignNodeId = NodeId.New();
        var delayNodeId = NodeId.New();
        var returnNodeId = NodeId.New();

        var execEntryOut = PinId.New();
        var execBranchIn = PinId.New();
        var condBranchIn = PinId.New();
        var execBranchTrue = PinId.New();
        var execBranchFalse = PinId.New();
        var execAssignIn = PinId.New();
        var execAssignOut = PinId.New();
        var valAssignIn = PinId.New();
        var execDelayIn = PinId.New();
        var execDelayOut = PinId.New();
        var execReturnIn = PinId.New();

        var varId = SymbolId.New();

        var doc = new GraphDocument
        {
            Id = GraphId.New(),
            Name = "ValidBranchGraph",
            Kind = GraphKind.System,
            Side = GraphSide.Server,
            Variables =
            [
                new GraphVariableDocument
                {
                    Id = varId,
                    Name = "Counter",
                    TypeName = "int32",
                    DefaultValue = "0"
                }
            ],
            Nodes =
            [
                new NodeDocument
                {
                    Id = entryNodeId,
                    Name = "OnStart",
                    NodeType = "Event.Initialize",
                    Pins = [new PinDocument { Id = execEntryOut, Name = "Out", Direction = PinDirection.Output, Kind = PinKind.Execution }]
                },
                new NodeDocument
                {
                    Id = branchNodeId,
                    Name = "CheckCounter",
                    NodeType = "Core.Branch",
                    Pins =
                    [
                        new PinDocument { Id = execBranchIn, Name = "In", Direction = PinDirection.Input, Kind = PinKind.Execution },
                        new PinDocument { Id = condBranchIn, Name = "Condition", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "bool", DefaultValue = "true" },
                        new PinDocument { Id = execBranchTrue, Name = "True", Direction = PinDirection.Output, Kind = PinKind.Execution },
                        new PinDocument { Id = execBranchFalse, Name = "False", Direction = PinDirection.Output, Kind = PinKind.Execution }
                    ]
                },
                new NodeDocument
                {
                    Id = assignNodeId,
                    Name = "IncrementCounter",
                    NodeType = "Core.VariableAssign",
                    Pins =
                    [
                        new PinDocument { Id = execAssignIn, Name = "In", Direction = PinDirection.Input, Kind = PinKind.Execution },
                        new PinDocument { Id = valAssignIn, Name = "Value", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "int32", DefaultValue = "42" },
                        new PinDocument { Id = execAssignOut, Name = "Out", Direction = PinDirection.Output, Kind = PinKind.Execution }
                    ],
                    Properties = { ["VariableName"] = "Counter" }
                },
                new NodeDocument
                {
                    Id = delayNodeId,
                    Name = "WaitOneSecond",
                    NodeType = "Flow.Delay",
                    Pins =
                    [
                        new PinDocument { Id = execDelayIn, Name = "In", Direction = PinDirection.Input, Kind = PinKind.Execution },
                        new PinDocument { Id = execDelayOut, Name = "Out", Direction = PinDirection.Output, Kind = PinKind.Execution }
                    ]
                },
                new NodeDocument
                {
                    Id = returnNodeId,
                    Name = "Exit",
                    NodeType = "Core.Return",
                    Pins =
                    [
                        new PinDocument { Id = execReturnIn, Name = "In", Direction = PinDirection.Input, Kind = PinKind.Execution }
                    ]
                }
            ],
            Connections =
            [
                new ConnectionDocument { FromNode = entryNodeId, FromPin = execEntryOut, ToNode = branchNodeId, ToPin = execBranchIn },
                new ConnectionDocument { FromNode = branchNodeId, FromPin = execBranchTrue, ToNode = assignNodeId, ToPin = execAssignIn },
                new ConnectionDocument { FromNode = assignNodeId, FromPin = execAssignOut, ToNode = delayNodeId, ToPin = execDelayIn },
                new ConnectionDocument { FromNode = branchNodeId, FromPin = execBranchFalse, ToNode = returnNodeId, ToPin = execReturnIn }
            ]
        };

        var result = _analyzer.Analyze(doc);

        Assert.That(result.Success, Is.True, $"Analysis failed with diagnostics: {result.Diagnostics}");
        Assert.That(result.Program, Is.Not.Null);
        Assert.That(result.Program!.EntryPoints.Count, Is.EqualTo(1));
        Assert.That(result.Program.Variables.Count, Is.EqualTo(1));

        var entry = result.Program.EntryPoints[0];
        Assert.That(entry.Body.Statements.Count, Is.EqualTo(1));
        Assert.That(entry.Body.Statements[0], Is.TypeOf<AstBranchStatement>());

        var branch = (AstBranchStatement)entry.Body.Statements[0];
        Assert.That(branch.TrueBlock.Statements.Count, Is.EqualTo(2));
        Assert.That(branch.TrueBlock.Statements[0], Is.TypeOf<AstVariableAssignStatement>());
        Assert.That(branch.TrueBlock.Statements[1], Is.TypeOf<AstYieldContinuationStatement>());

        Assert.That(branch.FalseBlock, Is.Not.Null);
        Assert.That(branch.FalseBlock!.Statements.Count, Is.EqualTo(1));
        Assert.That(branch.FalseBlock.Statements[0], Is.TypeOf<AstReturnStatement>());
    }

    [Test]
    public void Analyze_TypeMismatch_ReportsDiagnosticError()
    {
        var node1 = NodeId.New();
        var node2 = NodeId.New();
        var pinOut = PinId.New();
        var pinIn = PinId.New();

        var doc = new GraphDocument
        {
            Id = GraphId.New(),
            Name = "BadTypeGraph",
            Nodes =
            [
                new NodeDocument
                {
                    Id = node1,
                    Name = "StringSource",
                    Pins = [new PinDocument { Id = pinOut, Name = "Text", Direction = PinDirection.Output, Kind = PinKind.Data, DataType = "string" }]
                },
                new NodeDocument
                {
                    Id = node2,
                    Name = "IntSink",
                    Pins = [new PinDocument { Id = pinIn, Name = "Number", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "int32" }]
                }
            ],
            Connections =
            [
                new ConnectionDocument { FromNode = node1, FromPin = pinOut, ToNode = node2, ToPin = pinIn }
            ]
        };

        var result = _analyzer.Analyze(doc);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics.HasErrors, Is.True);
        Assert.That(result.Diagnostics.Any(d => d.Code == DiagnosticCodes.TypeMismatch), Is.True);
    }

    [Test]
    public void Analyze_MultipleExecutionWiresIntoSinglePin_ReportsError()
    {
        var node1 = NodeId.New();
        var node2 = NodeId.New();
        var node3 = NodeId.New();

        var out1 = PinId.New();
        var out2 = PinId.New();
        var inPin = PinId.New();

        var doc = new GraphDocument
        {
            Id = GraphId.New(),
            Name = "MultiExecGraph",
            Nodes =
            [
                new NodeDocument { Id = node1, Name = "N1", Pins = [new PinDocument { Id = out1, Direction = PinDirection.Output, Kind = PinKind.Execution }] },
                new NodeDocument { Id = node2, Name = "N2", Pins = [new PinDocument { Id = out2, Direction = PinDirection.Output, Kind = PinKind.Execution }] },
                new NodeDocument { Id = node3, Name = "N3", Pins = [new PinDocument { Id = inPin, Direction = PinDirection.Input, Kind = PinKind.Execution }] }
            ],
            Connections =
            [
                new ConnectionDocument { FromNode = node1, FromPin = out1, ToNode = node3, ToPin = inPin },
                new ConnectionDocument { FromNode = node2, FromPin = out2, ToNode = node3, ToPin = inPin }
            ]
        };

        var result = _analyzer.Analyze(doc);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics.Any(d => d.Code == DiagnosticCodes.MultipleInputConnectionsToExecutionPin), Is.True);
    }

    [Test]
    public void Analyze_PolicyViolation_ServerApiOnClient_ReportsError()
    {
        var node1 = NodeId.New();
        var doc = new GraphDocument
        {
            Id = GraphId.New(),
            Name = "ClientGraph",
            Side = GraphSide.Client,
            Nodes =
            [
                new NodeDocument
                {
                    Id = node1,
                    Name = "ServerCommandNode",
                    Properties = { ["Side"] = "Server" }
                }
            ]
        };

        var result = _analyzer.Analyze(doc);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics.Any(d => d.Code == DiagnosticCodes.ServerApiCalledOnClient), Is.True);
    }

    [Test]
    public void Analyze_PureDataLoop_ReportsInfiniteDataLoopError()
    {
        var node1 = NodeId.New();
        var node2 = NodeId.New();

        var p1In = PinId.New();
        var p1Out = PinId.New();
        var p2In = PinId.New();
        var p2Out = PinId.New();

        var doc = new GraphDocument
        {
            Id = GraphId.New(),
            Name = "CyclicDataGraph",
            Nodes =
            [
                new NodeDocument
                {
                    Id = node1,
                    Name = "Add1",
                    NodeType = "Math.Add",
                    Pins =
                    [
                        new PinDocument { Id = p1In, Name = "A", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "int32" },
                        new PinDocument { Id = p1Out, Name = "Result", Direction = PinDirection.Output, Kind = PinKind.Data, DataType = "int32" }
                    ]
                },
                new NodeDocument
                {
                    Id = node2,
                    Name = "Add2",
                    NodeType = "Math.Add",
                    Pins =
                    [
                        new PinDocument { Id = p2In, Name = "A", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "int32" },
                        new PinDocument { Id = p2Out, Name = "Result", Direction = PinDirection.Output, Kind = PinKind.Data, DataType = "int32" }
                    ]
                },
                new NodeDocument
                {
                    Id = NodeId.New(),
                    Name = "Entry",
                    NodeType = "Event.Test",
                    Pins =
                    [
                        new PinDocument { Id = PinId.New(), Name = "Out", Direction = PinDirection.Output, Kind = PinKind.Execution }
                    ]
                }
            ],
            Connections =
            [
                new ConnectionDocument { FromNode = node1, FromPin = p1Out, ToNode = node2, ToPin = p2In },
                new ConnectionDocument { FromNode = node2, FromPin = p2Out, ToNode = node1, ToPin = p1In }
            ]
        };

        var result = _analyzer.Analyze(doc);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics.Any(d => d.Code == DiagnosticCodes.InfinitePureDataLoop), Is.True);
    }
}
