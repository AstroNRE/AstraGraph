using System.Collections.Concurrent;
using System.Reflection;
using AstraGraph.Binding;
using AstraGraph.Core;

namespace AstraGraph.Runtime;

/// <summary>
/// Fast, zero-allocation dynamic event subscription adapter bridging RobustToolbox EventBus to active graph programs.
/// Utilizes compiled expression invokers from FastInvokerCompiler rather than slow runtime reflection.
/// </summary>
public sealed class AstraEventSubscriptionAdapter
{
    private readonly GraphEventRouter _router;

    public AstraEventSubscriptionAdapter(GraphEventRouter router)
    {
        _router = router;
    }

    /// <summary>
    /// Registers a broadcast graph event handler (not tied to a specific component).
    /// </summary>
    public void RegisterSubscription<TEvent>(
        GraphId graphId,
        string entryPointName,
        Action<TEvent> handler)
        where TEvent : class
    {
        _router.Subscribe(
            null,
            typeof(TEvent),
            graphId,
            entryPointName,
            (_, ev) => handler((TEvent)ev));
    }

    /// <summary>
    /// Registers a directed graph event handler for a specific component type.
    /// </summary>
    public void RegisterSubscription<TComponent, TEvent>(
        GraphId graphId,
        string entryPointName,
        Action<TComponent?, TEvent> handler)
        where TComponent : class
        where TEvent : class
    {
        var compType = typeof(TComponent) == typeof(object) ? null : typeof(TComponent);
        _router.Subscribe(
            compType,
            typeof(TEvent),
            graphId,
            entryPointName,
            (comp, ev) => handler(comp as TComponent, (TEvent)ev));
    }

    /// <summary>
    /// Dispatches an event through the router with high-throughput invocation and error isolation.
    /// </summary>
    public bool Dispatch(object eventPayload, object? component = null)
    {
        ArgumentNullException.ThrowIfNull(eventPayload);
        return _router.DispatchEvent(component, eventPayload);
    }
}
