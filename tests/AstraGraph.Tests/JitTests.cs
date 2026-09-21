using System;
using System.Runtime.CompilerServices;
using AstraGraph.Core;
using AstraGraph.JIT;
using AstraGraph.VM;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class JitTests
{
    [Test]
    public void RoslynCompiler_CompilesAndExecutesArithmeticIr()
    {
        var graphId = GraphId.New();
        var irProgram = new IrProgram(graphId, "ArithmeticTest", GraphKind.Function, GraphSide.Server);

        var entryBlock = new IrBasicBlock(0, "entry");
        var func = new IrFunction("Calculate", [], PrimitiveType.Int64, entryBlock);
        irProgram.Functions.Add(func);

        // r0 = 10, r1 = 20, r2 = r0 + r1 (30), r3 = 2, r4 = r2 * r3 (60), return r4
        var r0 = new IrRegister(0, PrimitiveType.Int64);
        var r1 = new IrRegister(1, PrimitiveType.Int64);
        var r2 = new IrRegister(2, PrimitiveType.Int64);
        var r3 = new IrRegister(3, PrimitiveType.Int64);
        var r4 = new IrRegister(4, PrimitiveType.Int64);

        entryBlock.AddInstruction(new IrInstruction(IrOpCode.LoadConst, r0, [new IrConstant(10L, PrimitiveType.Int64)]));
        entryBlock.AddInstruction(new IrInstruction(IrOpCode.LoadConst, r1, [new IrConstant(20L, PrimitiveType.Int64)]));
        entryBlock.AddInstruction(new IrInstruction(IrOpCode.Add, r2, [r0, r1]));
        entryBlock.AddInstruction(new IrInstruction(IrOpCode.LoadConst, r3, [new IrConstant(2L, PrimitiveType.Int64)]));
        entryBlock.AddInstruction(new IrInstruction(IrOpCode.Mul, r4, [r2, r3]));
        entryBlock.SetTerminator(new IrInstruction(IrOpCode.Return, null, [r4]));
        func.RegisterCount = 5;

        using var jitProgram = RoslynCompiler.Compile(irProgram);
        var services = new DefaultVmHostServices();
        var result = jitProgram.Execute("Calculate", null, services);

        Assert.That(result.Type, Is.EqualTo(AstraValueType.Int64));
        Assert.That(result.AsInt64(), Is.EqualTo(60L));
    }

    [Test]
    public void JitVmParity_ProducesIdenticalResults()
    {
        var graphId = GraphId.New();
        var irProgram = new IrProgram(graphId, "ParityTest", GraphKind.Function, GraphSide.Server);

        var entryBlock = new IrBasicBlock(0, "entry");
        var thenBlock = new IrBasicBlock(1, "then");
        var elseBlock = new IrBasicBlock(2, "else");
        var mergeBlock = new IrBasicBlock(3, "merge");

        var func = new IrFunction("BranchLogic", [], PrimitiveType.Int64, entryBlock);
        irProgram.Functions.Add(func);
        func.Blocks.Add(thenBlock);
        func.Blocks.Add(elseBlock);
        func.Blocks.Add(mergeBlock);

        // r0: input (passed via initialRegisters), r1: threshold (50)
        // r2: r0 > r1
        // BranchIf r2 -> then, else
        // then: r3 = r0 * 2, goto merge
        // else: r3 = r0 - 10, goto merge
        // merge: return r3
        var r0 = new IrRegister(0, PrimitiveType.Int64);
        var r1 = new IrRegister(1, PrimitiveType.Int64);
        var r2 = new IrRegister(2, PrimitiveType.Bool);
        var r3 = new IrRegister(3, PrimitiveType.Int64);

        entryBlock.AddInstruction(new IrInstruction(IrOpCode.LoadConst, r1, [new IrConstant(50L, PrimitiveType.Int64)]));
        entryBlock.AddInstruction(new IrInstruction(IrOpCode.CmpGt, r2, [r0, r1]));
        entryBlock.SetTerminator(new IrInstruction(IrOpCode.BranchIf, null, [r2]), thenBlock, elseBlock);

        var rTwo = new IrRegister(4, PrimitiveType.Int64);
        thenBlock.AddInstruction(new IrInstruction(IrOpCode.LoadConst, rTwo, [new IrConstant(2L, PrimitiveType.Int64)]));
        thenBlock.AddInstruction(new IrInstruction(IrOpCode.Mul, r3, [r0, rTwo]));
        thenBlock.SetTerminator(new IrInstruction(IrOpCode.Jump), mergeBlock);

        var rTen = new IrRegister(5, PrimitiveType.Int64);
        elseBlock.AddInstruction(new IrInstruction(IrOpCode.LoadConst, rTen, [new IrConstant(10L, PrimitiveType.Int64)]));
        elseBlock.AddInstruction(new IrInstruction(IrOpCode.Sub, r3, [r0, rTen]));
        elseBlock.SetTerminator(new IrInstruction(IrOpCode.Jump), mergeBlock);

        mergeBlock.SetTerminator(new IrInstruction(IrOpCode.Return, null, [r3]));
        func.RegisterCount = 6;

        // Compile to JIT
        using var jitProg = RoslynCompiler.Compile(irProgram);

        // Compile to Bytecode for VM
        var bytecodeProg = IrToBytecodeCompiler.Compile(irProgram, RevisionId.New(), "parity_test");
        var vm = new AstraVm();
        var services = new DefaultVmHostServices();

        long[] testInputs = [10L, 50L, 75L, 100L, -5L];
        foreach (var input in testInputs)
        {
            var initialRegs = new[] { AstraValue.FromInt64(input), AstraValue.Null, AstraValue.Null, AstraValue.Null, AstraValue.Null, AstraValue.Null };

            var jitResult = jitProg.Execute("BranchLogic", initialRegs, services);
            var vmResult = vm.Execute(bytecodeProg, bytecodeProg.Functions[0], initialRegs, hostServices: services);

            Assert.That(vmResult.ReturnValue.AsInt64(), Is.EqualTo(jitResult.AsInt64()), $"Parity mismatch for input {input}");
        }
    }

    [Test]
    public void CollectibleLoadContext_UnloadsCleanlyWithoutMemoryLeaks()
    {
        var weakRef = ExecuteAndUnload();

        for (var i = 0; i < 5 && weakRef.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.That(weakRef.IsAlive, Is.False, "Collectible AssemblyLoadContext was not fully unloaded; potential memory leak.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ExecuteAndUnload()
    {
        var irProgram = new IrProgram(GraphId.New(), "UnloadTest", GraphKind.Function, GraphSide.Server);
        var entryBlock = new IrBasicBlock(0, "entry");
        var func = new IrFunction("Test", [], PrimitiveType.Int64, entryBlock);
        irProgram.Functions.Add(func);

        var r0 = new IrRegister(0, PrimitiveType.Int64);
        entryBlock.AddInstruction(new IrInstruction(IrOpCode.LoadConst, r0, [new IrConstant(12345L, PrimitiveType.Int64)]));
        entryBlock.SetTerminator(new IrInstruction(IrOpCode.Return, null, [r0]));
        func.RegisterCount = 1;

        var jitProgram = RoslynCompiler.Compile(irProgram);
        var services = new DefaultVmHostServices();
        var res = jitProgram.Execute("Test", null, services);
        Assert.That(res.AsInt64(), Is.EqualTo(12345L));

        var alcType = jitProgram.GetType().GetField("_loadContext", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(jitProgram);
        var weakRef = new WeakReference(alcType);

        jitProgram.Dispose();
        return weakRef;
    }
}
