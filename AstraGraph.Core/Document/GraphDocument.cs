namespace AstraGraph.Core;

/// <summary>
/// Root document model for an AstraGraph file (.agraph).
/// Represents the complete source of truth for a visual graph.
/// </summary>
public sealed class GraphDocument : IEquatable<GraphDocument>
{
    public const string CurrentFormatVersion = "1.0";

    public string FormatVersion { get; init; } = CurrentFormatVersion;

    public GraphId Id { get; init; } = GraphId.New();

    public string Name { get; init; } = string.Empty;

    public GraphKind Kind { get; init; } = GraphKind.System;

    public GraphSide Side { get; init; } = GraphSide.Server;

    public GraphMetadata Metadata { get; init; } = new();

    public List<GraphVariableDocument> Variables { get; init; } = [];

    public List<NodeDocument> Nodes { get; init; } = [];

    public List<ConnectionDocument> Connections { get; init; } = [];

    public EditorLayoutDocument EditorLayout { get; init; } = new();

    /// <summary>
    /// Component and struct schemas declared by this graph. The component name is the SS14 YAML type.
    /// </summary>
    public List<ComponentSchemaDocument> Schemas { get; init; } = [];

    public NodeDocument? FindNode(NodeId nodeId) => Nodes.FirstOrDefault(n => n.Id == nodeId);

    public GraphVariableDocument? FindVariable(SymbolId symbolId) =>
        Variables.FirstOrDefault(v => v.Id == symbolId);

    public GraphVariableDocument? FindVariable(string name) =>
        Variables.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.Ordinal));

    public IEnumerable<ConnectionDocument> GetIncomingConnections(NodeId toNode, PinId toPin) =>
        Connections.Where(c => c.ToNode == toNode && c.ToPin == toPin);

    public IEnumerable<ConnectionDocument> GetOutgoingConnections(NodeId fromNode, PinId fromPin) =>
        Connections.Where(c => c.FromNode == fromNode && c.FromPin == fromPin);

    public bool Equals(GraphDocument? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return FormatVersion == other.FormatVersion &&
               Id == other.Id &&
               Name == other.Name &&
               Kind == other.Kind &&
               Side == other.Side &&
               Metadata.Equals(other.Metadata) &&
               Variables.SequenceEqual(other.Variables) &&
               Nodes.SequenceEqual(other.Nodes) &&
               Connections.SequenceEqual(other.Connections) &&
               EditorLayout.Equals(other.EditorLayout) &&
               Schemas.SequenceEqual(other.Schemas);
    }

    public override bool Equals(object? obj) => Equals(obj as GraphDocument);

    public override int GetHashCode() => HashCode.Combine(Id, Name, Kind, Side);
}
