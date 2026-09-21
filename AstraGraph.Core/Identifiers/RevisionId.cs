using System.Text.Json;
using System.Text.Json.Serialization;

namespace AstraGraph.Core;

/// <summary>
/// Strongly-typed identifier for an immutable published revision.
/// </summary>
[JsonConverter(typeof(RevisionIdJsonConverter))]
public readonly record struct RevisionId(Guid Value) : IComparable<RevisionId>, IEquatable<RevisionId>
{
    public static readonly RevisionId Empty = new(Guid.Empty);

    public static RevisionId New() => new(Guid.NewGuid());

    public static RevisionId FromString(string value) => new(Guid.Parse(value));

    public static bool TryParse(string? value, out RevisionId result)
    {
        if (Guid.TryParse(value, out var guid))
        {
            result = new RevisionId(guid);
            return true;
        }

        result = Empty;
        return false;
    }

    public int CompareTo(RevisionId other) => Value.CompareTo(other.Value);

    public static bool operator <(RevisionId left, RevisionId right) => left.CompareTo(right) < 0;
    public static bool operator <=(RevisionId left, RevisionId right) => left.CompareTo(right) <= 0;
    public static bool operator >(RevisionId left, RevisionId right) => left.CompareTo(right) > 0;
    public static bool operator >=(RevisionId left, RevisionId right) => left.CompareTo(right) >= 0;

    public override string ToString() => Value.ToString("D");
}

public sealed class RevisionIdJsonConverter : JsonConverter<RevisionId>
{
    public override RevisionId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();
        return str != null && Guid.TryParse(str, out var guid) ? new RevisionId(guid) : RevisionId.Empty;
    }

    public override void Write(Utf8JsonWriter writer, RevisionId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
