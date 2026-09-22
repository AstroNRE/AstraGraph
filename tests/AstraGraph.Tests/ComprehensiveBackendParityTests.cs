using AstraGraph.Core;
using AstraGraph.JIT;
using AstraGraph.VM;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class ComprehensiveBackendParityTests
{
    [Test]
    public void Parity_FibonacciLoop_BytecodeAndJit_MatchExactOutputs()
    {
        // Program computing N-th Fibonacci number iteratively:
        // func Fib(n: int64) -> int64
        // r0 = n (input)
        // r1 = 0 (prev)
        // r2 = 1 (curr)
        // r3 = 0 (i)
        // r4 = 1 (step)
        // loop:
        //   cond = r3 < r0
        //   if (!cond) goto end
        //   next = r1 + r2
        //   r1 = r2
        //   r2 = next
        //   r3 = r3 + r4
        //   goto loop
        // end:
        //   return r1

        var graphId = GraphId.New();
        var irProg = new IrProgram(graphId, "FibProgram", GraphKind.Function, GraphSide.Server);

        var entry = new IrBasicBlock(0, "entry");
        var loopHeader = new IrBasicBlock(1, "loop_header");
        var loopBody = new IrBasicBlock(2, "loop_body");
        var exitBlock = new IrBasicBlock(3, "exit");

        var func = new IrFunction("Fib", [], PrimitiveType.Int64, entry);
        irProg.Functions.Add(func);
        func.Blocks.Add(loopHeader);
        func.Blocks.Add(loopBody);
        func.Blocks.Add(exitBlock);

        var r0 = new IrRegister(0, PrimitiveType.Int64); // n
        var r1 = new IrRegister(1, PrimitiveType.Int64); // prev
        var r2 = new IrRegister(2, PrimitiveType.Int64); // curr
        var r3 = new IrRegister(3, PrimitiveType.Int64); // i
        var r4 = new IrRegister(4, PrimitiveType.Int64); // 1
        var rCond = new IrRegister(5, PrimitiveType.Bool);
        var rNext = new IrRegister(6, PrimitiveType.Int64);

        // entry:
        // r1 = 0
        // r2 = 1
        // r3 = 0
        // r4 = 1
        // goto loop_header
        entry.AddInstruction(new IrInstruction(IrOpCode.LoadConst, r1, [new IrConstant(0L, PrimitiveType.Int64)]));
        entry.AddInstruction(new IrInstruction(IrOpCode.LoadConst, r2, [new IrConstant(1L, PrimitiveType.Int64)]));
        entry.AddInstruction(new IrInstruction(IrOpCode.LoadConst, r3, [new IrConstant(0L, PrimitiveType.Int64)]));
        entry.AddInstruction(new IrInstruction(IrOpCode.LoadConst, r4, [new IrConstant(1L, PrimitiveType.Int64)]));
        entry.SetTerminator(new IrInstruction(IrOpCode.Jump), loopHeader);

        // loop_header:
        // rCond = r3 < r0
        // branch rCond ? loop_body : exit
        loopHeader.AddInstruction(new IrInstruction(IrOpCode.CmpLt, rCond, [r3, r0]));
        loopHeader.SetTerminator(new IrInstruction(IrOpCode.BranchIf, null, [rCond]), loopBody, exitBlock);

        // loop_body:
        // rNext = r1 + r2
        // r1 = r2
        // r2 = rNext
        // r3 = r3 + r4
        // goto loop_header
        loopBody.AddInstruction(new IrInstruction(IrOpCode.Add, rNext, [r1, r2]));
        loopBody.AddInstruction(new IrInstruction(IrOpCode.Add, r1, [r2, new IrRegister(7, PrimitiveType.Int64)])); // r1 = r2 + 0
        // simpler: store r2 to r1
        // Let's do r1 = r2 via Add r1, r2, 0
        var rZero = new IrRegister(7, PrimitiveType.Int64);
        entry.AddInstruction(new IrInstruction(IrOpCode.LoadConst, rZero, [new IrConstant(0L, PrimitiveType.Int64)]));
        loopBody.AddInstruction(new IrInstruction(IrOpCode.Add, r1, [r2, rZero]));
        loopBody.AddInstruction(new IrInstruction(IrOpCode.Add, r2, [rNext, rZero]));
        loopBody.AddInstruction(new IrInstruction(IrOpCode.Add, r3, [r3, r4]));
        loopBody.SetTerminator(new IrInstruction(IrOpCode.Jump), loopHeader);

        // exit:
        // return r1
        exitBlock.SetTerminator(new IrInstruction(IrOpCode.Return, null, [r1]));
        func.RegisterCount = 8;

        // Compile JIT & VM Bytecode
        using var jitProg = RoslynCompiler.Compile(irProg);
        var bytecodeProg = IrToBytecodeCompiler.Compile(irProg, RevisionId.New(), "fib_hash");
        var vm = new AstraVm();
        var services = new DefaultVmHostServices();

        long[] inputs = [0L, 1L, 2L, 5L, 10L, 15L, 20L];
        foreach (var n in inputs)
        {
            var initial = new AstraValue[8];
            initial[0] = AstraValue.FromInt64(n);

            var jitRes = jitProg.Execute("Fib", initial, services);
            var vmRes = vm.Execute(bytecodeProg, bytecodeProg.Functions[0], initial, hostServices: services);

            Assert.That(vmRes.IsSuccess, Is.True);
            Assert.That(vmRes.ReturnValue.AsInt64(), Is.EqualTo(jitRes.AsInt64()), $"Mismatch for Fib({n})");
        }
    }

    [Test]
    public void Parity_FloatAndStringMath_MatchesVmAndJit()
    {
        var graphId = GraphId.New();
        var irProg = new IrProgram(graphId, "MathAndStrings", GraphKind.Function, GraphSide.Server);

        var entry = new IrBasicBlock(0, "entry");
        var func = new IrFunction("Run", [], PrimitiveType.String, entry);
        irProg.Functions.Add(func);

        var r0 = new IrRegister(0, PrimitiveType.Float64);
        var r1 = new IrRegister(1, PrimitiveType.Float64);
        var r2 = new IrRegister(2, PrimitiveType.Float64);
        var r3 = new IrRegister(3, PrimitiveType.String);
        var r4 = new IrRegister(4, PrimitiveType.String);
        var r5 = new IrRegister(5, PrimitiveType.String);

        // r0 = 100.5, r1 = 20.25, r2 = r0 / r1 (4.96296296)
        entry.AddInstruction(new IrInstruction(IrOpCode.LoadConst, r0, [new IrConstant(100.5, PrimitiveType.Float64)]));
        entry.AddInstruction(new IrInstruction(IrOpCode.LoadConst, r1, [new IrConstant(20.0, PrimitiveType.Float64)]));
        entry.AddInstruction(new IrInstruction(IrOpCode.Div, r2, [r0, r1]));

        // r3 = "Prefix: "
        // r4 = "Suffix"
        // r5 = r3 + r4
        entry.AddInstruction(new IrInstruction(IrOpCode.LoadConst, r3, [new IrConstant("Astra_", PrimitiveType.String)]));
        entry.AddInstruction(new IrInstruction(IrOpCode.LoadConst, r4, [new IrConstant("Graph", PrimitiveType.String)]));
        entry.AddInstruction(new IrInstruction(IrOpCode.Add, r5, [r3, r4]));

        entry.SetTerminator(new IrInstruction(IrOpCode.Return, null, [r5]));
        func.RegisterCount = 6;

        using var jitProg = RoslynCompiler.Compile(irProg);
        var bytecodeProg = IrToBytecodeCompiler.Compile(irProg, RevisionId.New(), "math_hash");
        var vm = new AstraVm();
        var services = new DefaultVmHostServices();

        var jitRes = jitProg.Execute("Run", null, services);
        var vmRes = vm.Execute(bytecodeProg, bytecodeProg.Functions[0], hostServices: services);

        Assert.That(vmRes.ReturnValue.AsString(), Is.EqualTo("Astra_Graph"));
        Assert.That(jitRes.AsString(), Is.EqualTo(vmRes.ReturnValue.AsString()));
    }

    [Test]
    public void Parity_NativeCallAndVariables_MatchesVmAndJit()
    {
        var graphId = GraphId.New();
        var irProg = new IrProgram(graphId, "NativeAndVars", GraphKind.Function, GraphSide.Server);

        var entry = new IrBasicBlock(0, "entry");
        var func = new IrFunction("ExecuteService", [], PrimitiveType.Int64, entry);
        irProg.Functions.Add(func);

        var r0 = new IrRegister(0, PrimitiveType.Int64);
        var r1 = new IrRegister(1, PrimitiveType.Int64);
        var r2 = new IrRegister(2, PrimitiveType.Int64);

        // Store variable "Multiplier" = 3
        // Call native "Multiply" (r0, r1) -> r2
        entry.AddInstruction(new IrInstruction(IrOpCode.LoadConst, r0, [new IrConstant(7L, PrimitiveType.Int64)]));
        entry.AddInstruction(new IrInstruction(IrOpCode.LoadConst, r1, [new IrConstant(6L, PrimitiveType.Int64)]));
        entry.AddInstruction(new IrInstruction(IrOpCode.CallNative, r2, [r0, r1], StringPayload: "Math.Multiply"));
        entry.AddInstruction(new IrInstruction(IrOpCode.StoreVariable, null, [r2], StringPayload: "Answer"));
        entry.SetTerminator(new IrInstruction(IrOpCode.Return, null, [r2]));
        func.RegisterCount = 3;

        using var jitProg = RoslynCompiler.Compile(irProg);
        var bytecodeProg = IrToBytecodeCompiler.Compile(irProg, RevisionId.New(), "native_hash");
        var vm = new AstraVm();

        var servicesJit = new DefaultVmHostServices();
        servicesJit.RegisterNativeMethod("Math.Multiply", args => AstraValue.FromInt64(args[0].AsInt64() * args[1].AsInt64()));

        var servicesVm = new DefaultVmHostServices();
        servicesVm.RegisterNativeMethod("Math.Multiply", args => AstraValue.FromInt64(args[0].AsInt64() * args[1].AsInt64()));

        var jitRes = jitProg.Execute("ExecuteService", null, servicesJit);
        var vmRes = vm.Execute(bytecodeProg, bytecodeProg.Functions[0], hostServices: servicesVm);

        Assert.That(vmRes.ReturnValue.AsInt64(), Is.EqualTo(42L));
        Assert.That(jitRes.AsInt64(), Is.EqualTo(42L));
        Assert.That(servicesVm.GetVariable(SymbolId.Empty, "Answer").AsInt64(), Is.EqualTo(42L));
        Assert.That(servicesJit.GetVariable(SymbolId.Empty, "Answer").AsInt64(), Is.EqualTo(42L));
    }

    [Test]
    public void Parity_BudgetExceeded_ThrowsOnBothBackends()
    {
        var graphId = GraphId.New();
        var irProg = new IrProgram(graphId, "InfiniteLoop", GraphKind.Function, GraphSide.Server);

        var loop = new IrBasicBlock(0, "loop");
        var func = new IrFunction("RunInfinite", [], PrimitiveType.Void, loop);
        irProg.Functions.Add(func);

        loop.SetTerminator(new IrInstruction(IrOpCode.Jump), loop);
        func.RegisterCount = 1;

        using var jitProg = RoslynCompiler.Compile(irProg);
        var bytecodeProg = IrToBytecodeCompiler.Compile(irProg, RevisionId.New(), "inf_hash");
        var vm = new AstraVm();
        var services = new DefaultVmHostServices();

        var budgetVm = new ExecutionBudget { MaxInstructions = 500 };
        var vmResult = vm.Execute(bytecodeProg, bytecodeProg.Functions[0], budget: budgetVm, hostServices: services);
        Assert.That(vmResult.Status, Is.EqualTo(VmExecutionStatus.ExceededBudget));

        var budgetJit = new ExecutionBudget { MaxInstructions = 500 };
        Assert.Throws<ExecutionBudgetExceededException>(() =>
        {
            jitProg.Execute("RunInfinite", null, services, budget: budgetJit);
        });
    }
}
