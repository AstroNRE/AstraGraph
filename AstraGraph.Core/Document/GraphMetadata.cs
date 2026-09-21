namespace AstraGraph.Core;

/// <summary>
/// Authoring metadata, versioning, and documentation for a graph.
/// </summary>
public sealed class GraphMetadata : IEquatable<GraphMetadata>
{
    public string Author { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Version { get; init; } = "1.0.0";

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset ModifiedAt { get; init; } = DateTimeOffset.UtcNow;

    public List<string> Tags { get; init; } = [];

    public Dictionary<string, string> CustomAttributes { get; init; } = [];

    public bool Equals(GraphMetadata? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Author == other.Author &&
               Description == other.Description &&
               Version == other.Version &&
               Tags.SequenceEqual(other.Tags) &&
               CustomAttributes.OrderBy(kv => kv.Key).SequenceEqual(other.CustomAttributes.OrderBy(kv => kv.Key));
    }

    public override bool Equals(object? obj) => Equals(obj as GraphMetadata);

    public override int GetHashCode() => HashCode.Combine(Author, Description, Version);
}
