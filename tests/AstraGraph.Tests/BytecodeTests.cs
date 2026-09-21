using AstraGraph.Core;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class BytecodeTests
{
    [Test]
    public void ConstantPool_DeduplicationAndIndexing()
    {
        var pool = new ConstantPool();
        var s1 = pool.GetOrAddString("Hello");
        var s2 = pool.GetOrAddString("Hello");
        var s3 = pool.GetOrAddString("World");

        Assert.That(s2, Is.EqualTo(s1));
        Assert.That(s3, Is.Not.EqualTo(s1));
        Assert.That(pool.Count, Is.EqualTo(2));

        var i1 = pool.GetOrAddInt64(42);
        var i2 = pool.GetOrAddInt64(42);
        Assert.That(i2, Is.EqualTo(i1));
        Assert.That(pool.Count, Is.EqualTo(3));
    }

    [Test]
    public void Bytecode_CompileSerializeAndDisassembleRoundtrip()
    {
        var graphId = GraphId.New();
        var varId = SymbolId.New();
        var nodeId = NodeId.New();

        var heatVar = new AstVariableDeclaration(varId, "CurrentHeat", PrimitiveType.Float32);
        var readHeat = new AstVariableReadExpression(varId, "CurrentHeat", PrimitiveType.Float32, nodeId);
        var threshold = new AstLiteralExpression(50.0f, PrimitiveType.Float32, nodeId);
        var condition = new AstBinaryExpression(AstBinaryOperator.GreaterThan, readHeat, threshold, PrimitiveType.Bool, nodeId);

        var assignZero = new AstVariableAssignStatement(varId, "CurrentHeat", new AstLiteralExpression(0.0f, PrimitiveType.Float32), nodeId);
        var trueBlock = new AstBlock([assignZero]);
        var falseBlock = new AstBlock([new AstReturnStatement()]);

        var branch = new AstBranchStatement(condition, trueBlock, falseBlock, nodeId);
        var entryPoint = new AstEntryPointStatement("OnCooldown", [], new AstBlock([branch]), nodeId);

        var astProgram = new AstProgram(
            graphId,
            "CooldownProgram",
            GraphKind.System,
            GraphSide.Server,
            [heatVar],
            [entryPoint],
            []);

        var irProgram = AstToIrCompiler.Compile(astProgram);
        var bytecodeProgram = IrToBytecodeCompiler.Compile(irProgram);

        Assert.That(bytecodeProgram.EntryPoints.Count, Is.EqualTo(1));
        var entryFunc = bytecodeProgram.EntryPoints[0];
        Assert.That(entryFunc.Instructions.Count, Is.GreaterThan(0));

        // Test Disassembler
        var disassembly = BytecodeDisassembler.Disassemble(bytecodeProgram);
        Assert.That(disassembly, Is.Not.Null.And.Not.Empty);
        Assert.That(disassembly, Contains.Substring(".entrypoint OnCooldown"));
        Assert.That(disassembly, Contains.Substring("BranchIf"));

        // Test Binary Serialization Roundtrip
        var bytes = BytecodeSerializer.SerializeToBytes(bytecodeProgram);
        Assert.That(bytes.Length, Is.GreaterThan(0));

        var deserialized = BytecodeSerializer.DeserializeFromBytes(bytes);
        Assert.That(deserialized.Id, Is.EqualTo(bytecodeProgram.Id));
        Assert.That(deserialized.Revision, Is.EqualTo(bytecodeProgram.Revision));
        Assert.That(deserialized.SemanticHash, Is.EqualTo(bytecodeProgram.SemanticHash));
        Assert.That(deserialized.Constants.Count, Is.EqualTo(bytecodeProgram.Constants.Count));
        Assert.That(deserialized.EntryPoints.Count, Is.EqualTo(1));

        var desEntry = deserialized.EntryPoints[0];
        Assert.That(desEntry.Instructions.Count, Is.EqualTo(entryFunc.Instructions.Count));
        for (var i = 0; i < entryFunc.Instructions.Count; i++)
        {
            Assert.That(desEntry.Instructions[i].OpCode, Is.EqualTo(entryFunc.Instructions[i].OpCode));
            Assert.That(desEntry.Instructions[i].DestRegister, Is.EqualTo(entryFunc.Instructions[i].DestRegister));
            Assert.That(desEntry.Instructions[i].Op1, Is.EqualTo(entryFunc.Instructions[i].Op1));
            Assert.That(desEntry.Instructions[i].Op2, Is.EqualTo(entryFunc.Instructions[i].Op2));
            Assert.That(desEntry.Instructions[i].Extra, Is.EqualTo(entryFunc.Instructions[i].Extra));
        }
    }
}
