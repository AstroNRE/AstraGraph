namespace AstraGraph.Studio.DevHost;

public sealed record DevHostStatus(
    string Mode,
    bool RuntimeConnected,
    bool AuthoringAvailable,
    int ActiveConnections,
    string Version,
    string Engine,
    string? CompatibilityProfile);

public sealed record DevHostClientConfig(
    string Mode,
    bool AuthoringAvailable,
    string TargetLabel);
