using AstraGraph.Core;
using Robust.Shared.GameObjects;

namespace AstraGraph.Robust.Shared;

/// <summary>
/// Converts between the engine-neutral entity id and Robust EntityUid.
/// Core and the VM never see EntityUid.
/// </summary>
public static class RobustEntityMap
{
    public static EntityUid ToUid(AstraEntityId id) => new(id.Value);

    public static AstraEntityId ToAstra(EntityUid uid) => new((int)uid);
}
