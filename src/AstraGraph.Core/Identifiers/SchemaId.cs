using System.Text.Json;
using System.Text.Json.Serialization;

namespace AstraGraph.Core;

/// <summary>
/// Strongly-typed identifier for a dynamic component or struct schema.
/// </summary>
[JsonConverter(typeof(SchemaIdJsonConverter))]
public readonly record struct SchemaId(Guid Value) : IComparable<SchemaId>, IEquatable<SchemaId>
{
    public static readonly SchemaId Empty = new(Guid.Empty);

    public static SchemaId New() => new(Guid.NewGuid());

    public static SchemaId FromString(string value) => new(Guid.Parse(value));

    public static bool TryParse(string? value, out SchemaId result)
    {
        if (Guid.TryParse(value, out var guid))
        {
            result = new SchemaId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public int CompareTo(SchemaId other) => Value.CompareTo(other.Value);

    public static bool operator <(SchemaId left, SchemaId right) => left.CompareTo(right) < 0;
    public static bool operator <=(SchemaId left, SchemaId right) => left.CompareTo(right) <= 0;
    public static bool operator >(SchemaId left, SchemaId right) => left.CompareTo(right) > 0;
    public static bool operator >=(SchemaId left, SchemaId right) => left.CompareTo(right) >= 0;

    public override string ToString() => Value.ToString("D");
}

public sealed class SchemaIdJsonConverter : JsonConverter<SchemaId>
{
    public override SchemaId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();
        return str != null && Guid.TryParse(str, out var guid) ? new SchemaId(guid) : SchemaId.Empty;
    }

    public override void Write(Utf8JsonWriter writer, SchemaId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
