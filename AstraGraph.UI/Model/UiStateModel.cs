using AstraGraph.UI.Catalog;

namespace AstraGraph.UI.Model;

/// <summary>
/// Typed UI state variable. Rename changes <see cref="Name"/> and leaves <see cref="Id"/> stable.
/// </summary>
public sealed class UiStateVariable
{
    public required string Id { get; init; }

    public required string Name { get; set; }

    public string TypeName { get; set; } = "string";

    public object? DefaultValue { get; set; }

    public UiStateScope Scope { get; set; } = UiStateScope.Local;

    public UiStateAuthority Authority { get; set; } = UiStateAuthority.Client;
}

/// <summary>
/// Typed BUI action. <see cref="Id"/> stays stable when <see cref="Name"/> changes.
/// </summary>
public sealed class UiBuiAction
{
    public required string Id { get; init; }

    public required string Name { get; set; }

    public List<UiEventPayloadField> Parameters { get; set; } = [];
}

/// <summary>
/// Server-to-client notification described by the BUI contract.
/// </summary>
public sealed class UiBuiNotification
{
    public required string Id { get; init; }

    public required string Name { get; set; }

    public List<UiEventPayloadField> Parameters { get; set; } = [];
}

/// <summary>
/// Typed BUI contract stored with the UI document. String action names are a projection of <see cref="UiBuiAction.Name"/>.
/// </summary>
public sealed class UiBuiContractDocument
{
    public required string Id { get; init; }

    public List<UiStateVariable> State { get; set; } = [];

    public List<UiBuiAction> Actions { get; set; } = [];

    public List<UiBuiNotification> Notifications { get; set; } = [];
}

/// <summary>
/// One step in a UI or BUI logic chain. The catalog supplies the event and property names.
/// </summary>
public sealed class UiLogicStep
{
    public required string Id { get; init; }

    public required string Kind { get; set; }

    public string? ElementId { get; set; }

    public string? EventName { get; set; }

    public string? StateVariable { get; set; }

    public string? ActionName { get; set; }

    public string? PropertyName { get; set; }

    public Dictionary<string, string> Arguments { get; set; } = [];
}
