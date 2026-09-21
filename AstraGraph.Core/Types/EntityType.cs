namespace AstraGraph.Core;

public enum EntityKind
{
    EntityUid,
    NetEntity
}

/// <summary>
/// First-class Entity identifiers in AstraGraph.
/// </summary>
public sealed record EntityType(EntityKind Kind, string Name) : AstraType
{
    public override string TypeName => Name;

    public static readonly EntityType EntityUid = new(EntityKind.EntityUid, "EntityUid");
    public static readonly EntityType NetEntity = new(EntityKind.NetEntity, "NetEntity");

    public override bool IsValueType => true;
}

public enum GeometryKind
{
    Vector2,
    Angle,
    EntityCoordinates,
    MapCoordinates
}

/// <summary>
/// Robust geometric and spatial types.
/// </summary>
public sealed record GeometryType(GeometryKind Kind, string Name) : AstraType
{
    public override string TypeName => Name;

    public static readonly GeometryType Vector2 = new(GeometryKind.Vector2, "Vector2");
    public static readonly GeometryType Angle = new(GeometryKind.Angle, "Angle");
    public static readonly GeometryType EntityCoordinates = new(GeometryKind.EntityCoordinates, "EntityCoordinates");
    public static readonly GeometryType MapCoordinates = new(GeometryKind.MapCoordinates, "MapCoordinates");

    public override bool IsValueType => true;
}
