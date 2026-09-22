using AstraGraph.Core;

namespace AstraGraph.Runtime;

public enum AstraGraphPhase
{
    PreSimulation,
    MainSimulation,
    PostSimulation
}

public sealed record SystemRegistration(
    GraphId GraphId,
    string SystemName,
    IReadOnlyList<string> Before,
    IReadOnlyList<string> After,
    int Priority = 0,
    Action<double, int>? UpdateCallback = null,
    AstraGraphPhase Phase = AstraGraphPhase.MainSimulation);

/// <summary>
/// Orders Astra graphs against other Astra graphs by Before, After, and Priority.
/// Placement next to native engine systems belongs to <see cref="IEngineScheduleHook"/>.
/// </summary>
public sealed class GraphScheduler
{
    private readonly Dictionary<string, SystemRegistration> _systems = new(StringComparer.OrdinalIgnoreCase);
    private List<SystemRegistration> _orderedExecutionList = [];
    private bool _dirty = true;
    private readonly Lock _lock = new();

    public IReadOnlyCollection<SystemRegistration> Systems
    {
        get
        {
            lock (_lock)
            {
                return _systems.Values.ToList();
            }
        }
    }

    public void RegisterSystem(SystemRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        lock (_lock)
        {
            _systems[registration.SystemName] = registration;
            _dirty = true;
        }
    }

    public bool UnregisterSystem(string systemName)
    {
        lock (_lock)
        {
            if (_systems.Remove(systemName))
            {
                _dirty = true;
                return true;
            }
            return false;
        }
    }

    public void UnregisterGraph(GraphId graphId)
    {
        lock (_lock)
        {
            var toRemove = _systems.Values.Where(s => s.GraphId == graphId).Select(s => s.SystemName).ToList();
            foreach (var name in toRemove)
            {
                _systems.Remove(name);
            }
            if (toRemove.Count > 0)
            {
                _dirty = true;
            }
        }
    }

    public IReadOnlyList<SystemRegistration> GetOrderedSystems()
    {
        lock (_lock)
        {
            if (_dirty)
            {
                RebuildExecutionOrder();
                _dirty = false;
            }
            return _orderedExecutionList;
        }
    }

    private readonly GraphFaultLog? _faults;

    public GraphScheduler(GraphFaultLog? faults = null)
    {
        _faults = faults;
    }

    public Exception? LastUpdateError { get; private set; }

    public void Update(double currentTimeSeconds, int currentTick)
    {
        var systems = GetOrderedSystems();
        foreach (var sys in systems)
        {
            if (_faults?.IsOpen(sys.GraphId) == true)
            {
                continue;
            }

            try
            {
                sys.UpdateCallback?.Invoke(currentTimeSeconds, currentTick);
            }
            catch (Exception ex)
            {
                LastUpdateError = ex;
                _faults?.Record(sys.GraphId, RevisionId.Empty, ex, currentTick);
            }
        }
    }

    public void UpdatePhase(AstraGraphPhase phase, double currentTimeSeconds, int currentTick)
    {
        var systems = GetOrderedSystems();
        foreach (var sys in systems)
        {
            if (sys.Phase != phase || _faults?.IsOpen(sys.GraphId) == true)
            {
                continue;
            }

            try
            {
                sys.UpdateCallback?.Invoke(currentTimeSeconds, currentTick);
            }
            catch (Exception ex)
            {
                LastUpdateError = ex;
                _faults?.Record(sys.GraphId, RevisionId.Empty, ex, currentTick);
            }
        }
    }

    private void RebuildExecutionOrder()
    {
        var nodes = _systems.Values.ToList();
        var adj = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in nodes)
        {
            adj[node.SystemName] = [];
            inDegree[node.SystemName] = 0;
        }

        // Build edges based on Before and After
        foreach (var node in nodes)
        {
            // If node.After contains X, edge is X -> node
            foreach (var afterName in node.After)
            {
                if (adj.TryGetValue(afterName, out var afterList))
                {
                    afterList.Add(node.SystemName);
                    inDegree[node.SystemName]++;
                }
            }

            // If node.Before contains Y, edge is node -> Y
            foreach (var beforeName in node.Before)
            {
                if (adj.TryGetValue(node.SystemName, out var beforeList) && adj.ContainsKey(beforeName))
                {
                    beforeList.Add(beforeName);
                    inDegree[beforeName]++;
                }
            }
        }

        // Priority queue / Kahn's algorithm
        var queue = new PriorityQueue<SystemRegistration, int>();
        foreach (var node in nodes)
        {
            if (inDegree[node.SystemName] == 0)
            {
                // Invert priority so higher priority values run first
                queue.Enqueue(node, -node.Priority);
            }
        }

        var result = new List<SystemRegistration>(nodes.Count);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            result.Add(current);

            foreach (var neighbor in adj[current.SystemName])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                {
                    var neighborNode = _systems[neighbor];
                    queue.Enqueue(neighborNode, -neighborNode.Priority);
                }
            }
        }

        if (result.Count < nodes.Count)
        {
            throw new InvalidOperationException("Cyclic dependency detected in GraphScheduler system ordering.");
        }

        _orderedExecutionList = result;
    }
}
