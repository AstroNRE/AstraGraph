using System.Reflection;
using AstraGraph.Core;

namespace AstraGraph.Runtime;

public sealed class GraphEventSubscription
{
    public GraphId GraphId { get; }
    public Type? ComponentType { get; }
    public Type EventType { get; }
    public string EntryPointName { get; }
    public Action<object?, object>? Handler { get; }

    public GraphEventSubscription(
        GraphId graphId,
        Type? componentType,
        Type eventType,
        string entryPointName,
        Action<object?, object>? handler = null)
    {
        GraphId = graphId;
        ComponentType = componentType;
        EventType = eventType;
        EntryPointName = entryPointName;
        Handler = handler;
    }
}

/// <summary>
/// Stable native event router that bridges RobustToolbox event bus to active graph revisions.
/// Subscriptions belong to the Router rather than transient assemblies, preventing memory leaks during hot reload.
/// </summary>
public sealed class GraphEventRouter
{
    private readonly Dictionary<(Type?, Type), List<GraphEventSubscription>> _subscriptions = [];
    private readonly Lock _lock = new();

    public void Subscribe(
        Type? componentType,
        Type eventType,
        GraphId graphId,
        string entryPointName,
        Action<object?, object>? handler = null)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        lock (_lock)
        {
            var key = (componentType, eventType);
            if (!_subscriptions.TryGetValue(key, out var list))
            {
                list = [];
                _subscriptions[key] = list;
            }

            list.Add(new GraphEventSubscription(graphId, componentType, eventType, entryPointName, handler));
        }
    }

    public void UnsubscribeGraph(GraphId graphId)
    {
        lock (_lock)
        {
            foreach (var list in _subscriptions.Values)
            {
                list.RemoveAll(s => s.GraphId == graphId);
            }
        }
    }

    public bool DispatchEvent(object? component, object eventObject)
    {
        ArgumentNullException.ThrowIfNull(eventObject);

        var eventType = eventObject.GetType();
        var compType = component?.GetType();

        List<GraphEventSubscription> matched = [];

        lock (_lock)
        {
            // Match with exact component type
            if (compType != null && _subscriptions.TryGetValue((compType, eventType), out var compMatches))
            {
                matched.AddRange(compMatches);
            }

            // Match wildcard (broadcast / no component)
            if (_subscriptions.TryGetValue((null, eventType), out var wildcardMatches))
            {
                matched.AddRange(wildcardMatches);
            }
        }

        foreach (var sub in matched)
        {
            sub.Handler?.Invoke(component, eventObject);
        }

        // Check if event has a Handled property and whether it was marked handled
        var handledProp = eventType.GetProperty("Handled", BindingFlags.Public | BindingFlags.Instance);
        if (handledProp != null && handledProp.PropertyType == typeof(bool))
        {
            return (bool)(handledProp.GetValue(eventObject) ?? false);
        }

        return false;
    }

    public int GetSubscriptionCount(Type eventType)
    {
        lock (_lock)
        {
            var count = 0;
            foreach (var ((_, evType), list) in _subscriptions)
            {
                if (evType == eventType) count += list.Count;
            }
            return count;
        }
    }
}
