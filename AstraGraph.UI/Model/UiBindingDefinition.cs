namespace AstraGraph.UI.Model;

/// <summary>
/// Defines a reactive data binding between a UI control property and a state variable.
/// </summary>
public sealed class UiBindingDefinition
{
    public required string BindingId { get; init; }
    public required string ElementId { get; init; }
    public required string TargetProperty { get; init; }
    public required string StateVariable { get; init; }
    public BindingDirection Direction { get; init; } = BindingDirection.OneWay;
    public string? Converter { get; init; }
}

/// <summary>
/// Subscribes a UI event (e.g. OnPressed, OnTextChanged) to an action or graph logic.
/// </summary>
public sealed class UiEventSubscription
{
    public required string SubscriptionId { get; init; }
    public required string ElementId { get; init; }
    public required string EventName { get; init; }
    public required string TargetAction { get; init; }
    public string? PayloadExpression { get; init; }
}
