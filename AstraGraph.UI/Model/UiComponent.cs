namespace AstraGraph.UI.Model;

/// <summary>
/// A reusable piece of a UI tree. Instances receive new element ids.
/// </summary>
public sealed class UiComponentDefinition
{
    public required string Id { get; init; }

    public required string Name { get; set; }

    public required UiElementNode Root { get; set; }

    public List<UiComponentInput> Inputs { get; set; } = [];

    public List<UiComponentOutput> Outputs { get; set; } = [];
}

public sealed class UiComponentInput
{
    public required string Name { get; init; }

    public string TypeName { get; set; } = "string";

    public string? TargetElementId { get; set; }

    public string PropertyName { get; set; } = "Text";
}

public sealed class UiComponentOutput
{
    public required string Name { get; init; }

    public string? ElementId { get; set; }

    public string EventName { get; set; } = "OnPressed";
}

public static class UiComponents
{
    public static UiElementNode Instantiate(UiComponentDefinition component, IReadOnlyDictionary<string, string>? inputs = null)
    {
        ArgumentNullException.ThrowIfNull(component);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var copy = Clone(component.Root, map);
        if (inputs == null)
        {
            return copy;
        }

        foreach (var input in component.Inputs)
        {
            if (!inputs.TryGetValue(input.Name, out var value) || string.IsNullOrEmpty(input.TargetElementId) || !map.TryGetValue(input.TargetElementId, out var targetId))
            {
                continue;
            }

            var target = Find(copy, targetId);
            if (target != null)
            {
                target.Properties[input.PropertyName] = value;
            }
        }

        return copy;
    }

    private static UiElementNode? Find(UiElementNode node, string id)
    {
        if (node.Id == id)
        {
            return node;
        }

        foreach (var child in node.Children)
        {
            var found = Find(child, id);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static UiElementNode Clone(UiElementNode node, Dictionary<string, string> map)
    {
        var copy = new UiElementNode
        {
            Id = Guid.NewGuid().ToString("D"),
            ControlTypeId = node.ControlTypeId,
            Name = node.Name,
            Properties = new Dictionary<string, object?>(node.Properties, StringComparer.OrdinalIgnoreCase),
            StyleClasses = [.. node.StyleClasses]
        };
        map[node.Id] = copy.Id;
        foreach (var child in node.Children)
        {
            copy.AddChild(Clone(child, map));
        }

        return copy;
    }
}
