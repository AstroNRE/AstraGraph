using AstraGraph.Core;
using AstraGraph.UI.Catalog;
using AstraGraph.UI.Model;

namespace AstraGraph.UI.Logic;

/// <summary>
/// Builds the client and server graphs that replace UI code-behind and BUI server code.
/// The UI document stays the source of truth; these graphs are the executable form.
/// </summary>
public static class UiLogicGraph
{
    public static UiLogicBundle Build(UiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new UiLogicBundle(BuildClient(document), BuildServer(document));
    }

    public static GraphDocument BuildClient(UiDocument document)
    {
        var graph = Graph(document.Name + " Client", GraphSide.Client);
        foreach (var step in document.Logic.Where(item => item.Kind.Equals("OnEvent", StringComparison.OrdinalIgnoreCase)))
        {
            var send = document.Logic.FirstOrDefault(item =>
                item.Kind.Equals("SendAction", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.ElementId, step.ElementId, StringComparison.Ordinal))
                ?? document.Events.Where(item => item.ElementId == step.ElementId)
                    .Select(item => new UiLogicStep
                    {
                        Id = item.SubscriptionId,
                        Kind = "SendAction",
                        ElementId = item.ElementId,
                        ActionName = item.TargetAction,
                        StateVariable = document.Bindings.FirstOrDefault(binding => binding.ElementId == item.ElementId)?.StateVariable
                    })
                    .FirstOrDefault();
            if (send == null)
            {
                continue;
            }

            var call = AddSendChain(graph, document, step.ElementId, step.EventName, send.ActionName, send.StateVariable ?? PayloadVariable(document, step.ElementId));
            AppendClientTail(graph, document, step.ElementId, call);
        }

        if (graph.Nodes.Count == 0)
        {
            foreach (var ev in document.Events)
            {
                var call = AddSendChain(graph, document, ev.ElementId, ev.EventName, ev.TargetAction, PayloadVariable(document, ev.ElementId));
                AppendClientTail(graph, document, ev.ElementId, call);
            }
        }

        foreach (var notification in document.Contract?.Notifications ?? [])
        {
            var pins = new List<PinDocument> { Pin("Out", PinDirection.Output, PinKind.Execution) };
            pins.AddRange(notification.Parameters.Select(parameter => Pin(parameter.Name, PinDirection.Output, PinKind.Data, AstraType(parameter.TypeName))));
            var entry = Node("UI.OnNotification", $"On {notification.Name}", new Dictionary<string, string>
            {
                ["Notification"] = notification.Name
            }, pins);
            graph.Nodes.Add(entry);
            Place(graph, entry, graph.Nodes.Count);
        }

        foreach (var variable in document.StateVariables)
        {
            graph.Variables.Add(Variable(variable.Name, AstraType(variable.TypeName)));
        }

        return graph;
    }

    public static GraphDocument BuildServer(UiDocument document)
    {
        var graph = Graph(document.Name + " Server", GraphSide.Server);
        var actions = document.Contract?.Actions ?? [];
        if (actions.Count == 0)
        {
            actions = document.Events
                .Select(item => item.TargetAction)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .Select(name => new UiBuiAction { Id = name, Name = name })
                .ToList();
        }

        foreach (var action in actions)
        {
            var assignStep = document.Logic.FirstOrDefault(item =>
                item.Kind.Equals("SetState", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.ActionName, action.Name, StringComparison.Ordinal));
            var stateName = assignStep?.StateVariable
                ?? document.StateVariables.FirstOrDefault(item => item.Scope == UiStateScope.Server)?.Name
                ?? "result";
            var parameter = action.Parameters.FirstOrDefault()?.Name ?? assignStep?.PropertyName ?? "value";
            var assign = AddServerChain(graph, action, parameter, stateName, AstraType(document.StateVariables.FirstOrDefault(item => item.Name == stateName)?.TypeName));
            AppendServerTail(graph, document, action.Name, assign);
        }

        foreach (var variable in document.StateVariables)
        {
            if (graph.Variables.All(item => !item.Name.Equals(variable.Name, StringComparison.Ordinal)))
            {
                graph.Variables.Add(Variable(variable.Name, AstraType(variable.TypeName)));
            }
        }

        return graph;
    }

