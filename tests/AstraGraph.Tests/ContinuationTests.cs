using AstraGraph.Core;
using AstraGraph.Runtime;
using AstraGraph.VM;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class ContinuationTests
{
    [Test]
    public void ContinuationScheduler_TimeDelay_ResumesAtTargetTime()
    {
        var pool = new ConstantPool();
        var cVal = pool.GetOrAddInt64(50);
        var fnName = pool.GetOrAddString("DelayedMath");
        var host = new DefaultVmHostServices();

        // L0000: %r0 = LoadConst #cVal (50)
        // L0001: %r1 = Add %r0, %r0 (100)
        // L0002: Return %r1
        var instructions = new List<BytecodeInstruction>
        {
            new((byte)IrOpCode.LoadConst, 0, cVal, 0, 0),
            new((byte)IrOpCode.Add, 1, 0, 0, 0),
            new((byte)IrOpCode.Return, BytecodeInstruction.NoRegister, 1, 0, 0)
        };

        var func = new BytecodeFunction(fnName, 2, 0, instructions);
        var program = new BytecodeProgram(GraphId.New(), RevisionId.New(), "hash", pool);

        var scheduler = new ContinuationScheduler(new AstraVm(), host);

        var frame = new ContinuationFrame(
            program.Id,
            program,
            func,
            capturedRegisters: new AstraValue[2],
            resumePointId: Guid.NewGuid(),
            instructionPointer: 0,
            condition: ContinuationCondition.DelaySeconds(currentTimeSeconds: 0.0, delaySeconds: 2.0));

        scheduler.Schedule(frame);
        Assert.That(scheduler.ActiveCount, Is.EqualTo(1));

        // Update at t=1.0s: should NOT wake up
        scheduler.Update(currentTimeSeconds: 1.0, currentTick: 10);
        Assert.That(scheduler.ActiveCount, Is.EqualTo(1));

        // Update at t=2.0s: should wake up, execute, and complete
        scheduler.Update(currentTimeSeconds: 2.0, currentTick: 20);
        Assert.That(scheduler.ActiveCount, Is.EqualTo(0));
    }

    [Test]
    public void ContinuationScheduler_EntityDeletion_CancelsTargetContinuations()
    {
        var pool = new ConstantPool();
        var func = new BytecodeFunction(0, 1, 0, [new((byte)IrOpCode.Return, BytecodeInstruction.NoRegister, -1, 0, 0)]);
        var program = new BytecodeProgram(GraphId.New(), RevisionId.New(), "hash", pool);

        var scheduler = new ContinuationScheduler();

        var targetEntity = 100500;
        var frame = new ContinuationFrame(
            program.Id,
            program,
            func,
            new AstraValue[1],
            Guid.NewGuid(),
            0,
            ContinuationCondition.DelaySeconds(0, 10.0),
            targetEntity: targetEntity);

        scheduler.Schedule(frame);
        Assert.That(scheduler.ActiveCount, Is.EqualTo(1));

        // Delete entity
        scheduler.CancelByEntity(targetEntity);
        Assert.That(scheduler.ActiveCount, Is.EqualTo(0));
        Assert.That(frame.IsCancelled, Is.True);
    }

    [Test]
    public void ContinuationScheduler_Predicate_ResumesWhenConditionIsTrue()
    {
        var pool = new ConstantPool();
        var func = new BytecodeFunction(0, 1, 0, [new((byte)IrOpCode.Return, BytecodeInstruction.NoRegister, -1, 0, 0)]);
        var program = new BytecodeProgram(GraphId.New(), RevisionId.New(), "hash", pool);

        var scheduler = new ContinuationScheduler();

        var doorOpen = false;
        var frame = new ContinuationFrame(
            program.Id,
            program,
            func,
            new AstraValue[1],
            Guid.NewGuid(),
            0,
            ContinuationCondition.Until(() => doorOpen));

        scheduler.Schedule(frame);
        Assert.That(scheduler.ActiveCount, Is.EqualTo(1));

        // First tick: false
        scheduler.Update(1.0, 1);
        Assert.That(scheduler.ActiveCount, Is.EqualTo(1));

        // Open door
        doorOpen = true;
        scheduler.Update(2.0, 2);
        Assert.That(scheduler.ActiveCount, Is.EqualTo(0));
    }
}
