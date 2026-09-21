using System.Collections.Generic;
using AstraGraph.Binding;
using AstraGraph.Core;

namespace AstraGraph.Editor.Core.Search;

public sealed record VisualPinDefinition(
    string Name,
    PinDirection Direction,
    PinKind Kind,
    string DataType);

public sealed class NodePaletteItem
{
    public string Id { get; }
    public string DisplayName { get; }
    public string Category { get; }
    public string NodeType { get; }
    public IReadOnlyList<VisualPinDefinition> Pins { get; }
    public bool IsPure { get; }
    public bool IsPredictionSafe { get; }
    public GraphSide Side { get; }
    public SecurityProfile RequiredProfile { get; }
    public int EstimatedCost { get; }
    public string Documentation { get; }
    public Dictionary<string, string> DefaultProperties { get; }

    public NodePaletteItem(
        string id,
        string displayName,
        string category,
        string nodeType,
        IReadOnlyList<VisualPinDefinition> pins,
        bool isPure = false,
        bool isPredictionSafe = true,
        GraphSide side = GraphSide.Server,
        SecurityProfile requiredProfile = SecurityProfile.Gameplay,
        int estimatedCost = 1,
        string documentation = "",
        Dictionary<string, string>? defaultProperties = null)
    {
        Id = id;
        DisplayName = displayName;
        Category = category;
        NodeType = nodeType;
        Pins = pins;
        IsPure = isPure;
        IsPredictionSafe = isPredictionSafe;
        Side = side;
        RequiredProfile = requiredProfile;
        EstimatedCost = estimatedCost;
        Documentation = documentation;
        DefaultProperties = defaultProperties ?? [];
    }

    public VisualNode CreateVisualNode(CanvasPoint position, string? customName = null)
    {
        var node = new VisualNode(NodeId.New(), customName ?? DisplayName, NodeType, position);

        foreach (var pinDef in Pins)
        {
            node.Pins.Add(new VisualPin(
                PinId.New(),
                node.Id,
                pinDef.Name,
                pinDef.Direction,
                pinDef.Kind,
                pinDef.DataType));
        }

        foreach (var (k, v) in DefaultProperties)
        {
            node.Properties[k] = v;
        }

        return node;
    }
}
