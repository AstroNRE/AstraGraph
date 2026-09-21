using System.Text.Json;
using System.Text.Json.Serialization;

namespace AstraGraph.Core;

/// <summary>
/// Strongly-typed identifier for a field within a Schema.
/// </summary>
[JsonConverter(typeof(FieldIdJsonConverter))]
public readonly record struct FieldId(Guid Value) : IComparable<FieldId>, IEquatable<FieldId>
{
    public static readonly FieldId Empty = new(Guid.Empty);

    public static FieldId New() => new(Guid.NewGuid());

    public static FieldId FromString(string value) => new(Guid.Parse(value));

    public static bool TryParse(string? value, out FieldId result)
    {
        if (Guid.TryParse(value, out var guid))
        {
            result = new FieldId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public int CompareTo(FieldId other) => Value.CompareTo(other.Value);

    public static bool operator <(FieldId left, FieldId right) => left.CompareTo(right) < 0;
    public static bool operator <=(FieldId left, FieldId right) => left.CompareTo(right) <= 0;
    public static bool operator >(FieldId left, FieldId right) => left.CompareTo(right) > 0;
    public static bool operator >=(FieldId left, FieldId right) => left.CompareTo(right) >= 0;

    public override string ToString() => Value.ToString("D");
}

public sealed class FieldIdJsonConverter : JsonConverter<FieldId>
{
    public override FieldId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();
        return str != null && Guid.TryParse(str, out var guid) ? new FieldId(guid) : FieldId.Empty;
    }

    public override void Write(Utf8JsonWriter writer, FieldId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
