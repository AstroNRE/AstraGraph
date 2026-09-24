using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace AstraGraph.Robust.Client;

[Serializable, NetSerializable]
public sealed class AstraBuiState : BoundUserInterfaceState
{
    public int Revision { get; set; }

    public string ContractHash { get; set; } = "";

    public Dictionary<string, string> Values { get; set; } = [];

    public Dictionary<string, string> TypedValues { get; set; } = [];

    public List<string> ClientActions { get; set; } = [];

    public List<string> ServerNotifications { get; set; } = [];
}

[Serializable, NetSerializable]
public sealed class AstraBuiUiMessage : BoundUserInterfaceMessage
{
    public string Action { get; set; } = "";

    public string? Payload { get; set; }
}