    private static NodeDocument AddSendChain(GraphDocument graph, UiDocument document, string? elementId, string? eventName, string? action, string? stateVariable)
    {
        var entry = Node("UI.OnEvent", $"On {elementId}.{eventName}", new Dictionary<string, string>
        {
            ["ElementId"] = elementId ?? "",
            ["EventName"] = eventName ?? "OnPressed"
        }, ExecOut());
        var pins = ExecInOut().ToList();
        var contractAction = document.Contract?.Actions.FirstOrDefault(item => item.Name == action);
        if (contractAction != null)
        {
            pins.AddRange(contractAction.Parameters.Select(parameter => Pin(parameter.Name, PinDirection.Input, PinKind.Data, AstraType(parameter.TypeName))));
        }

        var middle = document.Logic.Where(item =>
            string.Equals(item.ElementId, elementId, StringComparison.Ordinal) &&
            item.Kind.Equals("GetProperty", StringComparison.OrdinalIgnoreCase)).ToList();
        var call = Node("Native.Call", $"Send {action}", new Dictionary<string, string>
        {
            ["Method"] = "Ui.SendAction",
            ["Action"] = action ?? "",
            ["StateVariable"] = stateVariable ?? ""
        }, pins);
        graph.Nodes.Add(entry);
        var previous = entry;
        foreach (var step in middle)
        {
            var read = Node("UI.GetProperty", $"Get {step.ElementId}.{step.PropertyName}", new Dictionary<string, string>
            {
                ["ElementId"] = step.PropertyName == null ? step.ElementId ?? "" : step.Arguments.GetValueOrDefault("Target") ?? step.ElementId ?? "",
                ["Property"] = step.PropertyName ?? "Text"
            },
            [
                Pin("In", PinDirection.Input, PinKind.Execution),
                Pin("Out", PinDirection.Output, PinKind.Execution),
                Pin("Value", PinDirection.Output, PinKind.Data, "string")
            ]);
            if (step.Arguments.TryGetValue("Target", out var target) && !string.IsNullOrWhiteSpace(target))
            {
                read.Properties["ElementId"] = target;
            }

            graph.Nodes.Add(read);
            Connect(graph, previous, "Out", read, "In");
            previous = read;
        }

        graph.Nodes.Add(call);
        Connect(graph, previous, "Out", call, "In");
        Place(graph, entry, graph.Nodes.Count);
        Place(graph, call, graph.Nodes.Count);
        return call;
    }

    private static void AppendClientTail(GraphDocument graph, UiDocument document, string? elementId, NodeDocument tail)
    {
        foreach (var step in document.Logic.Where(item => string.Equals(item.ElementId, elementId, StringComparison.Ordinal)))
        {
            NodeDocument? next = step.Kind.ToLowerInvariant() switch
            {
                "setproperty" => Node("UI.SetProperty", $"Set {step.PropertyName}", new Dictionary<string, string>
                {
                    ["ElementId"] = step.Arguments.GetValueOrDefault("Target") ?? elementId ?? "",
                    ["Property"] = step.PropertyName ?? "Text",
                    ["Value"] = step.Arguments.GetValueOrDefault("Value") ?? ""
                }, ExecInOut()),
                "focus" => Node("UI.Focus", $"Focus {elementId}", new Dictionary<string, string>
                {
                    ["ElementId"] = step.Arguments.GetValueOrDefault("Target") ?? elementId ?? ""
                }, ExecInOut()),
                _ => null
            };
            if (next == null)
            {
                continue;
            }

            graph.Nodes.Add(next);
            Connect(graph, tail, "Out", next, "In");
            Place(graph, next, graph.Nodes.Count);
            tail = next;
        }
    }

    private static NodeDocument AddServerChain(GraphDocument graph, UiBuiAction action, string parameter, string stateName, string typeName)
    {
        var pins = new List<PinDocument>
        {
            Pin("Out", PinDirection.Output, PinKind.Execution),
            Pin("User", PinDirection.Output, PinKind.Data, "string"),
            Pin("Entity", PinDirection.Output, PinKind.Data, "string"),
            Pin("BuiKey", PinDirection.Output, PinKind.Data, "string"),
            Pin("BoundEntity", PinDirection.Output, PinKind.Data, "string"),
            Pin(parameter, PinDirection.Output, PinKind.Data, typeName)
        };
        foreach (var extra in action.Parameters.Where(item => !item.Name.Equals(parameter, StringComparison.Ordinal)))
        {
            pins.Add(Pin(extra.Name, PinDirection.Output, PinKind.Data, AstraType(extra.TypeName)));
        }

        var entry = Node("UI.OnAction", $"On BUI {action.Name}", new Dictionary<string, string>
        {
            ["Action"] = action.Name
        }, pins);
        var assign = Node("Core.VariableAssign", $"Set {stateName}", new Dictionary<string, string>
        {
            ["VariableName"] = stateName
        },
        [
            Pin("In", PinDirection.Input, PinKind.Execution),
            Pin("Out", PinDirection.Output, PinKind.Execution),
            Pin("Value", PinDirection.Input, PinKind.Data, typeName)
        ]);
        if (graph.Variables.All(item => !item.Name.Equals(stateName, StringComparison.Ordinal)))
        {
            graph.Variables.Add(Variable(stateName, typeName));
        }

        graph.Nodes.Add(entry);
        graph.Nodes.Add(assign);
        Connect(graph, entry, "Out", assign, "In");
        Connect(graph, entry, parameter, assign, "Value");
        Place(graph, entry, graph.Nodes.Count);
        Place(graph, assign, graph.Nodes.Count);
        return assign;
    }

