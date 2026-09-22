using AstraGraph.Core;

namespace AstraGraph.Runtime;

public enum ContinuationConditionKind
{
    TimeDelay,
    NextTick,
    Predicate,
    Event
}

/// <summary>
/// Condition determining when a suspended continuation should wake up and resume.
/// </summary>
public sealed record ContinuationCondition(
    ContinuationConditionKind Kind,
    double TargetTimeSeconds = 0,
    int TargetTick = 0,
    Func<bool>? Predicate = null,
    string? EventTypeName = null)
{
    public static ContinuationCondition DelaySeconds(double currentTimeSeconds, double delaySeconds) =>
        new(ContinuationConditionKind.TimeDelay, TargetTimeSeconds: currentTimeSeconds + delaySeconds);

    public static ContinuationCondition NextTick(int currentTick) =>
        new(ContinuationConditionKind.NextTick, TargetTick: currentTick + 1);

    public static ContinuationCondition Until(Func<bool> predicate) =>
        new(ContinuationConditionKind.Predicate, Predicate: predicate);

    public static ContinuationCondition AwaitEvent(string eventTypeName) =>
        new(ContinuationConditionKind.Event, EventTypeName: eventTypeName);

    public bool IsSatisfied(double currentTimeSeconds, int currentTick) => Kind switch
    {
        ContinuationConditionKind.TimeDelay => currentTimeSeconds >= TargetTimeSeconds,
        ContinuationConditionKind.NextTick => currentTick >= TargetTick,
        ContinuationConditionKind.Predicate => Predicate?.Invoke() ?? true,
        ContinuationConditionKind.Event => false, // awakened directly by event router
        _ => true
    };
}

/// <summary>
/// Serializable frame capturing suspended execution state across game ticks.
/// </summary>
public sealed class ContinuationFrame
{
    public Guid Id { get; } = Guid.NewGuid();
    public GraphId GraphId { get; }
    public BytecodeProgram Program { get; }
    public BytecodeFunction Function { get; }
    public AstraValue[] CapturedRegisters { get; }
    public Guid ResumePointId { get; }
    public int InstructionPointer { get; set; }
    public ContinuationCondition Condition { get; set; }
    public AstraEntityId? TargetEntity { get; }
    public bool IsCancelled { get; private set; }

    public ContinuationFrame(
        GraphId graphId,
        BytecodeProgram program,
        BytecodeFunction function,
        AstraValue[] capturedRegisters,
        Guid resumePointId,
        int instructionPointer,
        ContinuationCondition condition,
        AstraEntityId? targetEntity = null)
    {
        GraphId = graphId;
        Program = program;
        Function = function;
        CapturedRegisters = capturedRegisters;
        ResumePointId = resumePointId;
        InstructionPointer = instructionPointer;
        Condition = condition;
        TargetEntity = targetEntity;
    }

    public void Cancel()
    {
        IsCancelled = true;
    }
}
