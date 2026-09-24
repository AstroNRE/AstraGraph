using System.Globalization;

namespace AstraGraph.UI.Runtime;

/// <summary>
/// Compact typed encoding for BUI state values. Untyped legacy strings stay on the old channel.
/// </summary>
public static class UiValueCodec
{
    public static string Encode(object? value) => value switch
    {
        null => "null",
        bool flag => flag ? "bool:true" : "bool:false",
        int number => "int:" + number.ToString(CultureInfo.InvariantCulture),
        long number => "int:" + number.ToString(CultureInfo.InvariantCulture),
        float number => "float:" + number.ToString(CultureInfo.InvariantCulture),
        double number => "float:" + number.ToString(CultureInfo.InvariantCulture),
        _ => "string:" + value
    };

    public static object? Decode(string? encoded)
    {
        if (string.IsNullOrEmpty(encoded) || encoded == "null")
        {
            return null;
        }

        var split = encoded.IndexOf(':');
        if (split <= 0)
        {
            return encoded;
        }

        var kind = encoded[..split];
        var rest = encoded[(split + 1)..];
        return kind switch
        {
            "bool" => bool.TryParse(rest, out var flag) ? flag : rest,
            "int" => int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : rest,
            "float" => float.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : rest,
            "string" => rest,
            _ => encoded
        };
    }

    public static Dictionary<string, string> EncodeMap(IReadOnlyDictionary<string, object?> values) =>
        values.ToDictionary(pair => pair.Key, pair => Encode(pair.Value), StringComparer.Ordinal);

    public static Dictionary<string, object?> DecodeMap(IReadOnlyDictionary<string, string> values) =>
        values.ToDictionary(pair => pair.Key, pair => Decode(pair.Value), StringComparer.Ordinal);
}
