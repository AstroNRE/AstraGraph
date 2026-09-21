using AstraGraph.Core;

namespace AstraGraph.Runtime;

/// <summary>
/// Specification for an ECS query that combines native CLR components and dynamic Astra schemas.
/// </summary>
public sealed record QueryDescriptor(
    IReadOnlyList<SchemaId> RequiredAstraSchemas,
    IReadOnlyList<SchemaId> ExcludedAstraSchemas,
    IReadOnlyList<Type> RequiredNativeTypes,
    IReadOnlyList<Type> ExcludedNativeTypes);

/// <summary>
/// Bridge interface enabling AstraGraph to query native CLR components in RobustToolbox ECS.
/// </summary>
public interface IEcsQueryBridge
{
    bool HasNativeComponent(int entityUid, Type clrComponentType);

    object? GetNativeComponent(int entityUid, Type clrComponentType);

    IReadOnlyList<int> GetEntitiesWithNativeComponent(Type clrComponentType);
}

/// <summary>
/// In-memory ECS query bridge for standalone execution, tests, and mock simulations.
/// </summary>
public sealed class InMemoryEcsQueryBridge : IEcsQueryBridge
{
    private readonly Dictionary<(int Entity, Type CompType), object> _components = [];
    private readonly Dictionary<Type, HashSet<int>> _entitiesByType = [];
    private readonly Lock _lock = new();

    public void AddComponent(int entityUid, object component)
    {
        ArgumentNullException.ThrowIfNull(component);
        var type = component.GetType();

        lock (_lock)
        {
            _components[(entityUid, type)] = component;
            if (!_entitiesByType.TryGetValue(type, out var set))
            {
                set = [];
                _entitiesByType[type] = set;
            }
            set.Add(entityUid);
        }
    }

    public void RemoveComponent(int entityUid, Type clrComponentType)
    {
        lock (_lock)
        {
            _components.Remove((entityUid, clrComponentType));
            if (_entitiesByType.TryGetValue(clrComponentType, out var set))
            {
                set.Remove(entityUid);
            }
        }
    }

    public bool HasNativeComponent(int entityUid, Type clrComponentType)
    {
        lock (_lock) return _components.ContainsKey((entityUid, clrComponentType));
    }

    public object? GetNativeComponent(int entityUid, Type clrComponentType)
    {
        lock (_lock) return _components.GetValueOrDefault((entityUid, clrComponentType));
    }

    public IReadOnlyList<int> GetEntitiesWithNativeComponent(Type clrComponentType)
    {
        lock (_lock)
        {
            if (_entitiesByType.TryGetValue(clrComponentType, out var set))
            {
                return [.. set];
            }
            return [];
        }
    }
}
