using System.Text.Json;
using System.Text.Json.Serialization;

namespace AstraGraph.Core;

/// <summary>
/// Strongly-typed identifier for a reusable Symbol (Function, Variable, Type).
/// </summary>
[JsonConverter(typeof(SymbolIdJsonConverter))]
public readonly record struct SymbolId(Guid Value) : IComparable<SymbolId>, IEquatable<SymbolId>
{
    public static readonly SymbolId Empty = new(Guid.Empty);

    public static SymbolId New() => new(Guid.NewGuid());

    public static SymbolId FromString(string value) => new(Guid.Parse(value));

    public static bool TryParse(string? value, out SymbolId result)
    {
        if (Guid.TryParse(value, out var guid))
        {
            result = new SymbolId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public int CompareTo(SymbolId other) => Value.CompareTo(other.Value);

    public static bool operator <(SymbolId left, SymbolId right) => left.CompareTo(right) < 0;
    public static bool operator <=(SymbolId left, SymbolId right) => left.CompareTo(right) <= 0;
    public static bool operator >(SymbolId left, SymbolId right) => left.CompareTo(right) > 0;
    public static bool operator >=(SymbolId left, SymbolId right) => left.CompareTo(right) >= 0;

    public override string ToString() => Value.ToString("D");
}

public sealed class SymbolIdJsonConverter : JsonConverter<SymbolId>
{
    public override SymbolId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();
        return str != null && Guid.TryParse(str, out var guid) ? new SymbolId(guid) : SymbolId.Empty;
    }

    public override void Write(Utf8JsonWriter writer, SymbolId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
