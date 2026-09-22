namespace AstraGraph.Core;

/// <summary>
/// Invocation-local native event frame. Each dispatch pushes its own context.
/// There is no process-wide current event.
/// </summary>
public sealed class AstraEventInvocationContext
{
    public AstraEventInvocationContext(AstraValue entity, object? component, object? eventObject)
    {
        Entity = entity;
        Component = component;
        Event = eventObject;
    }

    public AstraValue Entity { get; }

    public object? Component { get; }

    public object? Event { get; }

    public AstraValue Read(int slot) => slot switch
    {
        0 => Entity,
        1 => AstraValueBox.Box(Component),
        _ => AstraValueBox.Box(Event)
    };
}
