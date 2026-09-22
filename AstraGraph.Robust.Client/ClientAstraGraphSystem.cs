using AstraGraph.Editor.Bridge;
using AstraGraph.Robust.Shared;
using AstraGraph.UI.Runtime;
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

    public RobustUiControlFactory ControlFactory => _controlFactory;
    public RobustUiReconciler Reconciler => _reconciler;
    public AstraLocalBridge? LocalBridge => _localBridge;

    public override void Initialize()
    {
        base.Initialize();

        // 1. Initialize native Robust UI factory and reconciler
        _controlFactory = new RobustUiControlFactory();
        _reconciler = new RobustUiReconciler(_controlFactory);

        // 2. Register in IoC
        IoCManager.RegisterInstance<RobustUiControlFactory>(_controlFactory, overwrite: true);
        IoCManager.RegisterInstance<RobustUiReconciler>(_reconciler, overwrite: true);

        Log.Info("ClientAstraGraphSystem initialized with native RobustUiControlFactory.");
    }

    /// <summary>
    /// Launches the Astra Studio Web IDE in the default browser via the local loopback bridge.
    /// </summary>
    public async Task<string> LaunchStudioAsync()
    {
        if (_localBridge == null)
        {
            _localBridge = AstraLocalBridge.CreateDefault();
            await _localBridge.StartAsync();
        }

        return _localBridge.LaunchStudioInBrowser();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _localBridge?.Dispose();
        _localBridge = null;
    }
}
