using System;
using System.Collections.Generic;
using AstraGraph.Core;
using AstraGraph.Runtime.Debugging;

namespace AstraGraph.Editor.Core.Debugging;

public sealed record DebugWatchItem(string Name, string Expression, string Value);

public sealed record DebugFrameInfo(string FunctionName, NodeId NodeId, int InstructionPointer);

/// <summary>
/// Session manager coordinating live graph debugging, breakpoint state,
/// suspension inspection, call frames, and variable watches on top of CanvasModel.
/// </summary>
public sealed class VisualDebuggerSession
{
    private readonly CanvasModel _canvasModel;
    private readonly GraphDebugger _debugger;
    private readonly Dictionary<string, string> _watches = [];

    public CanvasModel CanvasModel => _canvasModel;
    public GraphDebugger Debugger => _debugger;

    public DebugSuspension? CurrentSuspension { get; private set; }
    public bool IsPaused => CurrentSuspension != null;
    public NodeId? SuspendedNodeId => CurrentSuspension?.NodeId;

    public event Action<DebugSuspension>? OnSuspended;
    public event Action? OnResumed;
    public event Action<NodeId, bool>? OnBreakpointChanged;

    public VisualDebuggerSession(CanvasModel canvasModel, GraphDebugger debugger)
    {
        _canvasModel = canvasModel ?? throw new ArgumentNullException(nameof(canvasModel));
        _debugger = debugger ?? throw new ArgumentNullException(nameof(debugger));
    }

    public bool HasBreakpoint(NodeId nodeId) => _debugger.HasBreakpoint(nodeId);

    public void ToggleBreakpoint(NodeId nodeId, BreakpointMode mode = BreakpointMode.GraphPause, Func<IReadOnlyList<AstraValue>, bool>? condition = null)
    {
        if (_debugger.HasBreakpoint(nodeId))
        {
            _debugger.RemoveBreakpoint(nodeId);
            OnBreakpointChanged?.Invoke(nodeId, false);
        }
        else
        {
            _debugger.SetBreakpoint(new Breakpoint(nodeId, mode, condition));
            OnBreakpointChanged?.Invoke(nodeId, true);
        }
    }

    public void SetBreakpoint(Breakpoint breakpoint)
    {
        ArgumentNullException.ThrowIfNull(breakpoint);
        _debugger.SetBreakpoint(breakpoint);
        OnBreakpointChanged?.Invoke(breakpoint.NodeId, true);
    }

    public void RemoveBreakpoint(NodeId nodeId)
    {
        if (_debugger.RemoveBreakpoint(nodeId))
        {
            OnBreakpointChanged?.Invoke(nodeId, false);
        }
    }

    public void ClearAllBreakpoints()
    {
        _debugger.ClearBreakpoints();
        foreach (var node in _canvasModel.Nodes.Keys)
        {
            OnBreakpointChanged?.Invoke(node, false);
        }
    }

    public void HandleSuspension(DebugSuspension suspension)
    {
        CurrentSuspension = suspension ?? throw new ArgumentNullException(nameof(suspension));
        OnSuspended?.Invoke(suspension);
    }

    public void Resume()
    {
        CurrentSuspension = null;
        OnResumed?.Invoke();
    }

    public void StepOver()
    {
        // Resume until next node execution
        CurrentSuspension = null;
        OnResumed?.Invoke();
    }

    public void StepInto()
    {
        CurrentSuspension = null;
        OnResumed?.Invoke();
    }

    public void StepOut()
    {
        CurrentSuspension = null;
        OnResumed?.Invoke();
    }

    public void AddWatch(string name, string expression)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(expression);
        _watches[name] = expression;
    }

    public bool RemoveWatch(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _watches.Remove(name);
    }

    public IReadOnlyList<DebugWatchItem> EvaluateWatches()
    {
        var items = new List<DebugWatchItem>(_watches.Count);
        var registers = CurrentSuspension?.Registers;

        foreach (var (name, expr) in _watches)
        {
            var displayVal = EvaluateExpression(expr, registers);
            items.Add(new DebugWatchItem(name, expr, displayVal));
        }

        return items;
    }

    public IReadOnlyList<DebugWatchItem> GetLocals()
    {
        var result = new List<DebugWatchItem>();
        if (CurrentSuspension?.Registers == null)
        {
            return result;
        }

        var regs = CurrentSuspension.Registers;
        for (var i = 0; i < regs.Length; i++)
        {
            result.Add(new DebugWatchItem($"r{i}", $"r{i}", regs[i].ToString()));
        }

        return result;
    }

    public IReadOnlyList<DebugFrameInfo> GetCallStack()
    {
        var frames = new List<DebugFrameInfo>();
        if (CurrentSuspension != null)
        {
            frames.Add(new DebugFrameInfo("CurrentFrame", CurrentSuspension.NodeId, CurrentSuspension.InstructionPointer));
        }

        var recentTrace = _debugger.GetRecentTrace(10);
        foreach (var entry in recentTrace)
        {
            if (CurrentSuspension != null && entry.NodeId == CurrentSuspension.NodeId && entry.InstructionPointer == CurrentSuspension.InstructionPointer)
            {
                continue;
            }
            frames.Add(new DebugFrameInfo("TraceFrame", entry.NodeId, entry.InstructionPointer));
        }

        return frames;
    }

    private static string EvaluateExpression(string expr, AstraValue[]? registers)
    {
        if (registers == null)
        {
            return "<unavailable>";
        }

        if (expr.StartsWith("r", StringComparison.OrdinalIgnoreCase) && int.TryParse(expr.AsSpan(1), out var regIdx))
        {
            if (regIdx >= 0 && regIdx < registers.Length)
            {
                return registers[regIdx].ToString();
            }
            return "<out of range>";
        }

        return expr;
    }
}
