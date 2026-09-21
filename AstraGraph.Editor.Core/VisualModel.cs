using System.Collections.Generic;
using AstraGraph.Core;

namespace AstraGraph.Editor.Core;

public sealed class VisualPin
{
    public PinId Id { get; }
    public NodeId NodeId { get; }
    public string Name { get; }
    public PinDirection Direction { get; }
    public PinKind Kind { get; }
    public string DataType { get; }

    public VisualPin(PinId id, NodeId nodeId, string name, PinDirection direction, PinKind kind, string dataType)
    {
        Id = id;
        NodeId = nodeId;
        Name = name;
        Direction = direction;
        Kind = kind;
        DataType = dataType;
    }
}

public sealed class VisualNode
{
    public NodeId Id { get; }
    public string Name { get; set; }
    public string NodeType { get; set; }
    public CanvasPoint Position { get; set; }
    public float Width { get; set; } = 180f;
    public float Height { get; set; } = 80f;
    public bool IsSelected { get; set; }

    public List<VisualPin> Pins { get; } = [];
    public Dictionary<string, string> Properties { get; } = [];

    public VisualNode(NodeId id, string name, string nodeType, CanvasPoint position)
    {
        Id = id;
        Name = name;
        NodeType = nodeType;
        Position = position;
    }

    public CanvasRect Bounds => new(Position.X, Position.Y, Width, Height);

    public CanvasPoint GetPinAnchorPosition(PinId pinId)
    {
        var pinIndex = Pins.FindIndex(p => p.Id == pinId);
        if (pinIndex < 0)
        {
            return Position;
        }

        var pin = Pins[pinIndex];
        var x = pin.Direction == PinDirection.Input ? Position.X : Position.X + Width;
        var y = Position.Y + 30f + (pinIndex * 20f);
        return new CanvasPoint(x, y);
    }
}

public sealed record VisualConnection(
    PinId SourcePinId,
    PinId TargetPinId,
    IReadOnlyList<CanvasPoint>? ReroutePoints = null);
