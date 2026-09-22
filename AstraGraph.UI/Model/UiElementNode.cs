using System.Text.Json.Serialization;

namespace AstraGraph.UI.Model;

/// <summary>
/// A node representing a visual UI element in the Astra UI view tree.
/// </summary>
public sealed class UiElementNode
{
    public required string Id { get; init; }
    public required UiElementType ElementType { get; init; }
    public string? Name { get; set; }
    public string? Text { get; set; }
    public bool Visible { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public UiOrientation Orientation { get; set; } = UiOrientation.Vertical;
    public int? MinWidth { get; set; }
    public int? MinHeight { get; set; }
    public List<string> StyleClasses { get; set; } = [];
    public Dictionary<string, object?> CustomProperties { get; set; } = [];
    public List<UiElementNode> Children { get; set; } = [];

    public void AddChild(UiElementNode child)
    {
        Children.Add(child);
    }
}
