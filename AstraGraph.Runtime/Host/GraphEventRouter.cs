using System.Reflection;
using AstraGraph.Core;
using AstraGraph.Core.Events;
using AstraGraph.Runtime.Events;

namespace AstraGraph.Runtime;

public delegate void RefEventDispatcher<TEvent>(ref TEvent ev);
public delegate void RefComponentEventDispatcher<TComp, TEvent>(object? uid, TComp? comp, ref TEvent ev);

/// <summary>
/// Execution binding representing an active event subscription inside the router.
/// </summary>
public sealed class EventSubscriptionBinding
{
    public GraphEventSubscription Descriptor { get; }
    public Action<object?, object>? UntypedHandler { get; }
    public object? RefHandler { get; }

    public EventSubscriptionBinding(
        GraphEventSubscription descriptor,
        Action<object?, object>? untypedHandler = null,
        object? refHandler = null)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        UntypedHandler = untypedHandler;
        RefHandler = refHandler;
    }
}

/// <summary>
/// Stable native event router that bridges RobustToolbox event bus to active graph revisions.
/// Subscriptions belong to the Router rather than transient assemblies, preventing memory leaks during hot reload.
/// Supports both untyped objects and zero-copy unboxed ref struct/class event dispatch.
/// </summary>
public sealed class GraphEventRouter
{
    private readonly Dictionary<(Type?, Type), List<EventSubscriptionBinding>> _subscriptions = [];
    private readonly Lock _lock = new();

    public void Subscribe(
        Type? componentType,
        Type eventType,
        GraphId graphId,
        string entryPointName,
        Action<object?, object>? handler = null)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        var descriptor = new GraphEventSubscription(
            graphId,
            entryPointName,
            componentType,
            eventType,
            componentType != null ? AstraEventSource.DirectedComponent : AstraEventSource.Broadcast);

        Subscribe(descriptor, handler);
    }

    public void Subscribe(GraphEventSubscription descriptor, Action<object?, object>? handler = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        lock (_lock)
        {
            var key = (descriptor.ComponentType, descriptor.EventType);
            if (!_subscriptions.TryGetValue(key, out var list))
            {
                list = [];
                _subscriptions[key] = list;
            }

            list.Add(new EventSubscriptionBinding(descriptor, untypedHandler: handler));
        }
    }

    public void SubscribeRef<TEvent>(
        GraphId graphId,
        string entryPointName,
        RefEventDispatcher<TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var descriptor = new GraphEventSubscription(
            graphId,
            entryPointName,
            null,
            typeof(TEvent),
            AstraEventSource.Broadcast,
            ByRef: true);

        lock (_lock)
        {
            var key = ((Type?)null, typeof(TEvent));
            if (!_subscriptions.TryGetValue(key, out var list))
            {
                list = [];
                _subscriptions[key] = list;
            }

            list.Add(new EventSubscriptionBinding(descriptor, refHandler: handler));
        }
    }

    public void SubscribeComponentRef<TComp, TEvent>(
        GraphId graphId,
        string entryPointName,
        RefComponentEventDispatcher<TComp, TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var descriptor = new GraphEventSubscription(
            graphId,
            entryPointName,
            typeof(TComp),
            typeof(TEvent),
            AstraEventSource.DirectedComponent,
            ByRef: true);

        lock (_lock)
        {
            var key = (typeof(TComp), typeof(TEvent));
            if (!_subscriptions.TryGetValue(key, out var list))
            {
                list = [];
                _subscriptions[key] = list;
            }

            list.Add(new EventSubscriptionBinding(descriptor, refHandler: handler));
        }
    }

    public void UnsubscribeGraph(GraphId graphId)
    {
        lock (_lock)
        {
            foreach (var list in _subscriptions.Values)
            {
                list.RemoveAll(s => s.Descriptor.GraphId == graphId);
            }
        }
    }

    public void DispatchRefEvent<TEvent>(ref TEvent ev)
    {
        List<EventSubscriptionBinding> matched = [];

        lock (_lock)
        {
            if (_subscriptions.TryGetValue((null, typeof(TEvent)), out var list))
            {
                matched.AddRange(list);
            }
        }

        foreach (var sub in matched)
        {
            if (sub.RefHandler is RefEventDispatcher<TEvent> typedHandler)
            {
                typedHandler(ref ev);
            }
            else if (sub.UntypedHandler != null)
            {
                sub.UntypedHandler(null, ev!);
            }
        }
    }

    public void DispatchComponentRefEvent<TComp, TEvent>(object? uid, TComp? comp, ref TEvent ev)
    {
        List<EventSubscriptionBinding> matched = [];

        lock (_lock)
        {
            if (comp != null && _subscriptions.TryGetValue((typeof(TComp), typeof(TEvent)), out var compList))
            {
                matched.AddRange(compList);
            }

            if (_subscriptions.TryGetValue((null, typeof(TEvent)), out var wildcardList))
            {
                matched.AddRange(wildcardList);
            }
        }

        foreach (var sub in matched)
        {
            if (sub.RefHandler is RefComponentEventDispatcher<TComp, TEvent> compHandler)
            {
                compHandler(uid, comp, ref ev);
            }
            else if (sub.RefHandler is RefEventDispatcher<TEvent> broadcastHandler)
            {
                broadcastHandler(ref ev);
            }
            else if (sub.UntypedHandler != null)
            {
                sub.UntypedHandler(comp, ev!);
            }
        }
    }

    public bool DispatchEvent(object? component, object eventObject)
    {
        ArgumentNullException.ThrowIfNull(eventObject);

        var eventType = eventObject.GetType();
        var compType = component?.GetType();

        List<EventSubscriptionBinding> matched = [];

        lock (_lock)
        {
            if (compType != null && _subscriptions.TryGetValue((compType, eventType), out var compMatches))
            {
                matched.AddRange(compMatches);
            }

            if (_subscriptions.TryGetValue((null, eventType), out var wildcardMatches))
            {
                matched.AddRange(wildcardMatches);
            }
        }

        foreach (var sub in matched)
        {
            sub.UntypedHandler?.Invoke(component, eventObject);
        }

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
