using AstraGraph.Core;

namespace AstraGraph.Runtime;

/// <summary>
/// One isolated graph failure. Repeated faults open a circuit and stop that graph until reset.
/// </summary>
public sealed record GraphFault(
    GraphId GraphId,
    RevisionId RevisionId,
    NodeId? NodeId,
    string ExceptionType,
    string Message,
    int Tick,
    int Count);

public sealed class GraphFaultLog
{
    private readonly Dictionary<GraphId, int> _counts = [];

    public int Threshold { get; set; } = 8;

    public GraphFault? Latest { get; private set; }

    public bool IsOpen(GraphId graphId) => _counts.TryGetValue(graphId, out var count) && count >= Threshold;

    public GraphFault Record(GraphId graphId, RevisionId revisionId, Exception exception, int tick, NodeId? nodeId = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var count = _counts.GetValueOrDefault(graphId) + 1;
        _counts[graphId] = count;
        Latest = new GraphFault(graphId, revisionId, nodeId, exception.GetType().Name, exception.Message, tick, count);
        return Latest;
    }
}
