using AstraGraph.Editor.Bridge;
using AstraGraph.Editor.InGame;
using AstraGraph.Editor.Protocol;
using AstraGraph.Robust.Shared;
using AstraGraph.UI.Runtime;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace AstraGraph.Robust.Client;

/// <summary>
/// Client-side RobustToolbox EntitySystem managing UI graph reconciliation,
/// native control trees, and the Astra Studio Web local loopback bridge.
/// </summary>
public sealed class ClientAstraGraphSystem : SharedAstraGraphSystem
{
    private RobustUiControlFactory _controlFactory = default!;
    private RobustUiReconciler _reconciler = default!;
    private AstraLocalBridge? _localBridge;
    private AstraInGameLauncher? _launcher;
    private AstraRuntimeStatusReporter? _statusReporter;

    public RobustUiControlFactory ControlFactory => _controlFactory;
    public RobustUiReconciler Reconciler => _reconciler;
    public AstraLocalBridge? LocalBridge => _localBridge;
    public AstraInGameLauncher? Launcher => _launcher;
    public AstraRuntimeStatusReporter? StatusReporter => _statusReporter;
    public string? StudioUnavailableReason { get; private set; }

    public override void Initialize()
    {
        base.Initialize();

        // 1. Initialize native Robust UI factory and reconciler
        _controlFactory = new RobustUiControlFactory();
        _reconciler = new RobustUiReconciler(_controlFactory);

        // 2. Register in IoC
        try
        {
            IoCManager.RegisterInstance<RobustUiControlFactory>(_controlFactory, overwrite: true);
            IoCManager.RegisterInstance<RobustUiReconciler>(_reconciler, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error($"AstraGraph client registration failed: {ex}");
            throw;
        }

        Log.Info("ClientAstraGraphSystem initialized with native RobustUiControlFactory.");
    }

    /// <summary>
    /// Launches the Astra Studio Web IDE in the default browser via the local loopback bridge.
    /// </summary>
    public async Task<string> LaunchStudioAsync(IAuthoringMessageHandler? handler = null, StudioLaunchContext? context = null)
    {
        if (handler == null && _localBridge == null)
        {
            StudioUnavailableReason = "Authoring session handler is required.";
            throw new InvalidOperationException(StudioUnavailableReason);
        }

        EnsureBridge(handler);
        if (!_localBridge!.IsRunning)
        {
            await _localBridge.StartAsync();
        }

        return _localBridge.LaunchStudioInBrowser(context);
    }

    public void EnsureBridge(IAuthoringMessageHandler? handler = null)
    {
        if (_localBridge != null)
        {
            return;
        }

        if (handler == null)
        {
            StudioUnavailableReason = "Authoring session handler is required.";
            return;
        }

        StudioUnavailableReason = null;
        _localBridge = AstraLocalBridge.CreateWithHandler(handler);
        _statusReporter = new AstraRuntimeStatusReporter(_localBridge);
        _launcher = new AstraInGameLauncher(_localBridge, _statusReporter);
    }

    public void OpenStudio()
    {
        EnsureBridge();
        Launcher?.OpenStudio();
    }

    public void InspectEntity(EntityUid uid)
    {
        EnsureBridge();
        Launcher?.InspectEntity(uid.ToString());
    }

    public void OpenRuntimeError(string graphId, string nodeId, string diagnosticCode, long executionTick)
    {
        EnsureBridge();
        Launcher?.OpenRuntimeError(graphId, nodeId, diagnosticCode, executionTick);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _localBridge?.Dispose();
        _localBridge = null;
        _launcher = null;
        _statusReporter = null;
    }
}
