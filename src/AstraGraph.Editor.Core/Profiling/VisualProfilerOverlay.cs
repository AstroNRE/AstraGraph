using System;
using System.Collections.Generic;
using AstraGraph.Core;
using AstraGraph.Runtime.Profiling;

namespace AstraGraph.Editor.Core.Profiling;

public enum HeatLevel
{
    Cold,
    Warm,
    Hot,
    Critical
}

public sealed record NodeHeatInfo(
    NodeId NodeId,
    long HitCount,
    double NormalizedHeat,
    HeatLevel Level,
    string FormattedText);

/// <summary>
/// Calculates node execution heat maps, hot paths, and performance badges
/// from GraphProfiler runtime statistics.
/// </summary>
public sealed class VisualProfilerOverlay
{
    private readonly GraphProfiler _profiler;
    private readonly GraphId _graphId;
    private readonly CanvasModel _canvasModel;
    private readonly Dictionary<NodeId, NodeHeatInfo> _nodeHeat = [];
    private readonly HashSet<VisualConnection> _hotConnections = [];

    public bool IsActive { get; set; } = true;
    public GraphId GraphId => _graphId;
    public IReadOnlyDictionary<NodeId, NodeHeatInfo> NodeHeat => _nodeHeat;
    public IReadOnlySet<VisualConnection> HotConnections => _hotConnections;

    public VisualProfilerOverlay(GraphProfiler profiler, GraphId graphId, CanvasModel canvasModel)
    {
        _profiler = profiler ?? throw new ArgumentNullException(nameof(profiler));
        _graphId = graphId;
        _canvasModel = canvasModel ?? throw new ArgumentNullException(nameof(canvasModel));
        Refresh();
    }

    public void Refresh()
    {
        _nodeHeat.Clear();
        _hotConnections.Clear();

        var hottest = _profiler.GetHottestNodes(100);
        var hitMap = new Dictionary<NodeId, long>();
        long maxHits = 0;

        foreach (var kvp in hottest)
        {
            hitMap[kvp.Key] = kvp.Value;
            if (kvp.Value > maxHits)
            {
                maxHits = kvp.Value;
            }
        }

        foreach (var node in _canvasModel.Nodes.Values)
        {
            hitMap.TryGetValue(node.Id, out var hits);
            var normalized = maxHits > 0 ? (double)hits / maxHits : 0.0;
            var level = GetHeatLevel(normalized, hits);
            var formatted = FormatHits(hits);

            _nodeHeat[node.Id] = new NodeHeatInfo(node.Id, hits, normalized, level, formatted);
        }

        // Identify hot connections: source and target are both Warm, Hot, or Critical
        foreach (var conn in _canvasModel.Connections)
        {
            var sourceNode = _canvasModel.GetNodeForPin(conn.SourcePinId);
            var targetNode = _canvasModel.GetNodeForPin(conn.TargetPinId);

            if (sourceNode != null && targetNode != null)
            {
                if (_nodeHeat.TryGetValue(sourceNode.Id, out var srcHeat) &&
                    _nodeHeat.TryGetValue(targetNode.Id, out var tgtHeat))
                {
                    if (srcHeat.Level >= HeatLevel.Warm && tgtHeat.Level >= HeatLevel.Warm)
                    {
                        _hotConnections.Add(conn);
                    }
                }
            }
        }
    }

    public NodeHeatInfo? GetNodeHeat(NodeId nodeId)
    {
        return _nodeHeat.TryGetValue(nodeId, out var info) ? info : null;
    }

    public bool IsHotConnection(VisualConnection conn) => _hotConnections.Contains(conn);

    public GraphPerformanceMetric GetMetrics() => _profiler.GetMetrics(_graphId);

    private static HeatLevel GetHeatLevel(double normalized, long hits)
    {
        if (hits == 0)
        {
            return HeatLevel.Cold;
        }

        if (normalized >= 0.85)
        {
            return HeatLevel.Critical;
        }

        if (normalized >= 0.50)
        {
            return HeatLevel.Hot;
        }

        if (normalized >= 0.15)
        {
            return HeatLevel.Warm;
        }

        return HeatLevel.Cold;
    }

    private static string FormatHits(long hits)
    {
        if (hits >= 1_000_000)
        {
            return $"{hits / 1_000_000.0:F1}M hits";
        }

        if (hits >= 1_000)
        {
            return $"{hits / 1_000.0:F1}k hits";
        }

        return $"{hits} hits";
    }
}
