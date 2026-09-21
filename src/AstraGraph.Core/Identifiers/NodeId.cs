using System.Text.Json;
using System.Text.Json.Serialization;

namespace AstraGraph.Core;

/// <summary>
/// Strongly-typed identifier for a Node within a Graph.
/// </summary>
[JsonConverter(typeof(NodeIdJsonConverter))]
public readonly record struct NodeId(Guid Value) : IComparable<NodeId>, IEquatable<NodeId>
{
    public static readonly NodeId Empty = new(Guid.Empty);

    public static NodeId New() => new(Guid.NewGuid());

    public static NodeId FromString(string value) => new(Guid.Parse(value));

    public static bool TryParse(string? value, out NodeId result)
    {
        if (Guid.TryParse(value, out var guid))
        {
            result = new NodeId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public int CompareTo(NodeId other) => Value.CompareTo(other.Value);

    public static bool operator <(NodeId left, NodeId right) => left.CompareTo(right) < 0;
    public static bool operator <=(NodeId left, NodeId right) => left.CompareTo(right) <= 0;
    public static bool operator >(NodeId left, NodeId right) => left.CompareTo(right) > 0;
    public static bool operator >=(NodeId left, NodeId right) => left.CompareTo(right) >= 0;

    public override string ToString() => Value.ToString("D");
}

public sealed class NodeIdJsonConverter : JsonConverter<NodeId>
{
    public override NodeId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();
        return str != null && Guid.TryParse(str, out var guid) ? new NodeId(guid) : NodeId.Empty;
    }

    public override void Write(Utf8JsonWriter writer, NodeId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
