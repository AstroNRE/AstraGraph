using System.Text.Json;
using System.Text.Json.Serialization;

namespace AstraGraph.Core;

/// <summary>
/// Strongly-typed identifier for a Pin on a Node.
/// </summary>
[JsonConverter(typeof(PinIdJsonConverter))]
public readonly record struct PinId(Guid Value) : IComparable<PinId>, IEquatable<PinId>
{
    public static readonly PinId Empty = new(Guid.Empty);

    public static PinId New() => new(Guid.NewGuid());

    public static PinId FromString(string value) => new(Guid.Parse(value));

    public static bool TryParse(string? value, out PinId result)
    {
        if (Guid.TryParse(value, out var guid))
        {
            result = new PinId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public int CompareTo(PinId other) => Value.CompareTo(other.Value);

    public static bool operator <(PinId left, PinId right) => left.CompareTo(right) < 0;
    public static bool operator <=(PinId left, PinId right) => left.CompareTo(right) <= 0;
    public static bool operator >(PinId left, PinId right) => left.CompareTo(right) > 0;
    public static bool operator >=(PinId left, PinId right) => left.CompareTo(right) >= 0;

    public override string ToString() => Value.ToString("D");
}

public sealed class PinIdJsonConverter : JsonConverter<PinId>
{
    public override PinId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();
        return str != null && Guid.TryParse(str, out var guid) ? new PinId(guid) : PinId.Empty;
    }

    public override void Write(Utf8JsonWriter writer, PinId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