    private static void AppendServerTail(GraphDocument graph, UiDocument document, string action, NodeDocument tail)
    {
        var delay = document.Logic.FirstOrDefault(item =>
            item.Kind.Equals("DoAfter", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.ActionName, action, StringComparison.Ordinal));
        if (delay != null)
        {
            var seconds = string.IsNullOrWhiteSpace(delay.PropertyName) ? "1" : delay.PropertyName;
            var node = Node("Flow.DoAfter", $"DoAfter {action}", new Dictionary<string, string>(),
            [
                Pin("In", PinDirection.Input, PinKind.Execution),
                Pin("Out", PinDirection.Output, PinKind.Execution),
                Pin("Seconds", PinDirection.Input, PinKind.Data, "float32", seconds)
            ]);
            graph.Nodes.Add(node);
            Connect(graph, tail, "Out", node, "In");
            Place(graph, node, graph.Nodes.Count);
            tail = node;
        }

        var notify = document.Logic.FirstOrDefault(item =>
            item.Kind.Equals("Notify", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.ActionName, action, StringComparison.Ordinal));
        if (notify == null)
        {
            return;
        }

        var notice = Node("UI.Notify", $"Notify {notify.StateVariable}", new Dictionary<string, string>
        {
            ["Notification"] = notify.StateVariable ?? "",
            ["Parameter"] = notify.PropertyName ?? "reason",
            ["Value"] = notify.Arguments.GetValueOrDefault("Value") ?? ""
        }, ExecInOut());
        graph.Nodes.Add(notice);
        Connect(graph, tail, "Out", notice, "In");
        Place(graph, notice, graph.Nodes.Count);
    }

    private static GraphDocument Graph(string name, GraphSide side) => new()
    {
        Id = GraphId.New(),
        Name = name,
        Kind = GraphKind.UI,
        Side = side
    };

    private static GraphVariableDocument Variable(string name, string typeName) => new()
    {
        Id = SymbolId.New(),
        Name = name,
        TypeName = typeName
    };

    private static NodeDocument Node(string type, string name, Dictionary<string, string> properties, IReadOnlyList<PinDocument> pins) => new()
    {
        Id = NodeId.New(),
        Name = name,
        NodeType = type,
        Properties = properties,
        Pins = pins.ToList()
    };

    private static PinDocument[] ExecOut() => [Pin("Out", PinDirection.Output, PinKind.Execution)];

    private static PinDocument[] ExecInOut() =>
    [
        Pin("In", PinDirection.Input, PinKind.Execution),
        Pin("Out", PinDirection.Output, PinKind.Execution)
    ];

    private static PinDocument Pin(string name, PinDirection direction, PinKind kind, string dataType = "", string? defaultValue = null) => new()
    {
        Id = PinId.New(),
        Name = name,
        Direction = direction,
        Kind = kind,
        DataType = dataType,
        DefaultValue = defaultValue
    };

    private static void Connect(GraphDocument graph, NodeDocument from, string fromPin, NodeDocument to, string toPin)
    {
        var source = from.FindPin(fromPin, PinDirection.Output)!;
        var target = to.FindPin(toPin, PinDirection.Input)!;
        graph.Connections.Add(new ConnectionDocument
        {
            FromNode = from.Id,
            FromPin = source.Id,
            ToNode = to.Id,
            ToPin = target.Id
        });
    }

    private static void Place(GraphDocument graph, NodeDocument node, int index)
    {
        graph.EditorLayout.NodePositions[node.Id.ToString()] = new NodePosition(80, 40 + index * 120);
    }

    private static string? PayloadVariable(UiDocument document, string? elementId)
    {
        var onElement = document.Bindings.FirstOrDefault(binding => binding.ElementId == elementId && binding.Direction == BindingDirection.TwoWay)?.StateVariable;
        if (!string.IsNullOrEmpty(onElement))
        {
            return onElement;
        }

        return document.Bindings.FirstOrDefault(binding => binding.Direction == BindingDirection.TwoWay)?.StateVariable
            ?? document.StateVariables.FirstOrDefault(item => item.Scope == UiStateScope.Local)?.Name;
    }

    private static string AstraType(string? typeName) => typeName?.ToLowerInvariant() switch
    {
        "int" or "integer" => "int32",
        "float" or "single" => "float32",
        "bool" or "boolean" => "bool",
        null or "" => "string",
        _ => typeName
    };
}

public sealed record UiLogicBundle(GraphDocument Client, GraphDocument Server);
