namespace AstraGraph.Core;

/// <summary>
/// Gameplay identity that survives a restart. It is not an <see cref="EntityType"/> entity id.
/// The same value names a gun, a car, a tool, or a container.
/// </summary>
public readonly record struct PersistentObjectId(Guid Value)
{
    public static PersistentObjectId Empty { get; } = new(Guid.Empty);

    public static PersistentObjectId New() => new(Guid.NewGuid());

    public bool IsEmpty => Value == Guid.Empty;

    public override string ToString() => Value.ToString("D");
}

/// <summary>
/// Graph type of <see cref="PersistentObjectId"/>.
/// </summary>
public sealed record PersistentIdType : AstraType
{
    public static readonly PersistentIdType Instance = new();

    public override string TypeName => "PersistentObjectId";

    public override bool IsValueType => true;
}
