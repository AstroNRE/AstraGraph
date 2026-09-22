using System.Reflection;
using AstraGraph.Core;
using AstraGraph.Core.Events;
using AstraGraph.Runtime;
using Robust.Shared.GameObjects;

namespace AstraGraph.Robust.Shared;

/// <summary>
/// Delegate for handlers receiving event arguments by reference, allowing in-place mutations.
/// </summary>
public delegate void RefEventHandler<TEvent>(ref TEvent args);

/// <summary>
/// Delegate for component-directed handlers receiving event arguments by reference.
/// </summary>
public delegate void RefComponentEventHandler<TComp, TEvent>(EntityUid uid, TComp comp, ref TEvent args)
    where TComp : IComponent;

/// <summary>
/// High-performance adapter bridging RobustToolbox's IEventBus and SubscribeLocalEvent
/// directly to AstraGraph's GraphEventRouter with true, unboxed ref TEvent dispatch.
/// </summary>
public sealed class RobustEventBusSubscriptionAdapter
{
    private readonly GraphEventRouter _router;

    public RobustEventBusSubscriptionAdapter(GraphEventRouter router)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
    }

    /// <summary>
    /// Registers a subscription dynamically based on a GraphEventSubscription descriptor.
    /// </summary>
    public void RegisterSubscription(EntitySystem system, GraphEventSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(subscription);

        if (subscription.ComponentType != null)
        {
            var registerMethod = typeof(RobustEventBusSubscriptionAdapter)
                .GetMethod(nameof(RegisterComponentSubscription), BindingFlags.Instance | BindingFlags.Public)
                ?.MakeGenericMethod(subscription.ComponentType, subscription.EventType);

            registerMethod?.Invoke(this, [system, subscription.GraphId, subscription.EntryPointId, null]);
        }
        else
        {
            var registerMethod = typeof(RobustEventBusSubscriptionAdapter)
                .GetMethod(nameof(RegisterBroadcastSubscription), BindingFlags.Instance | BindingFlags.Public)
                ?.MakeGenericMethod(subscription.EventType);

            registerMethod?.Invoke(this, [system, subscription.GraphId, subscription.EntryPointId, null]);
        }
    }

    /// <summary>
    /// Subscribes a broadcast event with full ref TEvent pass-through support without boxing.
    /// </summary>
    public void RegisterBroadcastSubscription<TEvent>(
        EntitySystem system,
        GraphId graphId,
        string entryPointName,
        RefEventHandler<TEvent>? customRefHandler = null)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(system);

        if (Attribute.IsDefined(typeof(TEvent), typeof(ByRefEventAttribute), inherit: false))
        {
            SubscribeBroadcastByRef(system, customRefHandler);
            return;
        }

        var method = FindBroadcastSubscribe(byRef: false)
            ?? throw new InvalidOperationException("EntitySystem.SubscribeLocalEvent value handler was not found.");

        EntityEventHandler<TEvent> handler = args =>
        {
            if (customRefHandler != null)
            {
                customRefHandler(ref args);
            }

            _router.DispatchRefEvent(ref args);
        };

        method.MakeGenericMethod(typeof(TEvent)).Invoke(system, [handler, null, null]);
    }

    private void SubscribeBroadcastByRef<TEvent>(EntitySystem system, RefEventHandler<TEvent>? customRefHandler)
        where TEvent : notnull
    {
        var method = FindBroadcastSubscribe(byRef: true)
            ?? throw new InvalidOperationException("EntitySystem.SubscribeLocalEvent ref handler was not found.");

        EntityEventRefHandler<TEvent> handler = (ref TEvent args) =>
        {
            customRefHandler?.Invoke(ref args);
            _router.DispatchRefEvent(ref args);
        };

        method.MakeGenericMethod(typeof(TEvent)).Invoke(system, [handler, null, null]);
    }

    private static MethodInfo? FindBroadcastSubscribe(bool byRef)
    {
        return typeof(EntitySystem).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .FirstOrDefault(candidate =>
            {
                if (candidate.Name != "SubscribeLocalEvent"
                    || !candidate.IsGenericMethodDefinition
                    || candidate.GetGenericArguments().Length != 1
                    || candidate.GetParameters().Length != 3)
                {
                    return false;
                }

                var parameterName = candidate.GetParameters()[0].ParameterType.Name;
                return byRef
                    ? parameterName.StartsWith("EntityEventRefHandler", StringComparison.Ordinal)
                    : parameterName.StartsWith("EntityEventHandler", StringComparison.Ordinal);
            });
    }

    /// <summary>
    /// Subscribes a component-directed event with full ref TEvent pass-through support without boxing.
    /// </summary>
    public void RegisterComponentSubscription<TComp, TEvent>(
        EntitySystem system,
        GraphId graphId,
        string entryPointName,
        RefComponentEventHandler<TComp, TEvent>? customRefHandler = null)
        where TComp : IComponent
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(system);

        var method = typeof(EntitySystem).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .FirstOrDefault(m => m.Name == "SubscribeLocalEvent" &&
                                 m.IsGenericMethod &&
                                 m.GetGenericArguments().Length == 2 &&
                                 m.GetParameters().Length == 3 &&
                                 m.GetParameters()[0].ParameterType.Name.StartsWith("ComponentEventRefHandler", StringComparison.Ordinal));

        if (method != null)
        {
            var genericMethod = method.MakeGenericMethod(typeof(TComp), typeof(TEvent));
            ComponentEventRefHandler<TComp, TEvent> handler = (EntityUid uid, TComp comp, ref TEvent args) =>
            {
                customRefHandler?.Invoke(uid, comp, ref args);
                _router.DispatchComponentRefEvent(uid, comp, ref args);
            };

            genericMethod.Invoke(system, [handler, null, null]);
        }
    }
}
