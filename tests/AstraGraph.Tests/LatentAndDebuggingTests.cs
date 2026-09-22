using AstraGraph.Core;
using AstraGraph.Runtime;
using AstraGraph.Runtime.Debugging;
using AstraGraph.VM;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class LatentAndDebuggingTests
{
    [Test]
    public void LatentContinuation_MultiStepDelay_PreservesStateAcrossFrames()
    {
        var pool = new ConstantPool();
        var c10 = pool.GetOrAddInt64(10);
        var c5 = pool.GetOrAddInt64(5);
        var resumeGuid1 = Guid.NewGuid();
        var resumeGuid2 = Guid.NewGuid();
        var gIdx1 = pool.GetOrAddGuid(resumeGuid1);
        var gIdx2 = pool.GetOrAddGuid(resumeGuid2);
        var fnName = pool.GetOrAddString("MultiStepProcess");

        // L0000: %r0 = LoadConst 10
        // L0001: %r1 = LoadConst 5
        // L0002: %r2 = Add %r0, %r1 (15)
        // L0003: YieldContinuation Delay, guid1, nextIp: 4
        // L0004: %r2 = Add %r2, %r1 (20)
        // L0005: YieldContinuation Delay, guid2, nextIp: 6
        // L0006: %r2 = Add %r2, %r0 (30)
        // L0007: Return %r2
        var instructions = new List<BytecodeInstruction>
        {
            new((byte)IrOpCode.LoadConst, 0, c10, 0, 0),
            new((byte)IrOpCode.LoadConst, 1, c5, 0, 0),
            new((byte)IrOpCode.Add, 2, 0, 1, 0),
            new((byte)IrOpCode.YieldContinuation, BytecodeInstruction.NoRegister, (int)ContinuationKind.Delay, gIdx1, 4),
            new((byte)IrOpCode.Add, 2, 2, 1, 0),
            new((byte)IrOpCode.YieldContinuation, BytecodeInstruction.NoRegister, (int)ContinuationKind.Delay, gIdx2, 6),
            new((byte)IrOpCode.Add, 2, 2, 0, 0),
            new((byte)IrOpCode.Return, BytecodeInstruction.NoRegister, 2, 0, 0)
        };

        var func = new BytecodeFunction(fnName, 3, 0, instructions);
        var program = new BytecodeProgram(GraphId.New(), RevisionId.New(), "hash", pool);

        var vm = new AstraVm();
        var scheduler = new ContinuationScheduler(vm);

        // Run initial slice up to first yield
        var initResult = vm.Execute(program, func);
        Assert.That(initResult.IsYielded, Is.True);
        Assert.That(initResult.YieldState, Is.Not.Null);
        Assert.That(initResult.YieldState!.NextInstructionPointer, Is.EqualTo(4));
        Assert.That(initResult.YieldState.Arguments[2].AsInt64(), Is.EqualTo(15));

        var frame = new ContinuationFrame(
            program.Id,
            program,
            func,
            initResult.YieldState.Arguments.ToArray(),
            initResult.YieldState.ResumePointId,
            initResult.YieldState.NextInstructionPointer,
            ContinuationCondition.DelaySeconds(currentTimeSeconds: 0.0, delaySeconds: 1.0));

        scheduler.Schedule(frame);
        Assert.That(scheduler.ActiveCount, Is.EqualTo(1));

        // Advance to 0.5s -> should not resume
        scheduler.Update(currentTimeSeconds: 0.5, currentTick: 5);
        Assert.That(scheduler.ActiveCount, Is.EqualTo(1));

        // Advance to 1.0s -> resumes, executes L0004 (%r2 = 15 + 5 = 20), yields at L0005
        scheduler.Update(currentTimeSeconds: 1.0, currentTick: 10);
        Assert.That(scheduler.ActiveCount, Is.EqualTo(1));
        Assert.That(frame.InstructionPointer, Is.EqualTo(6));
        Assert.That(frame.CapturedRegisters[2].AsInt64(), Is.EqualTo(20));

        // Advance by another 1.0s (t = 2.0s) -> resumes, executes L0006 (%r2 = 20 + 10 = 30), completes!
        scheduler.Update(currentTimeSeconds: 2.0, currentTick: 20);
        Assert.That(scheduler.ActiveCount, Is.EqualTo(0));
    }

    [Test]
    public void LatentContinuation_AwaitEvent_InjectsPayloadAndResumes()
    {
        var pool = new ConstantPool();
        var fnName = pool.GetOrAddString("WaitForInteraction");
        var c100 = pool.GetOrAddInt64(100);

        // L0000: %r1 = Add %r0, #c100 (where %r0 is injected event payload)
        // L0001: Return %r1
        var instructions = new List<BytecodeInstruction>
        {
            new((byte)IrOpCode.Add, 1, 0, 0, 0),
            new((byte)IrOpCode.Return, BytecodeInstruction.NoRegister, 1, 0, 0)
        };

        var func = new BytecodeFunction(fnName, 2, 0, instructions);
        var program = new BytecodeProgram(GraphId.New(), RevisionId.New(), "hash", pool);

        var scheduler = new ContinuationScheduler();
        var frame = new ContinuationFrame(
            program.Id,
            program,
            func,
            new AstraValue[2],
            Guid.NewGuid(),
            0,
            ContinuationCondition.AwaitEvent("DoorOpened"));

        scheduler.Schedule(frame);
        Assert.That(scheduler.ActiveCount, Is.EqualTo(1));

        // Time updates should not wake up event continuation
        scheduler.Update(100.0, 1000);
        Assert.That(scheduler.ActiveCount, Is.EqualTo(1));

        // Fire unrelated event -> no wake
        scheduler.WakeByEvent("DoorClosed", AstraValue.FromInt64(42));
        Assert.That(scheduler.ActiveCount, Is.EqualTo(1));

        // Fire matching event with payload 25
        scheduler.WakeByEvent("DoorOpened", AstraValue.FromInt64(25));
        Assert.That(scheduler.ActiveCount, Is.EqualTo(0));
        Assert.That(frame.CapturedRegisters[0].AsInt64(), Is.EqualTo(25));
    }

    [Test]
    public void LatentContinuation_HostUnregister_CancelsByGraph()
    {
        var host = new AstraGraphHost();
        var pool = new ConstantPool();
        var func = new BytecodeFunction(0, 1, 0, [new((byte)IrOpCode.Return, BytecodeInstruction.NoRegister, -1, 0, 0)]);
        var graphId = GraphId.New();
        var program = new BytecodeProgram(graphId, RevisionId.New(), "hash", pool);

        host.RegisterProgram(program);

        var frame = new ContinuationFrame(
            graphId,
            program,
            func,
            new AstraValue[1],
            Guid.NewGuid(),
            0,
            ContinuationCondition.DelaySeconds(0, 10.0));

        host.Continuations.Schedule(frame);
        Assert.That(host.Continuations.ActiveCount, Is.EqualTo(1));

        // Unregister graph -> should cancel all continuations belonging to it
        host.UnregisterProgram(graphId);
        Assert.That(host.Continuations.ActiveCount, Is.EqualTo(0));
        Assert.That(frame.IsCancelled, Is.True);
    }

    [Test]
    public void Debugger_Breakpoint_SuspendsExecutionAndCapturesRegisters()
    {
        var pool = new ConstantPool();
        var c1 = pool.GetOrAddInt64(10);
        var c2 = pool.GetOrAddInt64(20);
        var bpNode = NodeId.New();

        // L0000: %r0 = 10
        // L0001: %r1 = 20
        // L0002: %r2 = %r0 + %r1 (30)   <-- Breakpoint at bpNode
        // L0003: Return %r2
        var instructions = new List<BytecodeInstruction>
        {
            new((byte)IrOpCode.LoadConst, 0, c1, 0, 0),
            new((byte)IrOpCode.LoadConst, 1, c2, 0, 0),
            new((byte)IrOpCode.Add, 2, 0, 1, 0),
            new((byte)IrOpCode.Return, BytecodeInstruction.NoRegister, 2, 0, 0)
        };
        var sourceMap = new List<NodeId?> { NodeId.New(), NodeId.New(), bpNode, NodeId.New() };

        var func = new BytecodeFunction(pool.GetOrAddString("Compute"), 3, 0, instructions, sourceMap);
        var program = new BytecodeProgram(GraphId.New(), RevisionId.New(), "hash", pool);

        var debugger = new GraphDebugger();
        debugger.SetBreakpoint(new Breakpoint(bpNode, BreakpointMode.GraphPause));

        var vm = new AstraVm();
        var result = vm.Execute(program, func, debugHook: debugger);

        // Execution should be suspended before executing instruction 2
        Assert.That(result.IsSuspended, Is.True);
        Assert.That(result.ResumeInstructionPointer, Is.EqualTo(2));
        Assert.That(result.CapturedRegisters![0].AsInt64(), Is.EqualTo(10));
        Assert.That(result.CapturedRegisters![1].AsInt64(), Is.EqualTo(20));

        Assert.That(debugger.IsPaused, Is.True);
        Assert.That(debugger.CurrentSuspension, Is.Not.Null);
        Assert.That(debugger.CurrentSuspension!.NodeId, Is.EqualTo(bpNode));
        Assert.That(debugger.CurrentSuspension.InstructionPointer, Is.EqualTo(2));

        // Resume debugger and finish execution
        debugger.Resume();
        Assert.That(debugger.IsPaused, Is.False);

        var resumeResult = vm.Execute(
            program,
            func,
            initialRegisters: result.CapturedRegisters.ToArray(),
            startIp: result.ResumeInstructionPointer,
            debugHook: debugger);

        Assert.That(resumeResult.IsSuccess, Is.True);
        Assert.That(resumeResult.ReturnValue.AsInt64(), Is.EqualTo(30));
    }

    [Test]
    public void Debugger_StepInto_AdvancesSingleInstructionAndSuspends()
    {
        var pool = new ConstantPool();
        var c1 = pool.GetOrAddInt64(10);
        var c2 = pool.GetOrAddInt64(20);
        var node0 = NodeId.New();
        var node1 = NodeId.New();
        var node2 = NodeId.New();

        var instructions = new List<BytecodeInstruction>
        {
            new((byte)IrOpCode.LoadConst, 0, c1, 0, 0),
            new((byte)IrOpCode.LoadConst, 1, c2, 0, 0),
            new((byte)IrOpCode.Add, 2, 0, 1, 0),
            new((byte)IrOpCode.Return, BytecodeInstruction.NoRegister, 2, 0, 0)
        };
        var sourceMap = new List<NodeId?> { node0, node1, node2, null };

        var func = new BytecodeFunction(pool.GetOrAddString("StepTest"), 3, 0, instructions, sourceMap);
        var program = new BytecodeProgram(GraphId.New(), RevisionId.New(), "hash", pool);

        var debugger = new GraphDebugger();
        // Start in StepInto mode
        debugger.StepInto();

        var vm = new AstraVm();

        // 1st step: executes instruction 0, suspends before instruction 1
        var step1 = vm.Execute(program, func, debugHook: debugger);
        Assert.That(step1.IsSuspended, Is.True);
        Assert.That(step1.ResumeInstructionPointer, Is.EqualTo(1));
        Assert.That(step1.CapturedRegisters![0].AsInt64(), Is.EqualTo(10));
        Assert.That(debugger.IsPaused, Is.True);

        // 2nd step: request another StepInto
        debugger.StepInto();
        var step2 = vm.Execute(
            program,
            func,
            initialRegisters: step1.CapturedRegisters.ToArray(),
            startIp: step1.ResumeInstructionPointer,
            debugHook: debugger);

        Assert.That(step2.IsSuspended, Is.True);
        Assert.That(step2.ResumeInstructionPointer, Is.EqualTo(2));
        Assert.That(step2.CapturedRegisters![1].AsInt64(), Is.EqualTo(20));

        // 3rd step: resume to completion
        debugger.Resume();
        var finalResult = vm.Execute(
            program,
            func,
            initialRegisters: step2.CapturedRegisters.ToArray(),
            startIp: step2.ResumeInstructionPointer,
            debugHook: debugger);

        Assert.That(finalResult.IsSuccess, Is.True);
        Assert.That(finalResult.ReturnValue.AsInt64(), Is.EqualTo(30));
    }

    [Test]
    public void Debugger_PauseAndResume_HaltsImmediatelyAndContinues()
    {
        var pool = new ConstantPool();
        var c1 = pool.GetOrAddInt64(42);
        var instructions = new List<BytecodeInstruction>
        {
            new((byte)IrOpCode.LoadConst, 0, c1, 0, 0),
            new((byte)IrOpCode.Return, BytecodeInstruction.NoRegister, 0, 0, 0)
        };

        var func = new BytecodeFunction(pool.GetOrAddString("PauseTest"), 1, 0, instructions);
        var program = new BytecodeProgram(GraphId.New(), RevisionId.New(), "hash", pool);

        var debugger = new GraphDebugger();
        debugger.Pause();

        var vm = new AstraVm();
        var pausedResult = vm.Execute(program, func, debugHook: debugger);

        // Suspended at instruction 0
        Assert.That(pausedResult.IsSuspended, Is.True);
        Assert.That(pausedResult.ResumeInstructionPointer, Is.EqualTo(0));

        // Resume
        debugger.Resume();
        var completedResult = vm.Execute(
            program,
            func,
            initialRegisters: pausedResult.CapturedRegisters?.ToArray(),
            startIp: pausedResult.ResumeInstructionPointer,
            debugHook: debugger);

        Assert.That(completedResult.IsSuccess, Is.True);
        Assert.That(completedResult.ReturnValue.AsInt64(), Is.EqualTo(42));
    }
}
