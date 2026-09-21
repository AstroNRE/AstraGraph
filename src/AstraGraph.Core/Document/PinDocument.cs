namespace AstraGraph.Core;

/// <summary>
/// Serializable document representation of a Pin on a Node.
/// </summary>
public sealed class PinDocument : IEquatable<PinDocument>
{
    public PinId Id { get; init; } = PinId.New();

    public string Name { get; init; } = string.Empty;

    public PinDirection Direction { get; init; } = PinDirection.Input;

    public PinKind Kind { get; init; } = PinKind.Data;

    /// <summary>
    /// Type descriptor name (e.g. "System.Int32", "Robust.Shared.GameObjects.EntityUid", etc.)
    /// </summary>
    public string DataType { get; init; } = string.Empty;

    /// <summary>
    /// Optional default literal value when pin is disconnected.
    /// </summary>
    public string? DefaultValue { get; init; }

    public bool Equals(PinDocument? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Id == other.Id &&
               Name == other.Name &&
               Direction == other.Direction &&
               Kind == other.Kind &&
               DataType == other.DataType &&
               DefaultValue == other.DefaultValue;
    }

    public override bool Equals(object? obj) => Equals(obj as PinDocument);

    public override int GetHashCode() => HashCode.Combine(Id, Name, Direction, Kind, DataType, DefaultValue);
}
