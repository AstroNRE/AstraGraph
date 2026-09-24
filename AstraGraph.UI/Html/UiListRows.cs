using System.Text.Json;
using AstraGraph.Core;

namespace AstraGraph.UI.Html;

/// <summary>
/// Turns a list of structs into the JSON a bound item list renders,
/// and reads one field back out of an action payload.
/// The client sends the id. The server graph decides what it means.
/// </summary>
public static class UiListRows
{
    public const int MaxRows = 256;

    public static string Format(AstraList? list, string? idField, string? textField, string? disabledField)
    {
        if (list == null)
        {
            return "[]";
        }

        var idName = string.IsNullOrWhiteSpace(idField) ? "Id" : idField;
        var textName = string.IsNullOrWhiteSpace(textField) ? "Text" : textField;
        var disabledName = string.IsNullOrWhiteSpace(disabledField) ? "" : disabledField;
        var rows = new List<Dictionary<string, object?>>(Math.Min(list.Count, MaxRows));
        var count = Math.Min(list.Count, MaxRows);
        for (var i = 0; i < count; i++)
        {
            var value = list.Get(i);
            var id = Text(value, idName);
            var text = value.AsObject() is AstraStruct ? Text(value, textName) : id;
            var disabled = disabledName.Length > 0 && Flag(AstraValues.GetField(value, disabledName));
            rows.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["id"] = id,
                ["text"] = text,
                ["disabled"] = disabled
            });
        }

        return JsonSerializer.Serialize(rows);
    }

    public static string Field(string? payload, string? name)
    {
        if (string.IsNullOrWhiteSpace(payload) || string.IsNullOrWhiteSpace(name))
        {
            return "";
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(name, out var property))
            {
                return "";
            }

            return property.ValueKind switch
            {
                JsonValueKind.String => property.GetString() ?? "",
                JsonValueKind.Null => "",
                _ => property.ToString()
            };
        }
        catch (JsonException)
        {
            return "";
        }
    }

    private static string Text(AstraValue value, string field)
    {
        var fieldValue = value.AsObject() is AstraStruct ? AstraValues.GetField(value, field) : value;
        if (fieldValue.Type == AstraValueType.Null)
        {
            return "";
        }

        if (fieldValue.Type == AstraValueType.PersistentId)
        {
            return fieldValue.AsPersistentId().Value.ToString("D");
        }

        return fieldValue.ToString() ?? "";
    }

    private static bool Flag(AstraValue value) =>
        value.Type == AstraValueType.Bool && value.AsBool();
}
