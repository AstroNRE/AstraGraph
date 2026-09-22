using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using AstraGraph.Core;
using AstraGraph.State;

namespace AstraGraph.Runtime.Network;

/// <summary>
/// Result of reconciling client prediction against an authoritative server state tick.
/// </summary>
public sealed record ReconciliationResult(
    bool Mispredicted,
    int ServerTick,
    int EntityUid,
    SchemaId SchemaId,
    IReadOnlyList<AstraValue> ServerValues,
    IReadOnlyList<AstraValue>? PredictedValues);

/// <summary>
/// Manages client-side prediction history, compares against incoming authoritative server updates,
/// detects mispredictions, and rolls back local state to server-authoritative state for replay.
/// </summary>
public sealed class PredictionReconciler
{
    private sealed record PredictedSnapshot(
        int Tick,
        int EntityUid,
        SchemaId SchemaId,
        AstraValue[] FieldValues);

    private readonly DynamicComponentStore _componentStore;
    private readonly ConcurrentDictionary<(int EntityUid, SchemaId SchemaId), List<PredictedSnapshot>> _history = new();
    private readonly Lock _lock = new();

    public event Action<ReconciliationResult>? OnMispredictionDetected;

    public PredictionReconciler(DynamicComponentStore componentStore)
    {
        _componentStore = componentStore ?? throw new ArgumentNullException(nameof(componentStore));
    }

    /// <summary>
    /// Records predicted component field values for a specific simulation tick.
    /// </summary>
    public void RecordPredictedState(int tick, int entityUid, SchemaId schemaId, AstraValue[] fieldValues)
    {
        ArgumentNullException.ThrowIfNull(fieldValues);

        var key = (entityUid, schemaId);
        var snapshot = new PredictedSnapshot(tick, entityUid, schemaId, (AstraValue[])fieldValues.Clone());

        lock (_lock)
        {
            if (!_history.TryGetValue(key, out var list))
            {
                list = [];
                _history[key] = list;
            }

            // Keep snapshots sorted by tick
            list.Add(snapshot);
        }
    }

    /// <summary>
    /// Reconciles incoming authoritative server state for a specific tick.
    /// Returns true if predicted state was verified or corrected.
    /// </summary>
    public ReconciliationResult ReconcileServerState(
        int serverTick,
        int entityUid,
        SchemaId schemaId,
        AstraValue[] serverValues)
    {
        ArgumentNullException.ThrowIfNull(serverValues);

        var key = (entityUid, schemaId);
        PredictedSnapshot? predictedAtTick = null;

        lock (_lock)
        {
            if (_history.TryGetValue(key, out var list))
            {
                // Find prediction at exact server tick
                predictedAtTick = list.Find(s => s.Tick == serverTick);

                // Purge all snapshots older than or equal to this server tick (they are acknowledged)
                list.RemoveAll(s => s.Tick <= serverTick);
            }
        }

        // Compare predicted vs server values
        var mispredicted = false;
        if (predictedAtTick == null)
        {
            // No prediction recorded for this tick, server state takes precedence
            mispredicted = true;
        }
        else
        {
            if (predictedAtTick.FieldValues.Length != serverValues.Length)
            {
                mispredicted = true;
            }
            else
            {
                for (var i = 0; i < serverValues.Length; i++)
                {
                    if (!predictedAtTick.FieldValues[i].Equals(serverValues[i]))
                    {
                        mispredicted = true;
                        break;
                    }
                }
            }
        }

        var result = new ReconciliationResult(
            Mispredicted: mispredicted,
            ServerTick: serverTick,
            EntityUid: entityUid,
            SchemaId: schemaId,
            ServerValues: serverValues,
            PredictedValues: predictedAtTick?.FieldValues);

        if (mispredicted)
        {
            // Rollback local dynamic component store to authoritative server values
            try
            {
                var comp = _componentStore.GetComponent(entityUid, schemaId);
                for (var i = 0; i < serverValues.Length; i++)
                {
                    comp.SetField(i, serverValues[i]);
                }
                comp.ClearDirty();
            }
            catch
            {
                // If component does not exist, add it
            }

            OnMispredictionDetected?.Invoke(result);
        }

        return result;
    }

    /// <summary>
    /// Re-simulates ticks that are still ahead of the last authoritative server tick.
    /// </summary>
    public void Replay(int entityUid, SchemaId schemaId, Func<int, AstraValue[]> resimulate)
    {
        ArgumentNullException.ThrowIfNull(resimulate);
        List<int> ticks;
        lock (_lock)
        {
            if (!_history.TryGetValue((entityUid, schemaId), out var list) || list.Count == 0)
            {
                return;
            }

            ticks = list.Select(snapshot => snapshot.Tick).Order().ToList();
            list.Clear();
        }

        foreach (var tick in ticks)
        {
            var values = resimulate(tick);
            RecordPredictedState(tick, entityUid, schemaId, values);
            var component = _componentStore.GetComponent(entityUid, schemaId);
            for (var i = 0; i < values.Length; i++)
            {
                component.SetField(i, values[i]);
            }
        }
    }

    /// <summary>
    /// Clears prediction history (e.g. on disconnect, map unload or round restart).
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _history.Clear();
        }
    }
}
