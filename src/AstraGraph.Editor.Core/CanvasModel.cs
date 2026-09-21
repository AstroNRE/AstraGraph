using System;
using System.Collections.Generic;
using System.Linq;
using AstraGraph.Core;

namespace AstraGraph.Editor.Core;

public sealed class CanvasModel
{
    private readonly Dictionary<NodeId, VisualNode> _nodes = [];
    private readonly List<VisualConnection> _connections = [];

    public IReadOnlyDictionary<NodeId, VisualNode> Nodes => _nodes;
    public IReadOnlyList<VisualConnection> Connections => _connections;
    public CanvasViewport Viewport { get; } = new();

    public void AddNode(VisualNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _nodes[node.Id] = node;
    }

    public bool RemoveNode(NodeId nodeId)
    {
        if (_nodes.TryGetValue(nodeId, out var node))
        {
            var pinIds = node.Pins.Select(p => p.Id).ToHashSet();
            _connections.RemoveAll(c => pinIds.Contains(c.SourcePinId) || pinIds.Contains(c.TargetPinId));
            return _nodes.Remove(nodeId);
        }
        return false;
    }

    public void AddConnection(VisualConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!_connections.Contains(connection))
        {
            _connections.Add(connection);
        }
    }

    public bool RemoveConnection(VisualConnection connection)
    {
        return _connections.Remove(connection);
    }

    public VisualPin? FindPin(PinId pinId)
    {
        foreach (var node in _nodes.Values)
        {
            var pin = node.Pins.FirstOrDefault(p => p.Id == pinId);
            if (pin != null) return pin;
        }
        return null;
    }

    public VisualNode? GetNodeForPin(PinId pinId)
    {
        foreach (var node in _nodes.Values)
        {
            if (node.Pins.Any(p => p.Id == pinId))
            {
                return node;
            }
        }
        return null;
    }

    public void SelectNode(NodeId nodeId, bool addToSelection = false)
    {
        if (!addToSelection)
        {
            DeselectAll();
        }

        if (_nodes.TryGetValue(nodeId, out var node))
        {
            node.IsSelected = true;
        }
    }

    public void DeselectAll()
    {
        foreach (var node in _nodes.Values)
        {
            node.IsSelected = false;
        }
    }

    public void SelectArea(CanvasRect selectionRect)
    {
        DeselectAll();
        foreach (var node in _nodes.Values)
        {
            if (selectionRect.IntersectsWith(node.Bounds))
            {
                node.IsSelected = true;
            }
        }
    }

    public IReadOnlyList<VisualNode> GetSelectedNodes() =>
        _nodes.Values.Where(n => n.IsSelected).ToList();

    public GraphDocument ToGraphDocument(GraphId graphId, string name, GraphKind kind, GraphSide side)
    {
        var nodeDocs = new List<NodeDocument>();
        var nodePositions = new Dictionary<string, NodePosition>();

        foreach (var visualNode in _nodes.Values)
        {
            var pinDocs = visualNode.Pins.Select(p => new PinDocument
            {
                Id = p.Id,
                Name = p.Name,
                Direction = p.Direction,
                Kind = p.Kind,
                DataType = p.DataType
            }).ToList();

            var nodeDoc = new NodeDocument
            {
                Id = visualNode.Id,
                Name = visualNode.Name,
                NodeType = visualNode.NodeType,
                Pins = pinDocs
            };

            foreach (var (k, v) in visualNode.Properties)
            {
                nodeDoc.Properties[k] = v;
            }

            nodeDocs.Add(nodeDoc);
            nodePositions[visualNode.Id.ToString()] = new NodePosition(visualNode.Position.X, visualNode.Position.Y);
        }

        var connDocs = new List<ConnectionDocument>();
        foreach (var c in _connections)
        {
            var fromNode = GetNodeForPin(c.SourcePinId);
            var toNode = GetNodeForPin(c.TargetPinId);
            if (fromNode != null && toNode != null)
            {
                connDocs.Add(new ConnectionDocument
                {
                    FromNode = fromNode.Id,
                    FromPin = c.SourcePinId,
                    ToNode = toNode.Id,
                    ToPin = c.TargetPinId
                });
            }
        }

        var layout = new EditorLayoutDocument
        {
            ViewportX = Viewport.PanX,
            ViewportY = Viewport.PanY,
            Zoom = Viewport.Zoom,
            NodePositions = nodePositions
        };

        return new GraphDocument
        {
            Id = graphId,
            Name = name,
            Kind = kind,
            Side = side,
            Nodes = nodeDocs,
            Connections = connDocs,
            EditorLayout = layout
        };
    }

    public static CanvasModel FromGraphDocument(GraphDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var canvas = new CanvasModel();

        var positions = doc.EditorLayout.NodePositions;
        canvas.Viewport.PanX = (float)doc.EditorLayout.ViewportX;
        canvas.Viewport.PanY = (float)doc.EditorLayout.ViewportY;
        canvas.Viewport.Zoom = (float)doc.EditorLayout.Zoom;

        foreach (var nodeDoc in doc.Nodes)
        {
            var pos = CanvasPoint.Zero;
            if (positions.TryGetValue(nodeDoc.Id.ToString(), out var nodePos))
            {
                pos = new CanvasPoint((float)nodePos.X, (float)nodePos.Y);
            }

            var visualNode = new VisualNode(
                nodeDoc.Id,
                nodeDoc.Name,
                nodeDoc.NodeType,
                pos);

            foreach (var pinDoc in nodeDoc.Pins)
            {
                visualNode.Pins.Add(new VisualPin(
                    pinDoc.Id,
                    nodeDoc.Id,
                    pinDoc.Name,
                    pinDoc.Direction,
                    pinDoc.Kind,
                    pinDoc.DataType ?? "System.Object"));
            }

            foreach (var (k, v) in nodeDoc.Properties)
            {
                visualNode.Properties[k] = v;
            }

            canvas.AddNode(visualNode);
        }

        foreach (var connDoc in doc.Connections)
        {
            canvas.AddConnection(new VisualConnection(connDoc.FromPin, connDoc.ToPin));
        }

        return canvas;
    }
}
