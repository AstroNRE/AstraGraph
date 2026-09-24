using AstraGraph.Core;
using AstraGraph.UI.Catalog;
using AstraGraph.UI.Model;

namespace AstraGraph.UI.Logic;

/// <summary>
/// The three demonstration documents from the UI builder plan: Mothroach, vehicle parts, and surgery.
/// </summary>
public static class UiSamples
{
    public static UiDocument Mothroach()
    {
        var label = Node("countLabel", UiElementType.Label, "Spawn count");
        var count = Node("count", UiElementType.LineEdit, "");
        var button = Node("convert", UiElementType.Button, "Convert Target");
        return Document("Mothroach Converter", "BUI", [label, count, button],
            [
                State("count", "int", UiStateScope.Local, 3),
                State("canConvert", "bool", UiStateScope.Server, true)
            ],
            [
                Bind("count", "count", BindingDirection.TwoWay)
            ],
            [Event("convert", "OnPressed", "Convert")],
            [
                Step("OnEvent", "convert", "OnPressed"),
                Step("SendAction", "convert", action: "Convert", state: "count"),
                Step("SetState", action: "Convert", state: "count")
            ],
            Contract("mothroach",
                [State("count", "int", UiStateScope.Server, 3)],
                [Action("Convert", new UiEventPayloadField("count", "int"))],
                []));
    }

    public static UiDocument VehicleMaintenance()
    {
        var grid = Node("parts", UiElementType.GridContainer, null);
        grid.Properties["Columns"] = "2";
        grid.StyleClasses.Add("parts");
        foreach (var name in new[] { "Wheel", "Engine", "Door", "Panel" })
        {
            var part = Node(name.ToLowerInvariant(), UiElementType.Button, name);
            part.StyleClasses.Add("parts");
            grid.AddChild(part);
        }

        return Document("Vehicle Maintenance", "BUI", [grid],
            [State("selected", "string", UiStateScope.Local, "Wheel")],
            [Bind("wheel", "selected", BindingDirection.OneWay)],
            [Event("wheel", "OnPressed", "SelectPart")],
            [
                Step("OnEvent", "wheel", "OnPressed"),
                Step("SendAction", "wheel", action: "SelectPart", state: "selected"),
                Step("SetState", action: "SelectPart", state: "selected")
            ],
            Contract("vehicle",
                [State("selected", "string", UiStateScope.Server, "Wheel")],
                [Action("SelectPart", new UiEventPayloadField("part", "string"))],
                []));
    }

    public static UiDocument Surgery()
    {
        var name = Node("patient", UiElementType.Label, "Patient");
        var progress = Node("progress", UiElementType.ProgressBar, null);
        var tool = Node("tool", UiElementType.ItemList, null);
        var start = Node("start", UiElementType.Button, "Start");
        return Document("Surgery", "BUI", [name, progress, tool, start],
            [
                State("patientName", "string", UiStateScope.Server, "Patient"),
                State("progress", "float", UiStateScope.Server, 0),
                State("reason", "string", UiStateScope.Local, "")
            ],
            [
                Bind("patient", "patientName", BindingDirection.OneWay),
                Bind("progress", "progress", BindingDirection.OneWay)
            ],
            [Event("start", "OnPressed", "StartOperation")],
            [
                Step("OnEvent", "start", "OnPressed"),
                Step("SendAction", "start", action: "StartOperation", state: "patientName"),
                Step("DoAfter", action: "StartOperation", property: "1"),
                Step("SetState", action: "StartOperation", state: "progress"),
                Step("Notify", action: "StartOperation", state: "OperationFailed", property: "reason", value: "interrupted")
            ],
            Contract("surgery",
                [State("progress", "float", UiStateScope.Server, 0)],
                [Action("StartOperation", new UiEventPayloadField("progress", "float"))],
                [Note("OperationFailed", new UiEventPayloadField("reason", "string"))]));
    }

    private static UiDocument Document(
        string name,
        string kind,
        IEnumerable<UiElementNode> children,
        IEnumerable<UiStateVariable> state,
        IEnumerable<UiBindingDefinition> bindings,
        IEnumerable<UiEventSubscription> events,
        IEnumerable<UiLogicStep> logic,
        UiBuiContractDocument contract) => new()
    {
        Id = GraphId.New(),
        Name = name,
        DocumentKind = kind,
        Root = new UiElementNode { Id = "root", ElementType = UiElementType.BoxContainer, Children = children.ToList() },
        StateVariables = state.ToList(),
        Bindings = bindings.ToList(),
        Events = events.ToList(),
        Logic = logic.ToList(),
        Contract = contract
    };

    private static UiElementNode Node(string id, UiElementType type, string? text) => new()
    {
        Id = id,
        ElementType = type,
        Name = id,
        Text = text
    };

    private static UiStateVariable State(string name, string typeName, UiStateScope scope, object? value) => new()
    {
        Id = name,
        Name = name,
        TypeName = typeName,
        Scope = scope,
        DefaultValue = value
    };

    private static UiBindingDefinition Bind(string elementId, string state, BindingDirection direction) => new()
    {
        BindingId = elementId + ":" + state,
        ElementId = elementId,
        TargetProperty = elementId == "progress" ? "Value" : "Text",
        StateVariable = state,
        Direction = direction
    };

    private static UiEventSubscription Event(string elementId, string eventName, string action) => new()
    {
        SubscriptionId = elementId + ":" + eventName,
        ElementId = elementId,
        EventName = eventName,
        TargetAction = action
    };

    private static UiLogicStep Step(string kind, string? elementId = null, string? eventName = null, string? action = null, string? state = null, string? property = null, string? value = null) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Kind = kind,
        ElementId = elementId,
        EventName = eventName,
        ActionName = action,
        StateVariable = state,
        PropertyName = property,
        Arguments = value == null ? [] : new Dictionary<string, string> { ["Value"] = value }
    };

    private static UiBuiAction Action(string name, params UiEventPayloadField[] parameters) => new()
    {
        Id = name,
        Name = name,
        Parameters = parameters.ToList()
    };

    private static UiBuiNotification Note(string name, params UiEventPayloadField[] parameters) => new()
    {
        Id = name,
        Name = name,
        Parameters = parameters.ToList()
    };

    private static UiBuiContractDocument Contract(string id, IEnumerable<UiStateVariable> state, IEnumerable<UiBuiAction> actions, IEnumerable<UiBuiNotification> notifications) => new()
    {
        Id = id,
        State = state.ToList(),
        Actions = actions.ToList(),
        Notifications = notifications.ToList()
    };
}
