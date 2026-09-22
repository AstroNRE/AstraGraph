using AstraGraph.Core;

namespace AstraGraph.VM;

public class ExecutionBudgetExceededException : Exception
{
    public ExecutionBudgetExceededException(string message) : base(message) { }
}

/// <summary>
/// Controls execution bounds to guarantee protection against infinite loops, runaway recursion, or CPU starvation.
/// </summary>
public sealed class ExecutionBudget
{
    public const int DefaultMaxInstructions = 100_000;
    public const int DefaultMaxRecursionDepth = 64;

    public int MaxInstructions { get; init; } = DefaultMaxInstructions;
    public int MaxRecursionDepth { get; init; } = DefaultMaxRecursionDepth;

    public int InstructionsExecuted { get; private set; }

    public void Tick()
    {
        InstructionsExecuted++;
        if (InstructionsExecuted > MaxInstructions)
        {
            throw new ExecutionBudgetExceededException(
                $"Astra VM execution budget exceeded: limit of {MaxInstructions} instructions was reached.");
        }
    }

    public void Reset()
    {
        InstructionsExecuted = 0;
    }
}

public enum VmExecutionStatus
{
    Completed,
    Yielded,
    Faulted,
    ExceededBudget
}

/// <summary>
/// Captured state when a graph execution yields via Delay, DoAfter, or WaitUntil.
/// </summary>
public sealed record ContinuationState(
    ContinuationKind Kind,
    Guid ResumePointId,
    int NextInstructionPointer,
    IReadOnlyList<AstraValue> Arguments,
    double DelaySeconds = 0.0,
    string? EventTypeName = null);

/// <summary>
/// Hook for VM debugging (breakpoints, stepping, pausing).
/// </summary>
public interface IVmDebugHook
{
    bool ShouldSuspend(
        NodeId? nodeId,
        int instructionPointer,
        ReadOnlySpan<AstraValue> registers);

    /// <summary>
    /// Step Out stops on the current function's Return or Yield. Other hooks ignore it.
    /// </summary>
    bool StopForStepOut(
        IrOpCode opcode,
        NodeId? nodeId,
        int instructionPointer,
        ReadOnlySpan<AstraValue> registers) => false;
}

/// <summary>
/// Structured result returned by Astra VM invocation.
/// </summary>
public sealed record VmExecutionResult(
    VmExecutionStatus Status,
    AstraValue ReturnValue,
    ContinuationState? YieldState = null,
    Exception? Exception = null,
    int InstructionsExecuted = 0,
    int ResumeInstructionPointer = 0,
    IReadOnlyList<AstraValue>? CapturedRegisters = null)
{
    public bool IsSuccess => Status == VmExecutionStatus.Completed;
    public bool IsYielded => Status == VmExecutionStatus.Yielded;
    public bool IsSuspended => Status == VmExecutionStatus.Yielded && YieldState == null;
}
