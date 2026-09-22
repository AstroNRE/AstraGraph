using System.Collections.Concurrent;
using AstraGraph.Core;

namespace AstraGraph.State;

/// <summary>
/// Central runtime state store holding all variable states for graph programs and systems.
/// Maintains complete separation between compiled CODE and mutable STATE.
/// </summary>
public sealed class AstraStateStore
{
    public sealed record VariableState(
        SymbolId Id,
        string Name,
        AstraValue Value,
        bool IsPersistent);

    private readonly ConcurrentDictionary<(GraphId, string), VariableState> _variablesByName = new();
    private readonly ConcurrentDictionary<(GraphId, SymbolId), VariableState> _variablesById = new();
    private readonly ConcurrentDictionary<(int, string), AstraValue> _entityVariables = new();
    private readonly Lock _lock = new();

    public void SetVariable(GraphId graphId, SymbolId variableId, string name, AstraValue value, bool isPersistent = false)
    {
        ArgumentNullException.ThrowIfNull(name);

        lock (_lock)
        {
            var effectiveId = variableId;
            var effectivePersistent = isPersistent;

            if (_variablesByName.TryGetValue((graphId, name), out var existing))
            {
                if (effectiveId == SymbolId.Empty)
                {
                    effectiveId = existing.Id;
                }
                effectivePersistent = isPersistent || existing.IsPersistent;
            }

            var state = new VariableState(effectiveId, name, value, effectivePersistent);
            _variablesByName[(graphId, name)] = state;
            if (effectiveId != SymbolId.Empty)
            {
                _variablesById[(graphId, effectiveId)] = state;
            }
        }
    }

    public AstraValue GetVariable(GraphId graphId, SymbolId variableId, string name)
    {
        if (variableId != SymbolId.Empty && _variablesById.TryGetValue((graphId, variableId), out var byId))
        {
            return byId.Value;
        }

        if (_variablesByName.TryGetValue((graphId, name), out var byName))
        {
            return byName.Value;
        }

        return AstraValue.Null;
    }

    public bool TryGetVariable(GraphId graphId, string name, out AstraValue value)
    {
        if (_variablesByName.TryGetValue((graphId, name), out var state))
        {
            value = state.Value;
            return true;
        }
        value = AstraValue.Null;
        return false;
    }

    /// <summary>
    /// Captures all variables marked as IsPersistent for crash-safe serialization or server restart.
    /// </summary>
    public Dictionary<(GraphId, SymbolId), (string Name, AstraValue Value)> GetPersistentSnapshot()
    {
        lock (_lock)
        {
            var result = new Dictionary<(GraphId, SymbolId), (string Name, AstraValue Value)>();
            foreach (var (key, state) in _variablesById)
            {
                if (state.IsPersistent)
                {
                    result[key] = (state.Name, state.Value);
                }
            }
            return result;
        }
    }

    /// <summary>
    /// Restores persistent variables after server restart or round load.
    /// </summary>
    public void RestorePersistentSnapshot(IDictionary<(GraphId, SymbolId), (string Name, AstraValue Value)> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_lock)
        {
            foreach (var ((graphId, symbolId), (name, val)) in snapshot)
            {
                SetVariable(graphId, symbolId, name, val, isPersistent: true);
            }
        }
    }

    public void ClearGraphState(GraphId graphId)
    {
        lock (_lock)
        {
            var namesToRemove = _variablesByName.Keys.Where(k => k.Item1 == graphId).ToList();
            foreach (var key in namesToRemove)
            {
                _variablesByName.TryRemove(key, out _);
            }

            var idsToRemove = _variablesById.Keys.Where(k => k.Item1 == graphId).ToList();
            foreach (var key in idsToRemove)
            {
                _variablesById.TryRemove(key, out _);
            }
        }
    }

    public void SetEntityVariable(int entityUid, string name, AstraValue value)
    {
        ArgumentNullException.ThrowIfNull(name);
        _entityVariables[(entityUid, name)] = value;
    }

    public AstraValue GetEntityVariable(int entityUid, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _entityVariables.TryGetValue((entityUid, name), out var val) ? val : AstraValue.Null;
    }

    public void ClearEntity(int entityUid)
    {
        lock (_lock)
        {
            var keysToRemove = _entityVariables.Keys.Where(k => k.Item1 == entityUid).ToList();
            foreach (var key in keysToRemove)
            {
                _entityVariables.TryRemove(key, out _);
            }
        }
    }

    public void ClearAll()
    {
        lock (_lock)
        {
            _variablesByName.Clear();
            _variablesById.Clear();
            _entityVariables.Clear();
        }
    }
}
