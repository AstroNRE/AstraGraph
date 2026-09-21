namespace AstraGraph.Core;

/// <summary>
/// Serializable document representation of a Node within a Graph.
/// </summary>
public sealed class NodeDocument : IEquatable<NodeDocument>
{
    public NodeId Id { get; init; } = NodeId.New();

    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Type identifier for the node (e.g. "Core.Branch", "Native.Call", "Math.Add", etc.)
    /// </summary>
    public string NodeType { get; init; } = string.Empty;

    public List<PinDocument> Pins { get; init; } = [];

    /// <summary>
    /// Configurable node properties / compile-time constants.
    /// </summary>
    public Dictionary<string, string> Properties { get; init; } = [];

    public PinDocument? FindPin(PinId pinId) => Pins.FirstOrDefault(p => p.Id == pinId);

    public PinDocument? FindPin(string pinName, PinDirection direction) =>
        Pins.FirstOrDefault(p => p.Name == pinName && p.Direction == direction);

    public bool Equals(NodeDocument? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Id == other.Id &&
               Name == other.Name &&
               NodeType == other.NodeType &&
               Pins.SequenceEqual(other.Pins) &&
               Properties.OrderBy(kv => kv.Key).SequenceEqual(other.Properties.OrderBy(kv => kv.Key));
    }

    public override bool Equals(object? obj) => Equals(obj as NodeDocument);

    public override int GetHashCode() => HashCode.Combine(Id, Name, NodeType);
}
