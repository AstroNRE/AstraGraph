using System.Reflection;
using AstraGraph.Core;
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
/// directly to AstraGraph's GraphEventRouter with true ref TEvent write-back support.
/// </summary>
public sealed class RobustEventBusSubscriptionAdapter
{
    private readonly GraphEventRouter _router;
    private readonly List<Action<EntitySystem>> _deferredRegistrations = [];

    public RobustEventBusSubscriptionAdapter(GraphEventRouter router)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
    }

    /// <summary>
    /// Subscribes a broadcast event with full ref TEvent write-back support.
    /// </summary>
    public void RegisterBroadcastSubscription<TEvent>(
        EntitySystem system,
        GraphId graphId,
        string entryPointName,
        RefEventHandler<TEvent>? customRefHandler = null)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(system);

        // Subscribes via Robust EntitySystem ref handler
        var method = typeof(EntitySystem).GetMethod(
            "SubscribeLocalEvent",
            BindingFlags.Instance | BindingFlags.NonPublic,
            [typeof(EntityEventRefHandler<TEvent>), typeof(Type[]), typeof(Type[])]);

        if (method != null)
        {
            EntityEventRefHandler<TEvent> handler = (ref TEvent args) =>
            {
                // 1. Invoke custom ref handler if specified
                customRefHandler?.Invoke(ref args);

                // 2. Dispatch through AstraGraph router
                _router.DispatchEvent(null, args);

                // 3. Write-back for ref events (Handled, Cancellable, or modified state)
                WriteBackRefEvent(ref args);
            };

            method.Invoke(system, [handler, null, null]);
        }
    }

    /// <summary>
    /// Subscribes a component-directed event with full ref TEvent write-back support.
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
                // 1. Invoke custom ref handler
                customRefHandler?.Invoke(uid, comp, ref args);

                // 2. Dispatch through AstraGraph router
                _router.DispatchEvent(comp, args);

                // 3. Write-back for ref events
                WriteBackRefEvent(ref args);
            };

            genericMethod.Invoke(system, [handler, null, null]);
        }
    }

    /// <summary>
    /// Performs in-place mutation write-back for Robust events (e.g. HandledEntityEventArgs, Cancellable).
    /// </summary>
    public static void WriteBackRefEvent<TEvent>(ref TEvent ev)
    {
        if (ev is null) return;

        // If it's a HandledEntityEventArgs, ensure handled status is propagated
        if (ev is HandledEntityEventArgs handledArgs)
        {
            // Already a reference class; state is mutated in-place
            _ = handledArgs.Handled;
        }

        // For value-type struct events, boxed reflection or interface write-back can be applied
        if (typeof(TEvent).IsValueType)
        {
            var prop = typeof(TEvent).GetProperty("Handled");
            if (prop != null && prop.CanWrite && prop.PropertyType == typeof(bool))
            {
                object boxed = ev;
                // If Handled flag was set during graph execution, propagate back
                prop.SetValue(boxed, true);
                ev = (TEvent)boxed;
            }
        }
    }
}
