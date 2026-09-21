using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using AstraGraph.Core;

namespace AstraGraph.Runtime.Debugging;

public enum BreakpointMode
{
    GraphPause,
    Tracepoint
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
/// tracepoints, variable inspection, and bounded execution trace ring buffers.
/// </summary>
public sealed class GraphDebugger
{
    private readonly ConcurrentDictionary<NodeId, Breakpoint> _breakpoints = new();
    private readonly List<ExecutionTraceEntry> _traceRing = [];
    private readonly Lock _lock = new();
    public int MaxTraceEntries { get; set; } = 500;

    public void SetBreakpoint(Breakpoint breakpoint)
    {
        ArgumentNullException.ThrowIfNull(breakpoint);
        _breakpoints[breakpoint.NodeId] = breakpoint;
    }

    public bool RemoveBreakpoint(NodeId nodeId) => _breakpoints.TryRemove(nodeId, out _);

    public void ClearBreakpoints() => _breakpoints.Clear();

    public bool HasBreakpoint(NodeId nodeId) => _breakpoints.ContainsKey(nodeId);

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
            // Tracepoint only records trace; does not suspend
            return false;
        }

        suspension = new DebugSuspension(nodeId, instructionPointer, regsArray);
        return true;
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
