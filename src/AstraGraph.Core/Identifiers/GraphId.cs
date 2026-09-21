using System.Text.Json;
using System.Text.Json.Serialization;

namespace AstraGraph.Core;

/// <summary>
/// Strongly-typed identifier for a Graph document.
/// </summary>
[JsonConverter(typeof(GraphIdJsonConverter))]
public readonly record struct GraphId(Guid Value) : IComparable<GraphId>, IEquatable<GraphId>
{
    public static readonly GraphId Empty = new(Guid.Empty);

    public static GraphId New() => new(Guid.NewGuid());

    public static GraphId FromString(string value) => new(Guid.Parse(value));

    public static bool TryParse(string? value, out GraphId result)
    {
        if (Guid.TryParse(value, out var guid))
        {
            result = new GraphId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public int CompareTo(GraphId other) => Value.CompareTo(other.Value);

    public static bool operator <(GraphId left, GraphId right) => left.CompareTo(right) < 0;
    public static bool operator <=(GraphId left, GraphId right) => left.CompareTo(right) <= 0;
    public static bool operator >(GraphId left, GraphId right) => left.CompareTo(right) > 0;
    public static bool operator >=(GraphId left, GraphId right) => left.CompareTo(right) >= 0;

    public override string ToString() => Value.ToString("D");
}

public sealed class GraphIdJsonConverter : JsonConverter<GraphId>
{
    public override GraphId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();
        return str != null && Guid.TryParse(str, out var guid) ? new GraphId(guid) : GraphId.Empty;
    }

    public override void Write(Utf8JsonWriter writer, GraphId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
