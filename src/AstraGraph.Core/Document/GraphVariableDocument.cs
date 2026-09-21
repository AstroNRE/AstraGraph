namespace AstraGraph.Core;

/// <summary>
/// Serializable definition of a graph variable (local, system, or persistent state).
/// </summary>
public sealed class GraphVariableDocument : IEquatable<GraphVariableDocument>
{
    public SymbolId Id { get; init; } = SymbolId.New();

    public string Name { get; init; } = string.Empty;

    public string TypeName { get; init; } = string.Empty;

    public string? DefaultValue { get; init; }

    /// <summary>
    /// Whether this variable state should survive round / server restart.
    /// </summary>
    public bool IsPersistent { get; init; }

    /// <summary>
    /// Whether this variable is synchronized across server and client.
    /// </summary>
    public bool IsReplicated { get; init; }

    public bool Equals(GraphVariableDocument? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Id == other.Id &&
               Name == other.Name &&
               TypeName == other.TypeName &&
               DefaultValue == other.DefaultValue &&
               IsPersistent == other.IsPersistent &&
               IsReplicated == other.IsReplicated;
    }

    public override bool Equals(object? obj) => Equals(obj as GraphVariableDocument);

    public override int GetHashCode() => HashCode.Combine(Id, Name, TypeName, IsPersistent, IsReplicated);
}
