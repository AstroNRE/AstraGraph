using System.Globalization;
using System.Text.Json.Serialization;
using AstraGraph.UI.Catalog;

namespace AstraGraph.UI.Model;

/// <summary>
/// One node in an Astra UI view tree.
/// <see cref="ControlTypeId"/> is the identity. Legacy fields project onto <see cref="Properties"/>.
/// Only values the author changed are stored.
/// </summary>
public sealed class UiElementNode
{
    private readonly Dictionary<string, object?> _properties = new(StringComparer.OrdinalIgnoreCase);
    private string _controlTypeId = UiControlIds.Window;

    public required string Id { get; init; }

    public string ControlTypeId
    {
        get => _controlTypeId;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Control type id is required.", nameof(value));
            }

            _controlTypeId = value;
        }
    }

    [JsonIgnore]
    public UiElementType ElementType
    {
        get => UiControlIds.ToLegacy(_controlTypeId);
        set => _controlTypeId = UiControlIds.FromLegacy(value);
    }

    public string? Name { get; set; }

    public Dictionary<string, object?> Properties
    {
        get => _properties;
        set
        {
            _properties.Clear();
            if (value == null)
            {
                return;
            }

            foreach (var pair in value)
            {
                _properties[pair.Key] = pair.Value;
            }
        }
    }

    public List<string> StyleClasses { get; set; } = [];

    public List<UiElementNode> Children { get; set; } = [];

    [JsonIgnore]
    public string? Text
    {
        get => ReadString("Text");
        set => Write("Text", value, value != null);
    }

    [JsonIgnore]
    public bool Visible
    {
        get => ReadBool("Visible", true);
        set => Write("Visible", value, !value);
    }

    [JsonIgnore]
    public bool Enabled
    {
        get => ReadBool("Enabled", true);
        set => Write("Enabled", value, !value);
    }

    [JsonIgnore]
    public UiOrientation Orientation
    {
        get => Enum.TryParse<UiOrientation>(ReadString("Orientation"), ignoreCase: true, out var orientation)
            ? orientation
            : UiOrientation.Vertical;
        set => Write("Orientation", value.ToString(), value != UiOrientation.Vertical);
    }

    [JsonIgnore]
    public int? MinWidth
    {
        get => ReadInt("MinWidth");
        set => Write("MinWidth", value, value.HasValue);
    }

    [JsonIgnore]
    public int? MinHeight
    {
        get => ReadInt("MinHeight");
        set => Write("MinHeight", value, value.HasValue);
    }

    [JsonIgnore]
    public Dictionary<string, object?> CustomProperties
    {
        get => new(_properties, StringComparer.OrdinalIgnoreCase);
        set
        {
            if (value == null)
            {
                return;
            }

            foreach (var pair in value)
            {
                _properties[pair.Key] = pair.Value;
            }
        }
    }

    public void AddChild(UiElementNode child)
    {
        ArgumentNullException.ThrowIfNull(child);
        Children.Add(child);
    }

    public bool TryGetProperty(string name, out object? value) => _properties.TryGetValue(name, out value);

    public void SetAuthoredProperty(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (value is null)
        {
            _properties.Remove(name);
            return;
        }

        _properties[name] = value;
    }

    public void ResetProperty(string name) => _properties.Remove(name);

    private string? ReadString(string name) =>
        _properties.TryGetValue(name, out var value) ? value?.ToString() : null;

    private bool ReadBool(string name, bool fallback)
    {
        if (!_properties.TryGetValue(name, out var value) || value is null)
        {
            return fallback;
        }

        if (value is bool flag)
        {
            return flag;
        }

        return bool.TryParse(value.ToString(), out var parsed) ? parsed : fallback;
    }

    private int? ReadInt(string name)
    {
        if (!_properties.TryGetValue(name, out var value) || value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    private void Write(string name, object? value, bool store)
    {
        if (!store)
        {
            _properties.Remove(name);
            return;
        }

        _properties[name] = value;
    }
}
