using System.Text.Json;
using System.Text.Json.Serialization;

namespace AstraGraph.Core;

/// <summary>
/// Serializes and deserializes GraphDocument to and from canonical JSON (.agraph).
/// </summary>
public static class GraphSerializer
{
    private static readonly JsonSerializerOptions DefaultOptions = CreateOptions(indented: true);
    private static readonly JsonSerializerOptions CompactOptions = CreateOptions(indented: false);

    private static JsonSerializerOptions CreateOptions(bool indented)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = indented,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
                new GraphIdJsonConverter(),
                new NodeIdJsonConverter(),
                new PinIdJsonConverter(),
                new SymbolIdJsonConverter(),
                new RevisionIdJsonConverter(),
                new SchemaIdJsonConverter(),
                new FieldIdJsonConverter()
            }
        };
        return options;
    }

    /// <summary>
    /// Serializes a GraphDocument to a JSON string.
    /// </summary>
    public static string Serialize(GraphDocument document, bool writeIndented = true)
    {
        ArgumentNullException.ThrowIfNull(document);
        var options = writeIndented ? DefaultOptions : CompactOptions;
        return JsonSerializer.Serialize(document, options);
    }

    /// <summary>
    /// Serializes a GraphDocument to UTF-8 byte array.
    /// </summary>
    public static byte[] SerializeToUtf8Bytes(GraphDocument document, bool writeIndented = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        var options = writeIndented ? DefaultOptions : CompactOptions;
        return JsonSerializer.SerializeToUtf8Bytes(document, options);
    }

    /// <summary>
    /// Deserializes a GraphDocument from a JSON string.
    /// </summary>
    public static GraphDocument Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var doc = JsonSerializer.Deserialize<GraphDocument>(json, DefaultOptions);
        if (doc is null)
        {
            throw new JsonException("Failed to deserialize GraphDocument: payload returned null.");
        }
        return doc;
    }

    /// <summary>
    /// Deserializes a GraphDocument from UTF-8 byte span.
    /// </summary>
    public static GraphDocument Deserialize(ReadOnlySpan<byte> utf8Json)
    {
        var doc = JsonSerializer.Deserialize<GraphDocument>(utf8Json, DefaultOptions);
        if (doc is null)
        {
            throw new JsonException("Failed to deserialize GraphDocument: payload returned null.");
        }
        return doc;
    }
}
