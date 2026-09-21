using System.Numerics;
using AstraGraph.Core;
using AstraGraph.VM;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class VmTests
{
    [Test]
    public void AstraValue_TaggedUnionBehaviors()
    {
        var vInt = AstraValue.FromInt64(42);
        Assert.That(vInt.Type, Is.EqualTo(AstraValueType.Int64));
        Assert.That(vInt.AsInt64(), Is.EqualTo(42));
        Assert.That(vInt.AsInt32(), Is.EqualTo(42));

        var vDouble = AstraValue.FromDouble(3.14159);
        Assert.That(vDouble.Type, Is.EqualTo(AstraValueType.Double));
        Assert.That(vDouble.AsDouble(), Is.EqualTo(3.14159).Within(0.0001));

        var vEntity = AstraValue.FromEntityUid(100500);
        Assert.That(vEntity.Type, Is.EqualTo(AstraValueType.EntityUid));
        Assert.That(vEntity.AsEntityUid(), Is.EqualTo(100500));

        var vVec = AstraValue.FromVector2(new Vector2(10.5f, -20.5f));
        Assert.That(vVec.Type, Is.EqualTo(AstraValueType.Vector2));
        Assert.That(vVec.AsVector2(), Is.EqualTo(new Vector2(10.5f, -20.5f)));

        var vStr = AstraValue.FromString("Hello Astra");
        Assert.That(vStr.Type, Is.EqualTo(AstraValueType.Object));
        Assert.That(vStr.AsString(), Is.EqualTo("Hello Astra"));
    }

    [Test]
    public void AstraVm_ExecutesBranchingLogic_ComputesCorrectResult()
    {
        // Compute: if (input > 10) return input * 2 else return input + 5
        var pool = new ConstantPool();
        var c10 = pool.GetOrAddInt64(10);
        var c2 = pool.GetOrAddInt64(2);
        var c5 = pool.GetOrAddInt64(5);
        var fnName = pool.GetOrAddString("Calculate");

        // Registers: %r0 = input (arg), %r1 = temp, %r2 = cond, %r3 = result
        var instructions = new List<BytecodeInstruction>
        {
            // L0000: %r1 = LoadConst #c10 (10)
            new((byte)IrOpCode.LoadConst, 1, c10, 0, 0),
            // L0001: %r2 = CmpGt %r0, %r1
            new((byte)IrOpCode.CmpGt, 2, 0, 1, 0),
            // L0002: BranchIf %r2 -> L0003 (then), else L0006 (else)
            new((byte)IrOpCode.BranchIf, BytecodeInstruction.NoRegister, 2, 3, 6),
            // L0003 (then): %r1 = LoadConst #c2 (2)
            new((byte)IrOpCode.LoadConst, 1, c2, 0, 0),
            // L0004: %r3 = Mul %r0, %r1
            new((byte)IrOpCode.Mul, 3, 0, 1, 0),
            // L0005: Return %r3
            new((byte)IrOpCode.Return, BytecodeInstruction.NoRegister, 3, 0, 0),
            // L0006 (else): %r1 = LoadConst #c5 (5)
            new((byte)IrOpCode.LoadConst, 1, c5, 0, 0),
            // L0007: %r3 = Add %r0, %r1
            new((byte)IrOpCode.Add, 3, 0, 1, 0),
            // L0008: Return %r3
            new((byte)IrOpCode.Return, BytecodeInstruction.NoRegister, 3, 0, 0)
        };

        var func = new BytecodeFunction(fnName, registerCount: 4, parameterCount: 1, instructions);
        var program = new BytecodeProgram(GraphId.New(), RevisionId.New(), "hash", pool);
        program.EntryPoints.Add(func);

        var vm = new AstraVm();

        // Test branch 1: input = 15 (> 10) -> 15 * 2 = 30
        var res1 = vm.Execute(program, func, [AstraValue.FromInt64(15)]);
        Assert.That(res1.IsSuccess, Is.True);
        Assert.That(res1.ReturnValue.AsInt64(), Is.EqualTo(30));

        // Test branch 2: input = 4 (<= 10) -> 4 + 5 = 9
        var res2 = vm.Execute(program, func, [AstraValue.FromInt64(4)]);
        Assert.That(res2.IsSuccess, Is.True);
        Assert.That(res2.ReturnValue.AsInt64(), Is.EqualTo(9));
    }

    [Test]
    public void AstraVm_ExecutionBudget_TerminatesInfiniteLoop()
    {
        // Create an infinite loop: Jump -> L0000
        var pool = new ConstantPool();
        var fnName = pool.GetOrAddString("InfiniteLoop");

        var instructions = new List<BytecodeInstruction>
        {
            new((byte)IrOpCode.Jump, BytecodeInstruction.NoRegister, 0, 0, 0)
        };

        var func = new BytecodeFunction(fnName, 1, 0, instructions);
        var program = new BytecodeProgram(GraphId.New(), RevisionId.New(), "hash", pool);

        var vm = new AstraVm();
        var budget = new ExecutionBudget { MaxInstructions = 500 };

        var result = vm.Execute(program, func, budget: budget);

        Assert.That(result.Status, Is.EqualTo(VmExecutionStatus.ExceededBudget));
        Assert.That(result.Exception, Is.TypeOf<ExecutionBudgetExceededException>());
        Assert.That(result.InstructionsExecuted, Is.GreaterThan(500));
    }

    [Test]
    public void AstraVm_YieldAndResume_PreservesRegistersAndCompletes()
    {
        var pool = new ConstantPool();
        var resumeGuid = Guid.NewGuid();
        var gIdx = pool.GetOrAddGuid(resumeGuid);
        var cVal = pool.GetOrAddInt64(100);
        var fnName = pool.GetOrAddString("LatentRoutine");

        // L0000: %r0 = LoadConst #cVal (100)
        // L0001: YieldContinuation Delay, resumeGuid, nextIP: 2
        // L0002: %r1 = Add %r0, %r0 (200)
        // L0003: Return %r1
        var instructions = new List<BytecodeInstruction>
        {
            new((byte)IrOpCode.LoadConst, 0, cVal, 0, 0),
            new((byte)IrOpCode.YieldContinuation, BytecodeInstruction.NoRegister, (int)ContinuationKind.Delay, gIdx, 2),
            new((byte)IrOpCode.Add, 1, 0, 0, 0),
            new((byte)IrOpCode.Return, BytecodeInstruction.NoRegister, 1, 0, 0)
        };

        var func = new BytecodeFunction(fnName, 2, 0, instructions);
        var program = new BytecodeProgram(GraphId.New(), RevisionId.New(), "hash", pool);

        var vm = new AstraVm();

        // 1st step: runs to YieldContinuation
        var step1 = vm.Execute(program, func);
        Assert.That(step1.IsYielded, Is.True);
        Assert.That(step1.YieldState, Is.Not.Null);
        Assert.That(step1.YieldState!.Kind, Is.EqualTo(ContinuationKind.Delay));
        Assert.That(step1.YieldState.ResumePointId, Is.EqualTo(resumeGuid));
        Assert.That(step1.YieldState.NextInstructionPointer, Is.EqualTo(2));

        // 2nd step: resume from IP=2 with captured registers
        var capturedRegisters = step1.YieldState.Arguments.ToArray();
        var step2 = vm.Execute(program, func, initialRegisters: capturedRegisters, startIp: step1.YieldState.NextInstructionPointer);

        Assert.That(step2.IsSuccess, Is.True);
        Assert.That(step2.ReturnValue.AsInt64(), Is.EqualTo(200));
    }

    [Test]
    public void AstraVm_NativeMethodCall_InteractsWithHostServices()
    {
        var pool = new ConstantPool();
        var mIdx = pool.GetOrAddString("Math.CustomSquare");
        var c5 = pool.GetOrAddInt64(5);
        var fnName = pool.GetOrAddString("NativeCaller");

        var instructions = new List<BytecodeInstruction>
        {
            new((byte)IrOpCode.LoadConst, 0, c5, 0, 0),
            new((byte)IrOpCode.CallNative, 1, mIdx, 1, 0),
            new((byte)IrOpCode.Return, BytecodeInstruction.NoRegister, 1, 0, 0)
        };

        var func = new BytecodeFunction(fnName, 2, 0, instructions);
        var program = new BytecodeProgram(GraphId.New(), RevisionId.New(), "hash", pool);

        var services = new DefaultVmHostServices();
        services.RegisterNativeMethod("Math.CustomSquare", args =>
        {
            // Square 5 -> 25
            return AstraValue.FromInt64(25);
        });

        var vm = new AstraVm();
        var result = vm.Execute(program, func, hostServices: services);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.ReturnValue.AsInt64(), Is.EqualTo(25));
    }
}
