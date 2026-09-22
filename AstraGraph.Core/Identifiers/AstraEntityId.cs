using System.Text.Json;
using System.Text.Json.Serialization;

namespace AstraGraph.Core;

/// <summary>
/// Engine-neutral entity identity. Robust <c>EntityUid</c> converts to this value at the integration boundary.
/// </summary>
[JsonConverter(typeof(AstraEntityIdJsonConverter))]
public readonly record struct AstraEntityId(int Value) : IComparable<AstraEntityId>, IEquatable<AstraEntityId>
{
    public static readonly AstraEntityId Invalid = new(0);

    public bool IsValid => Value > 0;

    public static AstraEntityId From(int value) => new(value);

    public int CompareTo(AstraEntityId other) => Value.CompareTo(other.Value);

    public static bool operator <(AstraEntityId left, AstraEntityId right) => left.CompareTo(right) < 0;
    public static bool operator <=(AstraEntityId left, AstraEntityId right) => left.CompareTo(right) <= 0;
    public static bool operator >(AstraEntityId left, AstraEntityId right) => left.CompareTo(right) > 0;
    public static bool operator >=(AstraEntityId left, AstraEntityId right) => left.CompareTo(right) >= 0;

    public static implicit operator AstraEntityId(int value) => new(value);

    public static implicit operator int(AstraEntityId id) => id.Value;

    public override string ToString() => Value.ToString();
}

public sealed class AstraEntityIdJsonConverter : JsonConverter<AstraEntityId>
{
    public override AstraEntityId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
        {
            return new AstraEntityId(number);
        }

        if (reader.TokenType == JsonTokenType.String && int.TryParse(reader.GetString(), out var parsed))
        {
            return new AstraEntityId(parsed);
        }

        throw new JsonException("AstraEntityId must be an integer.");
    }

    public override void Write(Utf8JsonWriter writer, AstraEntityId value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Value);
}
