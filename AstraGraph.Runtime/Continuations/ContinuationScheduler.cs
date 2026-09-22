using AstraGraph.Core;
using AstraGraph.VM;

namespace AstraGraph.Runtime;

/// <summary>
/// Tick-based scheduler driving non-blocking latent continuations (Delay, DoAfter, WaitUntil).
/// Integrates directly with entity deletion to guarantee deterministic cleanup.
/// </summary>
public sealed class ContinuationScheduler
{
    private readonly List<ContinuationFrame> _active = [];
    private readonly AstraVm _vm;
    private readonly IVmHostServices _hostServices;
    private readonly Lock _lock = new();

    public int MaxContinuations { get; init; } = 10_000;

    public int ActiveCount
    {
        get
        {
            lock (_lock) return _active.Count;
        }
    }

    public ContinuationScheduler(AstraVm? vm = null, IVmHostServices? hostServices = null)
    {
        _vm = vm ?? new AstraVm();
        _hostServices = hostServices ?? new DefaultVmHostServices();
    }

    public void Schedule(ContinuationFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        lock (_lock)
        {
            if (_active.Count >= MaxContinuations)
            {
                throw new InvalidOperationException($"Continuation budget exceeded: cannot schedule more than {MaxContinuations} active latent operations.");
            }
            _active.Add(frame);
        }
    }

    public void Update(double currentTimeSeconds, int currentTick)
    {
        List<ContinuationFrame> toProcess;
        lock (_lock)
        {
            toProcess = [.. _active];
        }

        foreach (var frame in toProcess)
        {
            if (frame.IsCancelled)
            {
                lock (_lock) _active.Remove(frame);
                continue;
            }

            if (frame.Condition.IsSatisfied(currentTimeSeconds, currentTick))
            {
                // Resume VM execution
                var result = _vm.Execute(
                    frame.Program,
                    frame.Function,
                    initialRegisters: frame.CapturedRegisters,
                    startIp: frame.InstructionPointer,
                    hostServices: _hostServices);

                if (result.IsYielded && result.YieldState != null)
                {
                    // Update continuation for the next yield point
                    frame.InstructionPointer = result.YieldState.NextInstructionPointer;
                    Array.Copy(result.YieldState.Arguments.ToArray(), frame.CapturedRegisters, Math.Min(result.YieldState.Arguments.Count, frame.CapturedRegisters.Length));

                    var delaySec = result.YieldState.DelaySeconds > 0 ? result.YieldState.DelaySeconds : 1.0;
                    frame.Condition = result.YieldState.Kind switch
                    {
                        ContinuationKind.Delay or ContinuationKind.DoAfter =>
                            ContinuationCondition.DelaySeconds(currentTimeSeconds, delaySec),
                        ContinuationKind.WaitUntil =>
                            ContinuationCondition.NextTick(currentTick),
                        ContinuationKind.AwaitEvent =>
                            ContinuationCondition.AwaitEvent(result.YieldState.EventTypeName ?? string.Empty),
                        _ => ContinuationCondition.DelaySeconds(currentTimeSeconds, delaySec)
                    };
                }
                else
                {
                    // Finished or faulted: remove
                    lock (_lock) _active.Remove(frame);
                }
            }
        }
    }

    public void WakeByEvent(string eventTypeName, AstraValue eventPayload)
    {
        ArgumentNullException.ThrowIfNull(eventTypeName);

        List<ContinuationFrame> matching;
        lock (_lock)
        {
            matching = _active.Where(f =>
                !f.IsCancelled &&
                f.Condition.Kind == ContinuationConditionKind.Event &&
                string.Equals(f.Condition.EventTypeName, eventTypeName, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        foreach (var frame in matching)
        {
            // Inject event payload into first register if applicable
            if (frame.CapturedRegisters.Length > 0)
            {
                frame.CapturedRegisters[0] = eventPayload;
            }

            var result = _vm.Execute(
                frame.Program,
                frame.Function,
                initialRegisters: frame.CapturedRegisters,
                startIp: frame.InstructionPointer,
                hostServices: _hostServices);

            if (result.IsYielded && result.YieldState != null)
            {
                frame.InstructionPointer = result.YieldState.NextInstructionPointer;
                Array.Copy(result.YieldState.Arguments.ToArray(), frame.CapturedRegisters, Math.Min(result.YieldState.Arguments.Count, frame.CapturedRegisters.Length));

                var delaySec = result.YieldState.DelaySeconds > 0 ? result.YieldState.DelaySeconds : 1.0;
                frame.Condition = result.YieldState.Kind switch
                {
                    ContinuationKind.Delay or ContinuationKind.DoAfter =>
                        ContinuationCondition.DelaySeconds(0.0, delaySec),
                    ContinuationKind.WaitUntil =>
                        ContinuationCondition.NextTick(0),
                    ContinuationKind.AwaitEvent =>
                        ContinuationCondition.AwaitEvent(result.YieldState.EventTypeName ?? string.Empty),
                    _ => ContinuationCondition.DelaySeconds(0.0, delaySec)
                };
            }
            else
            {
                lock (_lock) _active.Remove(frame);
            }
        }
    }

public void CancelByGraph(GraphId graphId)
    {
        lock (_lock)
        {
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                if (_active[i].GraphId == graphId)
                {
                    _active[i].Cancel();
                    _active.RemoveAt(i);
                }
            }
        }
    }

    public void CancelByEntity(AstraEntityId entityUid)
    {
        lock (_lock)
        {
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                if (_active[i].TargetEntity == entityUid)
                {
                    _active[i].Cancel();
                    _active.RemoveAt(i);
                }
            }
        }
    }

    public void CancelAll()
    {
        lock (_lock)
        {
            foreach (var frame in _active)
            {
                frame.Cancel();
            }
            _active.Clear();
        }
    }
}
