using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using AstraGraph.Core;
using AstraGraph.VM;

namespace AstraGraph.Runtime.Debugging;

public enum BreakpointMode
{
    GraphPause,
    Tracepoint
}

public enum DebuggerExecutionMode
{
    Running,
    Paused,
    StepInto,
    StepOver,
    StepOut
}

public sealed record Breakpoint(
    NodeId NodeId,
    BreakpointMode Mode = BreakpointMode.GraphPause,
    Func<IReadOnlyList<AstraValue>, bool>? Condition = null);

public sealed record ExecutionTraceEntry(
    NodeId NodeId,
    int InstructionPointer,
    IReadOnlyList<AstraValue> CapturedRegisters,
    DateTimeOffset Timestamp);

public sealed class DebugSuspension
{
    public Guid Id { get; } = Guid.NewGuid();
    public NodeId NodeId { get; }
    public int InstructionPointer { get; }
    public AstraValue[] Registers { get; }
    public DateTimeOffset Timestamp { get; }

    public DebugSuspension(NodeId nodeId, int instructionPointer, AstraValue[] registers)
    {
        NodeId = nodeId;
        InstructionPointer = instructionPointer;
        Registers = registers;
        Timestamp = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// Live graph debugger supporting non-blocking GraphPause breakpoints,
/// tracepoints, stepping (into/over), pause/resume, variable inspection, and bounded execution trace ring buffers.
/// </summary>
public sealed class GraphDebugger : IVmDebugHook
{
    private readonly ConcurrentDictionary<NodeId, Breakpoint> _breakpoints = new();
    private readonly List<ExecutionTraceEntry> _traceRing = [];
    private readonly Lock _lock = new();
    private bool _hasExecutedStep;

    public int MaxTraceEntries { get; set; } = 500;
    public DebuggerExecutionMode ExecutionMode { get; private set; } = DebuggerExecutionMode.Running;
    public DebugSuspension? CurrentSuspension { get; private set; }
    public bool IsPaused => ExecutionMode == DebuggerExecutionMode.Paused || CurrentSuspension != null;

    public event Action<DebugSuspension>? OnSuspension;
    public event Action? OnResumed;

    public void SetBreakpoint(Breakpoint breakpoint)
    {
        ArgumentNullException.ThrowIfNull(breakpoint);
        _breakpoints[breakpoint.NodeId] = breakpoint;
    }

    public bool RemoveBreakpoint(NodeId nodeId) => _breakpoints.TryRemove(nodeId, out _);

    public void ClearBreakpoints() => _breakpoints.Clear();

    public bool HasBreakpoint(NodeId nodeId) => _breakpoints.ContainsKey(nodeId);

    private int _skipBreakpointAtIp = -1;

    public void Pause()
    {
        ExecutionMode = DebuggerExecutionMode.Paused;
    }

    public void Resume()
    {
        if (CurrentSuspension != null)
        {
            _skipBreakpointAtIp = CurrentSuspension.InstructionPointer;
        }
        CurrentSuspension = null;
        ExecutionMode = DebuggerExecutionMode.Running;
        OnResumed?.Invoke();
    }

    public void StepInto()
    {
        if (CurrentSuspension != null)
        {
            _skipBreakpointAtIp = CurrentSuspension.InstructionPointer;
        }
        CurrentSuspension = null;
        ExecutionMode = DebuggerExecutionMode.StepInto;
        _hasExecutedStep = false;
        OnResumed?.Invoke();
    }

    public void StepOver()
    {
        if (CurrentSuspension != null)
        {
            _skipBreakpointAtIp = CurrentSuspension.InstructionPointer;
        }
        CurrentSuspension = null;
        ExecutionMode = DebuggerExecutionMode.StepOver;
        _hasExecutedStep = false;
        OnResumed?.Invoke();
    }

    public void StepOut()
    {
        if (CurrentSuspension != null)
        {
            _skipBreakpointAtIp = CurrentSuspension.InstructionPointer;
        }

        CurrentSuspension = null;
        ExecutionMode = DebuggerExecutionMode.StepOut;
        OnResumed?.Invoke();
    }

    public bool StopForStepOut(IrOpCode opcode, NodeId? nodeId, int instructionPointer, ReadOnlySpan<AstraValue> registers)
    {
        if (ExecutionMode != DebuggerExecutionMode.StepOut)
        {
            return false;
        }

        if (opcode is not (IrOpCode.Return or IrOpCode.YieldContinuation))
        {
            return false;
        }

        if (_skipBreakpointAtIp == instructionPointer)
        {
            _skipBreakpointAtIp = -1;
            ExecutionMode = DebuggerExecutionMode.Running;
            return false;
        }

        var suspension = new DebugSuspension(nodeId ?? NodeId.Empty, instructionPointer, registers.ToArray());
        CurrentSuspension = suspension;
        ExecutionMode = DebuggerExecutionMode.Paused;
        OnSuspension?.Invoke(suspension);
        return true;
    }

    /// <summary>
    /// Checks whether execution at the given node hits a breakpoint.
    /// Returns true with a DebugSuspension if GraphPause is triggered;
    /// logs and returns false if a Tracepoint is hit.
    /// </summary>
    public bool CheckBreakpoint(
        NodeId nodeId,
        int instructionPointer,
        ReadOnlySpan<AstraValue> registers,
        out DebugSuspension? suspension)
    {
        suspension = null;

        var regsArray = registers.ToArray();
        RecordTrace(nodeId, instructionPointer, regsArray);

        if (!_breakpoints.TryGetValue(nodeId, out var bp))
        {
            return false;
        }

        if (bp.Condition != null && !bp.Condition(regsArray))
        {
            return false;
        }

        if (bp.Mode == BreakpointMode.Tracepoint)
        {
            return false;
        }

        suspension = new DebugSuspension(nodeId, instructionPointer, regsArray);
        CurrentSuspension = suspension;
        return true;
    }

    public bool ShouldSuspend(
        NodeId? nodeId,
        int instructionPointer,
        ReadOnlySpan<AstraValue> registers)
    {
        var effectiveNode = nodeId ?? NodeId.New();
        var regsArray = registers.ToArray();
        RecordTrace(effectiveNode, instructionPointer, regsArray);

        if (ExecutionMode == DebuggerExecutionMode.Paused)
        {
            var suspension = new DebugSuspension(effectiveNode, instructionPointer, regsArray);
            CurrentSuspension = suspension;
            OnSuspension?.Invoke(suspension);
            return true;
        }

        if (ExecutionMode is DebuggerExecutionMode.StepInto or DebuggerExecutionMode.StepOver)
        {
            if (!_hasExecutedStep)
            {
                _hasExecutedStep = true;
                return false;
            }

            var suspension = new DebugSuspension(effectiveNode, instructionPointer, regsArray);
            CurrentSuspension = suspension;
            ExecutionMode = DebuggerExecutionMode.Paused;
            OnSuspension?.Invoke(suspension);
            return true;
        }

        if (_skipBreakpointAtIp == instructionPointer)
        {
            _skipBreakpointAtIp = -1;
            return false;
        }

        if (nodeId.HasValue && _breakpoints.TryGetValue(nodeId.Value, out var bp))
        {
            if (bp.Condition == null || bp.Condition(regsArray))
            {
                if (bp.Mode == BreakpointMode.GraphPause)
                {
                    var suspension = new DebugSuspension(nodeId.Value, instructionPointer, regsArray);
                    CurrentSuspension = suspension;
                    ExecutionMode = DebuggerExecutionMode.Paused;
                    OnSuspension?.Invoke(suspension);
                    return true;
                }
            }
        }

        return false;
    }

    private void RecordTrace(NodeId nodeId, int ip, AstraValue[] registers)
    {
        lock (_lock)
        {
            if (_traceRing.Count >= MaxTraceEntries)
            {
                _traceRing.RemoveAt(0);
            }
            _traceRing.Add(new ExecutionTraceEntry(nodeId, ip, registers, DateTimeOffset.UtcNow));
        }
    }

    public IReadOnlyList<ExecutionTraceEntry> GetRecentTrace(int count = 50)
    {
        lock (_lock)
        {
            var take = Math.Min(count, _traceRing.Count);
            return _traceRing.GetRange(_traceRing.Count - take, take);
        }
    }
}
