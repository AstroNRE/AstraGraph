namespace AstraGraph.Core.Events;

/// <summary>
/// Source origin of an AstraGraph event subscription.
/// </summary>
public enum AstraEventSource
{
    /// <summary>
    /// Broadcast event raised across the entire simulation world.
    /// </summary>
    Broadcast,

    /// <summary>
    /// Directed event dispatched to an entity possessing a specific component.
    /// </summary>
    DirectedComponent,

    /// <summary>
    /// Custom internal or script-defined event.
    /// </summary>
    Custom
}

/// <summary>
/// Explicit metadata describing a compiled graph's event subscription.
/// Maps native engine events directly to graph entry points without arbitrary object boxing.
/// </summary>
public sealed record GraphEventSubscription(
    GraphId GraphId,
    string EntryPointId,
    Type? ComponentType,
    Type EventType,
    AstraEventSource EventSource = AstraEventSource.DirectedComponent,
    bool ByRef = true,
    IReadOnlyList<Type>? Before = null,
    IReadOnlyList<Type>? After = null
);
