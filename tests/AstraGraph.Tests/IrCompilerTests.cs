using AstraGraph.Core;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class IrCompilerTests
{
    [Test]
    public void AstToIrCompiler_CompilesBranchAndContinuation_ProducesVerifiedCFG()
    {
        var graphId = GraphId.New();
        var varId = SymbolId.New();
        var entryNodeId = NodeId.New();

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

        // True block: Delay 1.5s, then assign CurrentHeat = 0.0f
        var delayArg = new AstLiteralExpression(1.5f, PrimitiveType.Float32);
        var continuation = new AstYieldContinuationStatement(
            ContinuationKind.Delay,
            [delayArg],
            Guid.NewGuid(),
            entryNodeId);

        var assignZero = new AstVariableAssignStatement(
            varId,
            "CurrentHeat",
            new AstLiteralExpression(0.0f, PrimitiveType.Float32),
            entryNodeId);

        var trueBlock = new AstBlock([continuation, assignZero]);
        var falseBlock = new AstBlock([new AstReturnStatement()]);

        var branch = new AstBranchStatement(condition, trueBlock, falseBlock, entryNodeId);
        var entryPoint = new AstEntryPointStatement("OnTick", [], new AstBlock([branch]), entryNodeId);

        var astProgram = new AstProgram(
            graphId,
            "TestHeatIR",
            GraphKind.System,
            GraphSide.Server,
            [heatVar],
            [entryPoint],
            []);

        var irProgram = AstToIrCompiler.Compile(astProgram);

        Assert.That(irProgram, Is.Not.Null);
        Assert.That(irProgram.EntryPoints.Count, Is.EqualTo(1));

        var irFunc = irProgram.EntryPoints[0];
        Assert.That(irFunc.Name, Is.EqualTo("OnTick"));
        Assert.That(irFunc.Blocks.Count, Is.GreaterThanOrEqualTo(4)); // entry, then, resume, else, merge

        var entryBlock = irFunc.EntryBlock;
        Assert.That(entryBlock.Terminator, Is.Not.Null);
        Assert.That(entryBlock.Terminator!.OpCode, Is.EqualTo(IrOpCode.BranchIf));
        Assert.That(entryBlock.Successors.Count, Is.EqualTo(2));

        // Verify with IrVerifier directly
        var diags = IrVerifier.Verify(irProgram);
        Assert.That(diags.HasErrors, Is.False, $"IrVerifier failed with: {diags}");
    }

    [Test]
    public void IrVerifier_CatchesMissingTerminator()
    {
        var entry = new IrBasicBlock(0, "entry");
        // No terminator set!

        var func = new IrFunction("BrokenFunc", [], PrimitiveType.Void, entry);
        var program = new IrProgram(GraphId.New(), "BrokenProg", GraphKind.System, GraphSide.Server);
        program.Functions.Add(func);

        var diags = IrVerifier.Verify(program);
        Assert.That(diags.HasErrors, Is.True);
        Assert.That(diags.Any(d => d.Code == "IR005"), Is.True);
    }
}
