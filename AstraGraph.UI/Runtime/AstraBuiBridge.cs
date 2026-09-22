using AstraGraph.UI.Compiler;

namespace AstraGraph.UI.Runtime;

public sealed record BuiMessage(string Action, object? Payload);

/// <summary>
/// Bridges client-side Astra UI state and interactions with server-side BoundUserInterface (BUI) logic.
/// </summary>
public sealed class AstraBuiBridge
{
    private readonly UiStateManager _stateManager;
    private readonly Action<BuiMessage> _sendToServerCallback;

    public AstraBuiBridge(UiStateManager stateManager, Action<BuiMessage> sendToServerCallback)
    {
        _stateManager = stateManager;
        _sendToServerCallback = sendToServerCallback;
    }

    public void ConnectEventSubscription(RegisterEventInstruction ev, IRobustUiControl control)
    {
        control.OnEventTriggered += (eventName, payload) =>
        {
            if (string.Equals(eventName, ev.EventName, StringComparison.OrdinalIgnoreCase))
            {
                _sendToServerCallback(new BuiMessage(ev.TargetAction, payload));
            }
        };
    }

    public void ReceiveStateFromServer(IReadOnlyDictionary<string, object?> serverState)
    {
        foreach (var kv in serverState)
        {
            _stateManager.SetVariable(kv.Key, kv.Value);
        }
    }

    public void SendAction(string action, object? payload = null)
    {
        _sendToServerCallback(new BuiMessage(action, payload));
    }
}
