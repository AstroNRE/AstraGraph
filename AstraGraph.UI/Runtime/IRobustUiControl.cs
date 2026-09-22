using AstraGraph.UI.Model;

namespace AstraGraph.UI.Runtime;

/// <summary>
/// Abstraction for a Robust UI control, enabling identical headless execution in tests and native rendering in SS14 client.
/// </summary>
public interface IRobustUiControl
{
    string Id { get; }
    UiElementType ElementType { get; }
    string? Name { get; set; }
    string? Text { get; set; }
    bool Visible { get; set; }
    bool Enabled { get; set; }
    IRobustUiControl? Parent { get; set; }
    IReadOnlyList<IRobustUiControl> Children { get; }

    void AddChild(IRobustUiControl child);
    void RemoveChild(IRobustUiControl child);
    void SetProperty(string propertyName, object? value);
    object? GetProperty(string propertyName);

    event Action<string, object?>? OnEventTriggered;
    void TriggerEvent(string eventName, object? payload = null);
}

public interface IRobustUiControlFactory
{
    IRobustUiControl CreateControl(string id, UiElementType type, string? name, int? minWidth, int? minHeight, UiOrientation orientation);
}

/// <summary>
/// Default in-memory implementation of IRobustUiControl for tests and simulation.
/// </summary>
public sealed class MockRobustUiControl : IRobustUiControl
{
    public string Id { get; }
    public UiElementType ElementType { get; }
    public string? Name { get; set; }
    public string? Text { get; set; }
    public bool Visible { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public IRobustUiControl? Parent { get; set; }

    private readonly List<IRobustUiControl> _children = [];
    public IReadOnlyList<IRobustUiControl> Children => _children;

    private readonly Dictionary<string, object?> _properties = new(StringComparer.OrdinalIgnoreCase);

    public event Action<string, object?>? OnEventTriggered;

    public MockRobustUiControl(string id, UiElementType type, string? name = null)
    {
        Id = id;
        ElementType = type;
        Name = name;
    }

    public void AddChild(IRobustUiControl child)
    {
        child.Parent = this;
        _children.Add(child);
    }

    public void RemoveChild(IRobustUiControl child)
    {
        if (_children.Remove(child))
        {
            child.Parent = null;
        }
    }

    public void SetProperty(string propertyName, object? value)
    {
        if (string.Equals(propertyName, "Text", StringComparison.OrdinalIgnoreCase))
        {
            Text = value?.ToString();
        }
        else if (string.Equals(propertyName, "Visible", StringComparison.OrdinalIgnoreCase))
        {
            Visible = value is bool b && b;
        }
        else if (string.Equals(propertyName, "Enabled", StringComparison.OrdinalIgnoreCase))
        {
            Enabled = value is bool b && b;
        }

        _properties[propertyName] = value;
    }

    public object? GetProperty(string propertyName)
    {
        if (string.Equals(propertyName, "Text", StringComparison.OrdinalIgnoreCase)) return Text;
        if (string.Equals(propertyName, "Visible", StringComparison.OrdinalIgnoreCase)) return Visible;
        if (string.Equals(propertyName, "Enabled", StringComparison.OrdinalIgnoreCase)) return Enabled;

        _properties.TryGetValue(propertyName, out var val);
        return val;
    }

    public void TriggerEvent(string eventName, object? payload = null)
    {
        OnEventTriggered?.Invoke(eventName, payload);
    }
}

public sealed class MockRobustUiControlFactory : IRobustUiControlFactory
{
    public IRobustUiControl CreateControl(string id, UiElementType type, string? name, int? minWidth, int? minHeight, UiOrientation orientation)
    {
        return new MockRobustUiControl(id, type, name);
    }
}
