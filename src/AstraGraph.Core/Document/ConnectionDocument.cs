namespace AstraGraph.Core;

/// <summary>
/// Serializable connection between an output pin and an input pin.
/// </summary>
public sealed class ConnectionDocument : IEquatable<ConnectionDocument>
{
    public NodeId FromNode { get; init; }

    public PinId FromPin { get; init; }

    public NodeId ToNode { get; init; }

    public PinId ToPin { get; init; }

    public bool Equals(ConnectionDocument? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return FromNode == other.FromNode &&
               FromPin == other.FromPin &&
               ToNode == other.ToNode &&
               ToPin == other.ToPin;
    }

    public override bool Equals(object? obj) => Equals(obj as ConnectionDocument);

    public override int GetHashCode() => HashCode.Combine(FromNode, FromPin, ToNode, ToPin);
}

/// <summary>
/// Canonical comparer for deterministic ordering of connections.
/// </summary>
public sealed class ConnectionComparer : IComparer<ConnectionDocument>
{
    public static readonly ConnectionComparer Instance = new();

    public int Compare(ConnectionDocument? x, ConnectionDocument? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        var cmp = x.FromNode.CompareTo(y.FromNode);
        if (cmp != 0) return cmp;
        cmp = x.FromPin.CompareTo(y.FromPin);
        if (cmp != 0) return cmp;
        cmp = x.ToNode.CompareTo(y.ToNode);
        if (cmp != 0) return cmp;
        return x.ToPin.CompareTo(y.ToPin);
    }
}
