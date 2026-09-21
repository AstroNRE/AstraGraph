using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AstraGraph.Core;

/// <summary>
/// Cryptographic hashing utilities for SourceHash and SemanticHash.
/// </summary>
public static class AstraHash
{
    /// <summary>
    /// Computes the SHA-256 hash of raw source content bytes.
    /// </summary>
    public static string ComputeSourceHash(ReadOnlySpan<byte> sourceBytes)
    {
        Span<byte> hashBytes = stackalloc byte[32];
        SHA256.HashData(sourceBytes, hashBytes);
        return Convert.ToHexStringLower(hashBytes);
    }

    /// <summary>
    /// Computes the SHA-256 hash of raw source string.
    /// </summary>
    public static string ComputeSourceHash(string sourceText)
    {
        var bytes = Encoding.UTF8.GetBytes(sourceText);
        return ComputeSourceHash(bytes);
    }

    /// <summary>
    /// Computes the canonical SemanticHash of a graph document.
    /// Guarantees that node layout coordinates, canvas zoom, comments, and non-semantic metadata
    /// DO NOT affect the hash.
    /// </summary>
    public static string ComputeSemanticHash(GraphDocument document)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();

            writer.WriteString("formatVersion", document.FormatVersion);
            writer.WriteString("id", document.Id.ToString());
            writer.WriteString("name", document.Name);
            writer.WriteString("kind", document.Kind.ToString());
            writer.WriteString("side", document.Side.ToString());

            // Variables sorted by Id
            writer.WriteStartArray("variables");
            foreach (var v in document.Variables.OrderBy(x => x.Id.Value))
            {
                writer.WriteStartObject();
                writer.WriteString("id", v.Id.ToString());
                writer.WriteString("name", v.Name);
                writer.WriteString("typeName", v.TypeName);
                writer.WriteString("defaultValue", v.DefaultValue ?? string.Empty);
                writer.WriteBoolean("isPersistent", v.IsPersistent);
                writer.WriteBoolean("isReplicated", v.IsReplicated);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            // Nodes sorted by Id
            writer.WriteStartArray("nodes");
            foreach (var n in document.Nodes.OrderBy(x => x.Id.Value))
            {
                writer.WriteStartObject();
                writer.WriteString("id", n.Id.ToString());
                writer.WriteString("name", n.Name);
                writer.WriteString("nodeType", n.NodeType);

                // Pins sorted by Id
                writer.WriteStartArray("pins");
                foreach (var p in n.Pins.OrderBy(x => x.Id.Value))
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", p.Id.ToString());
                    writer.WriteString("name", p.Name);
                    writer.WriteString("direction", p.Direction.ToString());
                    writer.WriteString("kind", p.Kind.ToString());
                    writer.WriteString("dataType", p.DataType);
                    writer.WriteString("defaultValue", p.DefaultValue ?? string.Empty);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();

                // Properties sorted by key
                writer.WriteStartObject("properties");
                foreach (var prop in n.Properties.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    writer.WriteString(prop.Key, prop.Value);
                }
                writer.WriteEndObject();

                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            // Connections sorted canonically
            writer.WriteStartArray("connections");
            foreach (var c in document.Connections.OrderBy(x => x, ConnectionComparer.Instance))
            {
                writer.WriteStartObject();
                writer.WriteString("fromNode", c.FromNode.ToString());
                writer.WriteString("fromPin", c.FromPin.ToString());
                writer.WriteString("toNode", c.ToNode.ToString());
                writer.WriteString("toPin", c.ToPin.ToString());
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
            writer.Flush();
        }

        return ComputeSourceHash(stream.ToArray());
    }
}
