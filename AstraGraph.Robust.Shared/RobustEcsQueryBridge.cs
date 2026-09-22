using AstraGraph.Core;
using AstraGraph.Runtime;
using Robust.Shared.GameObjects;

namespace AstraGraph.Robust.Shared;

/// <summary>
/// Connects AstraGraph's MixedQueryEngine directly to RobustToolbox's IEntityManager,
/// enabling seamless querying of native CLR components alongside dynamic Astra schemas.
/// </summary>
public sealed class RobustEcsQueryBridge : IEcsQueryBridge
{
    private readonly IEntityManager _entityManager;

    public RobustEcsQueryBridge(IEntityManager entityManager)
    {
        _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
    }

    public bool HasNativeComponent(AstraEntityId entityUid, Type clrComponentType)
    {
        ArgumentNullException.ThrowIfNull(clrComponentType);
        var uid = new EntityUid(entityUid.Value);
        return _entityManager.HasComponent(uid, clrComponentType);
    }

    public object? GetNativeComponent(AstraEntityId entityUid, Type clrComponentType)
    {
        ArgumentNullException.ThrowIfNull(clrComponentType);
        var uid = new EntityUid(entityUid.Value);
        return _entityManager.TryGetComponent(uid, clrComponentType, out var comp) ? comp : null;
    }

    public IReadOnlyList<AstraEntityId> GetEntitiesWithNativeComponent(Type clrComponentType)
    {
        ArgumentNullException.ThrowIfNull(clrComponentType);
        var result = new List<AstraEntityId>();
        var enumerator = _entityManager.AllEntityQueryEnumerator(clrComponentType);
        while (enumerator.MoveNext(out var uid, out _))
        {
            result.Add(new AstraEntityId((int)uid));
        }
        return result;
    }
}
