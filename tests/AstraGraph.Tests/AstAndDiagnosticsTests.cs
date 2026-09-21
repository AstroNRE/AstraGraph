using AstraGraph.Core;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class AstAndDiagnosticsTests
{
    [Test]
    public void DiagnosticBag_AccumulationAndSeverities()
    {
        var bag = new DiagnosticBag();
        Assert.That(bag.HasErrors, Is.False);
        Assert.That(bag.HasWarnings, Is.False);

        var nodeId = NodeId.New();
        var pinId = PinId.New();

        bag.ReportWarning("DOC001", "Unconnected pin", nodeId, pinId);
        Assert.That(bag.HasErrors, Is.False);
        Assert.That(bag.HasWarnings, Is.True);
        Assert.That(bag.Count, Is.EqualTo(1));

        bag.ReportError(DiagnosticCodes.TypeMismatch, "Cannot assign string to int", nodeId, pinId, suggestedFix: "Use string conversion");
        Assert.That(bag.HasErrors, Is.True);
        Assert.That(bag.Count, Is.EqualTo(2));

        var str = bag.ToString();
        Assert.That(str, Contains.Substring("TYP002"));
        Assert.That(str, Contains.Substring("Cannot assign string to int"));
    }

    [Test]
    public void AstProgram_TreeConstructionAndTypePreservation()
    {
        var graphId = GraphId.New();
        var varId = SymbolId.New();
        var entryNodeId = NodeId.New();
        var resumeId = Guid.NewGuid();

        var heatVar = new AstVariableDeclaration(varId, "CurrentHeat", PrimitiveType.Float32);

        // Expression: CurrentHeat > 100.0f
        var readHeat = new AstVariableReadExpression(varId, "CurrentHeat", PrimitiveType.Float32, entryNodeId);
        var literalThreshold = new AstLiteralExpression(100.0f, PrimitiveType.Float32, entryNodeId);
        var condition = new AstBinaryExpression(
            AstBinaryOperator.GreaterThan,
            readHeat,
            literalThreshold,
            PrimitiveType.Bool,
            entryNodeId);

        // True block: Delay 2.5s, then return
        var delayArg = new AstLiteralExpression(2.5f, PrimitiveType.Float32);
        var continuation = new AstYieldContinuationStatement(
            ContinuationKind.Delay,
            [delayArg],
            resumeId,
            entryNodeId);

        var trueBlock = new AstBlock([continuation, new AstReturnStatement()]);
        var falseBlock = new AstBlock([new AstReturnStatement()]);

        var branch = new AstBranchStatement(condition, trueBlock, falseBlock, entryNodeId);
        var entryPoint = new AstEntryPointStatement("OnTick", [], new AstBlock([branch]), entryNodeId);

        var program = new AstProgram(
            graphId,
            "CoolingSystem",
            GraphKind.System,
            GraphSide.Server,
            [heatVar],
            [entryPoint],
            []);

        Assert.That(program.Id, Is.EqualTo(graphId));
        Assert.That(program.Variables.Count, Is.EqualTo(1));
        Assert.That(program.EntryPoints.Count, Is.EqualTo(1));
        Assert.That(program.EntryPoints[0].Name, Is.EqualTo("OnTick"));

        var block = program.EntryPoints[0].Body;
        Assert.That(block.Statements.Count, Is.EqualTo(1));
        Assert.That(block.Statements[0], Is.TypeOf<AstBranchStatement>());

        var branchStmt = (AstBranchStatement)block.Statements[0];
        Assert.That(branchStmt.Condition, Is.TypeOf<AstBinaryExpression>());
        var bin = (AstBinaryExpression)branchStmt.Condition;
        Assert.That(bin.Type, Is.EqualTo(PrimitiveType.Bool));
        Assert.That(bin.Operator, Is.EqualTo(AstBinaryOperator.GreaterThan));
    }
}
