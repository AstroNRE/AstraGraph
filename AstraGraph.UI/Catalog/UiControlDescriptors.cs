namespace AstraGraph.UI.Catalog;

/// <summary>
/// How Studio should edit a control property. The kind is derived from the CLR type.
/// </summary>
public enum UiPropertyEditorKind
{
    Text,
    MultilineText,
    Boolean,
    Integer,
    Float,
    Enum,
    Color,
    Vector2,
    Resource,
    Prototype,
    StyleClasses,
    Reference,
    Custom
}

public enum UiStateScope
{
    Local,
    Server,
    Derived
}

public enum UiStateAuthority
{
    Client,
    Server
}

/// <summary>
/// One authorable property of a Robust <c>Control</c> or a content control.
/// </summary>
public sealed record UiPropertyDescriptor(
    string Name,
    string TypeName,
    bool CanRead,
    bool CanWrite,
    UiPropertyEditorKind EditorKind,
    string? Category = null,
    string? Documentation = null,
    string? DefaultValue = null,
    IReadOnlyList<string>? EnumValues = null);

/// <summary>
/// A field carried by a control event payload.
/// </summary>
public sealed record UiEventPayloadField(string Name, string TypeName);

/// <summary>
/// A real control event, indexed from the CLR type rather than a Studio list.
/// </summary>
public sealed record UiEventDescriptor(
    string Name,
    string EventType,
    IReadOnlyList<UiEventPayloadField> Payload,
    string? Documentation = null);

/// <summary>
/// Layout property that applies to a child because of its parent container.
/// </summary>
public sealed record UiAttachedPropertyDescriptor(
    string Name,
    string TypeName,
    string ParentTypeId,
    UiPropertyEditorKind EditorKind,
    string? Category = null);

/// <summary>
/// Stable description of one control type the designer can place.
/// </summary>
public sealed record UiControlDescriptor(
    string TypeId,
    string FullTypeName,
    string DisplayName,
    string Category,
    bool CanHaveChildren,
    IReadOnlyList<UiPropertyDescriptor> Properties,
    IReadOnlyList<UiEventDescriptor> Events,
    string? Documentation = null,
    IReadOnlyList<string>? AllowedChildTypes = null);

/// <summary>
/// Get, set, or event binding the catalog materializes for the logic graph.
/// </summary>
public sealed record UiGraphBinding(
    string ControlTypeId,
    string Kind,
    string Name,
    string TypeName,
    string? ElementId = null);
