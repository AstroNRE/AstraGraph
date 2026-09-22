using AstraGraph.UI.Runtime;
using Robust.Shared.GameObjects;

namespace AstraGraph.Robust.Client;

public static class AstraBuiRobustAdapter
{
    public static AstraBuiState ToState(AstraBuiContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        return new AstraBuiState
        {
            Revision = contract.Revision,
            ContractHash = contract.ContractHash,
            ClientActions = contract.ClientActions.ToList(),
            ServerNotifications = contract.ServerNotifications.ToList(),
            Values = contract.State.ToDictionary(pair => pair.Key, pair => pair.Value?.ToString() ?? "")
        };
    }

    public static bool Apply(AstraBuiBridge bridge, BoundUserInterfaceState state)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        if (state is not AstraBuiState astra)
        {
            return false;
        }

        var values = astra.Values.ToDictionary(pair => pair.Key, pair => (object?)pair.Value);
        return bridge.ApplyAuthoritative(new AstraBuiContract(
            values,
            astra.ClientActions,
            astra.ServerNotifications,
            astra.Revision,
            astra.ContractHash));
    }

    public static BuiMessage ToBuiMessage(BoundUserInterfaceMessage message)
    {
        var astra = message as AstraBuiUiMessage ?? throw new ArgumentException("Expected an Astra BUI message.", nameof(message));
        return new BuiMessage(astra.Action, astra.Payload);
    }
}
