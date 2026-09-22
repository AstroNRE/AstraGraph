using AstraGraph.Editor.Protocol;

namespace AstraGraph.Studio.DevHost;

/// <summary>
/// Attached-mode bridge to a runtime that already exists.
/// DevHost does not construct a second host when this is supplied.
/// </summary>
public interface IAstraDevHostBackend
{
    IAuthoringMessageHandler AuthoringHandler { get; }

    DevHostBackendInfo Info { get; }
}

public sealed record DevHostBackendInfo(
    string TargetName,
    string Engine,
    bool RuntimeConnected,
    string? CompatibilityProfile);
