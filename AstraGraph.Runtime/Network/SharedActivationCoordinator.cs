using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using AstraGraph.Core;

namespace AstraGraph.Runtime.Network;

/// <summary>
/// Coordinates synchronized atomic revision activation across server and client at an exact tick boundary.
/// </summary>
public sealed class SharedActivationCoordinator
{
    private sealed record PendingActivation(
        GraphId GraphId,
        RevisionId RevisionId,
        BytecodeProgram Program,
        int ActivationTick);

    private readonly AstraGraphHost _host;
    private readonly List<PendingActivation> _pending = [];
    private readonly Lock _lock = new();

    public int LeadTicks { get; set; } = 4;

    public event Action<SharedGraphEntry>? SynchronizedActivationCommitted;

    public SharedActivationCoordinator(AstraGraphHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>
    /// Schedules a shared revision for synchronized activation at a future tick.
    /// Returns the SharedGraphEntry to be broadcast to clients.
    /// </summary>
    public SharedGraphEntry ScheduleActivation(
        BytecodeProgram program,
        string schemaHash,
        GraphSide side,
        int currentTick)
    {
        ArgumentNullException.ThrowIfNull(program);

        var activationTick = currentTick + Math.Max(LeadTicks, 1);
        var entry = new SharedGraphEntry(
            program.Id,
            program.Revision,
            program.SemanticHash,
            schemaHash,
            side,
            activationTick);

        lock (_lock)
        {
            _pending.Add(new PendingActivation(program.Id, program.Revision, program, activationTick));
        }

        return entry;
    }

    /// <summary>
    /// Client-side receipt: queues an activation packet from the server to activate at the designated ActivationTick.
    /// </summary>
    public void QueueRemoteActivation(BytecodeProgram program, int activationTick)
    {
        ArgumentNullException.ThrowIfNull(program);

        lock (_lock)
        {
            _pending.Add(new PendingActivation(program.Id, program.Revision, program, activationTick));
        }
    }

    /// <summary>
    /// Processes scheduled activations during tick update.
    /// If currentTick >= ActivationTick, atomically commits the new revision.
    /// </summary>
    public int Update(int currentTick)
    {
        List<PendingActivation> ready = [];

        lock (_lock)
        {
            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                var item = _pending[i];
                if (currentTick >= item.ActivationTick)
                {
                    ready.Add(item);
                    _pending.RemoveAt(i);
                }
            }
        }

        foreach (var act in ready)
        {
            _host.RegisterProgram(act.Program);

            SynchronizedActivationCommitted?.Invoke(new SharedGraphEntry(
                act.GraphId,
                act.RevisionId,
                act.Program.SemanticHash,
                string.Empty,
                GraphSide.Shared,
                act.ActivationTick));
        }

        return ready.Count;
    }
}
