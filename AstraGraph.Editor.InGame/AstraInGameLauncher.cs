using AstraGraph.Core;
using AstraGraph.Editor.Bridge;

namespace AstraGraph.Editor.InGame;

/// <summary>
/// In-game runtime status of the Astra Local Bridge, shown in developer toolbar / admin menu.
/// </summary>
public sealed record AstraRuntimeStatus(
    bool BridgeActive,
    int BridgePort,
    int ActiveStudioConnections,
    string? LastError,
    DateTimeOffset? BridgeStartedAt);

/// <summary>
/// Provides real-time status information about the running Astra bridge and connected Studio sessions.
/// </summary>
public sealed class AstraRuntimeStatusReporter
{
    private readonly AstraLocalBridge _bridge;
    private string? _lastError;

    public AstraRuntimeStatusReporter(AstraLocalBridge bridge)
    {
        _bridge = bridge;
    }

    public void RecordError(string error)
    {
        _lastError = error;
    }

    public void ClearError()
    {
        _lastError = null;
    }

    public AstraRuntimeStatus GetStatus()
    {
        var bridgeStatus = _bridge.GetStatus();
        return new AstraRuntimeStatus(
            BridgeActive: bridgeStatus.IsRunning,
            BridgePort: bridgeStatus.Port,
            ActiveStudioConnections: bridgeStatus.ActiveWebSocketConnections,
            LastError: _lastError,
            BridgeStartedAt: bridgeStatus.StartedAt);
    }
}

/// <summary>
/// Provides contextual in-game launch points for Astra Studio:
/// entity inspection, graph navigation, debug sessions and runtime error deep-links.
/// </summary>
public sealed class AstraInGameLauncher
{
    private readonly AstraLocalBridge _bridge;
    private readonly AstraRuntimeStatusReporter _statusReporter;

    public AstraInGameLauncher(AstraLocalBridge bridge, AstraRuntimeStatusReporter statusReporter)
    {
        _bridge = bridge;
        _statusReporter = statusReporter;
    }

    /// <summary>
    /// Opens Astra Studio at the root (no specific context).
    /// </summary>
    public void OpenStudio()
    {
        EnsureBridgeRunning();
        var ctx = new StudioDeepLinkContext { Action = StudioAction.OpenStudio };
        LaunchWithContext(ctx);
    }

    /// <summary>
    /// Opens Studio focused on a specific entity inspector.
    /// </summary>
    public void InspectEntity(string entityUid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityUid);
        EnsureBridgeRunning();
        var ctx = new StudioDeepLinkContext
        {
            Action = StudioAction.InspectEntity,
            EntityUid = entityUid
        };
        LaunchWithContext(ctx);
    }

    /// <summary>
    /// Opens Studio focused on a specific graph, optionally at a specific revision.
    /// </summary>
    public void OpenGraph(string graphId, string? revision = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        EnsureBridgeRunning();
        var ctx = new StudioDeepLinkContext
        {
            Action = revision != null ? StudioAction.OpenRevision : StudioAction.OpenGraph,
            GraphId = graphId,
            Revision = revision
        };
        LaunchWithContext(ctx);
    }

    /// <summary>
    /// Opens Studio focused on a specific node within a graph.
    /// </summary>
    public void OpenNode(string graphId, string nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        EnsureBridgeRunning();
        var ctx = new StudioDeepLinkContext
        {
            Action = StudioAction.OpenNode,
            GraphId = graphId,
            NodeId = nodeId
        };
        LaunchWithContext(ctx);
    }

    /// <summary>
    /// Opens Studio in debug mode for a specific entity and graph.
    /// </summary>
    public void OpenDebugSession(string entityUid, string graphId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityUid);
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        EnsureBridgeRunning();
        var ctx = new StudioDeepLinkContext
        {
            Action = StudioAction.OpenDebugSession,
            EntityUid = entityUid,
            GraphId = graphId
        };
        LaunchWithContext(ctx);
    }

    /// <summary>
    /// Opens Studio focused on the runtime error location (graph + node + diagnostic code).
    /// </summary>
    public void OpenRuntimeError(string graphId, string nodeId, string diagnosticCode, long executionTick)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticCode);
        EnsureBridgeRunning();
        var ctx = new StudioDeepLinkContext
        {
            Action = StudioAction.OpenRuntimeError,
            GraphId = graphId,
            NodeId = nodeId,
            DiagnosticCode = diagnosticCode,
            ExecutionTick = executionTick
        };
        LaunchWithContext(ctx);
    }

    /// <summary>
    /// Builds the deep-link URL for a given context without opening the browser.
    /// Useful for copying, logging or displaying in UI.
    /// </summary>
    public string BuildUrl(StudioDeepLinkContext ctx)
    {
        EnsureBridgeRunning();
        string baseUrl = _bridge.CreateLaunchUrl();
        return StudioDeepLinkUrlBuilder.Build(baseUrl, ctx);
    }

    private void LaunchWithContext(StudioDeepLinkContext ctx)
    {
        try
        {
            string url = BuildUrl(ctx);
            BrowserLauncher.OpenUrl(url);
            _statusReporter.ClearError();
        }
        catch (Exception ex)
        {
            _statusReporter.RecordError(ex.Message);
            throw;
        }
    }

    private void EnsureBridgeRunning()
    {
        if (!_bridge.IsRunning)
            throw new InvalidOperationException("Astra Local Bridge is not running. Call StartAsync() first.");
    }
}
