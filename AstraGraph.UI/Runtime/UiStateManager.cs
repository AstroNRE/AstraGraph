using System.Collections.Concurrent;

namespace AstraGraph.UI.Runtime;

public sealed record VariableChangedEventArgs(string Name, object? OldValue, object? NewValue);

/// <summary>
/// Manages reactive local UI state variables, change notifications, and dirty tracking.
/// </summary>
public sealed class UiStateManager
{
    private readonly ConcurrentDictionary<string, object?> _variables = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _dirty = new(StringComparer.Ordinal);

    public event Action<VariableChangedEventArgs>? OnVariableChanged;

    public UiStateManager(IReadOnlyDictionary<string, object?>? initialValues = null)
    {
        if (initialValues != null)
        {
            foreach (var kv in initialValues)
            {
                _variables[kv.Key] = kv.Value;
            }
        }
    }

    public void SetVariable(string name, object? value)
    {
        bool changed = true;
        _variables.AddOrUpdate(name, value, (_, oldVal) =>
        {
            if (Equals(oldVal, value))
            {
                changed = false;
                return oldVal;
            }
            return value;
        });

        if (changed)
        {
            _dirty[name] = 0;
            OnVariableChanged?.Invoke(new VariableChangedEventArgs(name, null, value));
        }
    }

    public object? GetVariable(string name)
    {
        _variables.TryGetValue(name, out var val);
        return val;
    }

    public T? GetVariable<T>(string name)
    {
        var val = GetVariable(name);
        if (val is T typed) return typed;
        if (val == null) return default;
        return (T)Convert.ChangeType(val, typeof(T));
    }

    public IReadOnlyDictionary<string, object?> GetAllVariables() =>
        new Dictionary<string, object?>(_variables, StringComparer.Ordinal);

    public IReadOnlySet<string> ConsumeDirtyVariables()
    {
        var dirtySet = new HashSet<string>(_dirty.Keys, StringComparer.Ordinal);
        _dirty.Clear();
        return dirtySet;
    }
}
