using AstraGraph.Core;
using AstraGraph.UI.Model;

namespace AstraGraph.UI.Compiler;

/// <summary>
/// Abstract base instruction for the Astra UI intermediate representation.
/// </summary>
public abstract record UiIrInstruction;

public sealed record CreateWidgetInstruction(
    string ElementId,
    UiElementType ElementType,
    string? Name,
    int? MinWidth,
    int? MinHeight,
    UiOrientation Orientation) : UiIrInstruction;

public sealed record SetPropertyInstruction(
    string ElementId,
    string PropertyName,
    object? Value) : UiIrInstruction;

public sealed record AttachChildInstruction(
    string ParentId,
    string ChildId) : UiIrInstruction;

public sealed record RegisterBindingInstruction(
    string BindingId,
    string ElementId,
    string TargetProperty,
    string StateVariable,
    BindingDirection Direction,
    string? Converter) : UiIrInstruction;

public sealed record RegisterEventInstruction(
    string SubscriptionId,
    string ElementId,
    string EventName,
    string TargetAction,
    string? PayloadExpression) : UiIrInstruction;

/// <summary>
/// Compiled executable UI IR program.
/// </summary>
public sealed class UiIrProgram
{
    public required GraphId DocumentId { get; init; }
    public required string Name { get; init; }
    public required string RootElementId { get; init; }
    public required IReadOnlyList<UiIrInstruction> Instructions { get; init; }
    public required IReadOnlyDictionary<string, object?> InitialState { get; init; }
}
