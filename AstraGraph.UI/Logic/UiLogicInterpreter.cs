using AstraGraph.Core;
using AstraGraph.UI.Model;
using AstraGraph.UI.Runtime;

namespace AstraGraph.UI.Logic;

public sealed record UiLogicTrace(string Kind, string Detail);

/// <summary>
/// Runs the UI and BUI graphs against the open session. Client nodes can only send actions.
/// Server nodes are the authority for BUI state.
/// </summary>
public sealed class UiLogicInterpreter
{
    private readonly List<UiLogicTrace> _trace = [];

    public UiLogicInterpreter(UiLogicBundle bundle)
    {
        Bundle = bundle ?? throw new ArgumentNullException(nameof(bundle));
    }

    public UiLogicBundle Bundle { get; }

    public IReadOnlyList<UiLogicTrace> Trace => _trace;

    public bool DispatchClient(string elementId, string eventName, UiStateManager state, Action<string, object?> send, out string? action, IReadOnlyDictionary<string, IRobustUiControl>? controls = null)
    {
        action = null;
        var entry = Bundle.Client.Nodes.FirstOrDefault(node =>
            node.NodeType.Equals("UI.OnEvent", StringComparison.OrdinalIgnoreCase) &&
            node.Properties.GetValueOrDefault("ElementId") == elementId &&
            (node.Properties.GetValueOrDefault("EventName") ?? "").Equals(eventName, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
        {
            return false;
        }

        var chain = Walk(Bundle.Client, entry).ToList();
        if (chain.Any(IsServerMutation))
        {
            _trace.Add(new UiLogicTrace("security", "Client cannot mutate server state."));
            return false;
        }

        _trace.Add(new UiLogicTrace("event", $"{elementId}.{eventName}"));
        var values = new Dictionary<PinId, object?>();
        foreach (var node in chain)
        {
            if (node.NodeType.Equals("UI.GetProperty", StringComparison.OrdinalIgnoreCase))
            {
                var value = ReadControl(controls, node.Properties.GetValueOrDefault("ElementId"), node.Properties.GetValueOrDefault("Property") ?? "Text");
                var output = node.FindPin("Value", PinDirection.Output);
                if (output != null)
                {
                    values[output.Id] = value;
                }

                _trace.Add(new UiLogicTrace("get", $"{node.Properties.GetValueOrDefault("ElementId")}.{node.Properties.GetValueOrDefault("Property")}={value}"));
                continue;
            }

            if (node.NodeType.Equals("UI.SetProperty", StringComparison.OrdinalIgnoreCase))
            {
                var written = node.Properties.GetValueOrDefault("Value");
                WriteControl(controls, node.Properties.GetValueOrDefault("ElementId"), node.Properties.GetValueOrDefault("Property") ?? "Text", written);
                _trace.Add(new UiLogicTrace("set", $"{node.Properties.GetValueOrDefault("Property")}={written}"));
                continue;
            }

            if (node.NodeType.Equals("UI.Focus", StringComparison.OrdinalIgnoreCase))
            {
                WriteControl(controls, node.Properties.GetValueOrDefault("ElementId"), "Focused", true);
                _trace.Add(new UiLogicTrace("focus", node.Properties.GetValueOrDefault("ElementId") ?? ""));
                continue;
            }

            if (!node.NodeType.Equals("Native.Call", StringComparison.OrdinalIgnoreCase) ||
                node.Properties.GetValueOrDefault("Method") != "Ui.SendAction")
            {
                continue;
            }

            action = node.Properties.GetValueOrDefault("Action");
            var variable = node.Properties.GetValueOrDefault("StateVariable");
            object? payload = string.IsNullOrEmpty(variable) ? null : state.GetVariable(variable!);
            if (payload == null)
            {
                payload = ConnectedValue(Bundle.Client, node, "text", values) ?? ConnectedValue(Bundle.Client, node, "Value", values);
            }

            send(action ?? "", payload);
            _trace.Add(new UiLogicTrace("send", $"{action}({payload})"));
        }

        return action != null;
    }

    public bool DispatchNotification(string name, IReadOnlyDictionary<string, object?> payload, UiStateManager state, out string? error)
    {
        var entry = Bundle.Client.Nodes.FirstOrDefault(node =>
            node.NodeType.Equals("UI.OnNotification", StringComparison.OrdinalIgnoreCase) &&
            node.Properties.GetValueOrDefault("Notification") == name);
        if (entry == null)
        {
            error = $"Client graph has no handler for notification '{name}'.";
            return false;
        }

        _trace.Add(new UiLogicTrace("notification", name));
        foreach (var field in payload)
        {
            state.SetVariable(field.Key, field.Value);
            _trace.Add(new UiLogicTrace("state", $"{field.Key}={field.Value}"));
        }

        error = null;
        return true;
    }

    public bool DispatchServer(BuiAuthoritativeSession session, string actionName, IReadOnlyDictionary<string, object?> payload, int seenRevision, out string? error)
    {
        if (!session.TryHandleAction(actionName, payload, seenRevision, out error))
        {
            return false;
        }

        var entry = Bundle.Server.Nodes.FirstOrDefault(node =>
            node.NodeType.Equals("UI.OnAction", StringComparison.OrdinalIgnoreCase) &&
            node.Properties.GetValueOrDefault("Action") == actionName);
        if (entry == null)
        {
            error = $"Server graph has no handler for '{actionName}'.";
            return false;
        }

        var contextPayload = new Dictionary<string, object?>(payload, StringComparer.Ordinal);
        contextPayload.TryAdd("User", session.Context.User);
        contextPayload.TryAdd("Entity", session.Context.Entity);
        contextPayload.TryAdd("BuiKey", session.Context.BuiKey);
        contextPayload.TryAdd("BoundEntity", session.Context.BoundEntity);
        payload = contextPayload;

        _trace.Add(new UiLogicTrace("action", actionName));
        _trace.Add(new UiLogicTrace("context", $"{session.Context.User}/{session.Context.Entity}/{session.Context.BuiKey}"));
        foreach (var node in Walk(Bundle.Server, entry))
        {
            if (node.NodeType.Equals("UI.Notify", StringComparison.OrdinalIgnoreCase))
            {
                var notice = node.Properties.GetValueOrDefault("Notification") ?? "";
                var parameter = node.Properties.GetValueOrDefault("Parameter") ?? "reason";
                var noticeValue = node.Properties.GetValueOrDefault("Value");
                if (string.IsNullOrEmpty(noticeValue) && payload.TryGetValue(parameter, out var supplied))
                {
                    noticeValue = supplied?.ToString();
                }

                if (!session.TryNotify(notice, new Dictionary<string, object?> { [parameter] = noticeValue }, out error))
                {
                    return false;
                }

                _trace.Add(new UiLogicTrace("notify", $"{notice}({noticeValue})"));
                continue;
            }

            if (!node.NodeType.Equals("Core.VariableAssign", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = node.Properties.GetValueOrDefault("VariableName") ?? "";
            var valuePin = node.FindPin("Value", PinDirection.Input);
            object? value = null;
            if (valuePin != null)
            {
                var connection = Bundle.Server.Connections.FirstOrDefault(item => item.ToPin == valuePin.Id);
                if (connection != default && connection.FromPin != default)
                {
                    var source = Bundle.Server.Nodes.SelectMany(item => item.Pins).FirstOrDefault(pin => pin.Id == connection.FromPin);
                    if (source != null && payload.TryGetValue(source.Name, out var supplied))
                    {
                        value = supplied;
                    }
                }
            }

            if (!session.SetState(name, value, out error))
            {
                return false;
            }

            _trace.Add(new UiLogicTrace("state", $"{name}={value}"));
        }

        error = null;
        return true;
    }

    private static bool IsServerMutation(NodeDocument node) =>
        node.NodeType.Equals("UI.SetBuiState", StringComparison.OrdinalIgnoreCase) ||
        (node.NodeType.Equals("Native.Call", StringComparison.OrdinalIgnoreCase) &&
         node.Properties.GetValueOrDefault("Method") == "Ui.SetBuiState") ||
        node.Properties.GetValueOrDefault("Scope") == "Server";

    private static object? ReadControl(IReadOnlyDictionary<string, IRobustUiControl>? controls, string? elementId, string propertyName)
    {
        if (controls == null || string.IsNullOrEmpty(elementId) || !controls.TryGetValue(elementId, out var control))
        {
            return null;
        }

        return control.GetProperty(propertyName);
    }

    private static void WriteControl(IReadOnlyDictionary<string, IRobustUiControl>? controls, string? elementId, string propertyName, object? value)
    {
        if (controls == null || string.IsNullOrEmpty(elementId) || !controls.TryGetValue(elementId, out var control))
        {
            return;
        }

        if (control is MockRobustUiControl mock && propertyName.Equals("Focused", StringComparison.OrdinalIgnoreCase))
        {
            mock.IsFocused = value is true;
        }

        control.SetProperty(propertyName, value);
    }

    private static object? ConnectedValue(GraphDocument graph, NodeDocument node, string pinName, IReadOnlyDictionary<PinId, object?> values)
    {
        var pin = node.FindPin(pinName, PinDirection.Input);
        if (pin == null)
        {
            return null;
        }

        var connection = graph.Connections.FirstOrDefault(item => item.ToPin == pin.Id);
        if (connection == null)
        {
            return values.GetValueOrDefault(pin.Id);
        }

        return values.GetValueOrDefault(connection.FromPin);
    }

    private static IEnumerable<NodeDocument> Walk(GraphDocument graph, NodeDocument entry)
    {
        var current = entry;
        var guard = 0;
        while (current != null && guard++ < 32)
        {
            var execOut = current.Pins.FirstOrDefault(pin => pin.Kind == PinKind.Execution && pin.Direction == PinDirection.Output);
            if (execOut == null)
            {
                yield break;
            }

            var connection = graph.Connections.FirstOrDefault(item => item.FromPin == execOut.Id);
            if (connection == null)
            {
                yield break;
            }

            current = graph.FindNode(connection.ToNode);
            if (current != null)
            {
                yield return current;
            }
        }
    }
}
