using AstraGraph.Core;
using AstraGraph.HotReload;
using AstraGraph.Persistence;
using AstraGraph.Persistence.Discovery;
using AstraGraph.Persistence.State;
using AstraGraph.Robust.Shared;
using AstraGraph.Runtime;
using AstraGraph.Runtime.Debugging;
using AstraGraph.Runtime.Integration;
using AstraGraph.Runtime.Profiling;
using Robust.Shared.IoC;

namespace AstraGraph.Robust.Server;

/// <summary>
/// Server-side RobustToolbox EntitySystem managing persistent storage, hot reload transactions,
/// restart bootstrap recovery, and admin permissions for AstraGraph on live servers.
/// </summary>
public sealed class ServerAstraGraphSystem : SharedAstraGraphSystem
{
    private StorageLayout _storageLayout = default!;
    private AstraGraphFacade _facade = default!;
    private HotReloadManager _hotReloadManager = default!;
    private RobustAdminPermissionProvider _permissionProvider = default!;
    private PersistentStateStore _persistentStateStore = default!;
    private BootstrapLoader _bootstrapLoader = default!;
    private AstraAuthoringService _authoringService = default!;
    private bool _subscriptionsWired;

    public StorageLayout StorageLayout => _storageLayout;
    public AstraGraphFacade Facade => _facade;
    public HotReloadManager HotReloadManager => _hotReloadManager;
    public RobustAdminPermissionProvider PermissionProvider => _permissionProvider;
    public PersistentStateStore PersistentStateStore => _persistentStateStore;
    public BootstrapLoader BootstrapLoader => _bootstrapLoader;
    public IAstraAuthoringService AuthoringService => _authoringService;

    public ServerAstraGraphSystem()
    {
    }

    public ServerAstraGraphSystem(StorageLayout storageLayout)
    {
        _storageLayout = storageLayout;
    }

    public override void Initialize()
    {
        base.Initialize();
        EnsureInitialized();

        // Register in IoC
        try
        {
            IoCManager.RegisterInstance<HotReloadManager>(_hotReloadManager, overwrite: true);
            IoCManager.RegisterInstance<StorageLayout>(_storageLayout, overwrite: true);
            IoCManager.RegisterInstance<PersistentStateStore>(_persistentStateStore, overwrite: true);
            IoCManager.RegisterInstance<BootstrapLoader>(_bootstrapLoader, overwrite: true);
            IoCManager.RegisterInstance<IAstraAuthoringService>(_authoringService, overwrite: true);
        }
        catch
        {
        }

        // Execute Server Restart Bootstrap Pipeline
        ExecuteBootstrap();

        Log.Info("ServerAstraGraphSystem initialized with full restart bootstrap.");
    }

    public void EnsureInitialized()
    {
        _storageLayout ??= new StorageLayout(
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "AstraGraph"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "AstraGraph"));

        _storageLayout.EnsureDirectories();

        _permissionProvider ??= new RobustAdminPermissionProvider();
        _facade ??= AstraGraphFacade.ForServer(Host, _storageLayout, _permissionProvider);
        _hotReloadManager ??= _facade.Reloader;
        _persistentStateStore ??= _facade.PersistentState ?? new PersistentStateStore(_storageLayout);
        _bootstrapLoader ??= _facade.Discovery ?? new BootstrapLoader(_storageLayout);
        if (!_subscriptionsWired && EventAdapter != null)
        {
            _hotReloadManager.SubscriptionsCommitted += OnSubscriptionsCommitted;
            _subscriptionsWired = true;
        }

        _authoringService ??= new AstraAuthoringService(
            _permissionProvider,
            _hotReloadManager,
            auditLogger: null,
            debugger: new GraphDebugger(),
            profiler: new GraphProfiler(),
            bindingCatalog: null,
            storageLayout: _storageLayout,
            bootstrapLoader: _bootstrapLoader);
    }

    /// <summary>
    /// Executes the full server restart bootstrap pipeline:
    /// Crash recovery -> state restore -> graph discovery -> compilation/activation -> subscription & scheduler registration.
    /// </summary>
    public void ExecuteBootstrap()
    {
        EnsureInitialized();
        EnsureEngineCompatible();
        _facade.Initialize();
    }

    private void OnSubscriptionsCommitted(IReadOnlyList<AstraGraph.Core.Events.GraphEventSubscription> subscriptions)
    {
        if (EventAdapter == null)
        {
            return;
        }

        foreach (var subscription in subscriptions)
        {
            EventAdapter.RegisterSubscription(this, subscription);
        }
    }

    private static void EnsureEngineCompatible()
    {
        var manifestPath = EngineCompatibilityService.FindManifest();
        if (manifestPath == null)
        {
            throw new EngineCompatibilityException("Compatibility.json was not found. AstraGraph will not start against an unknown engine.");
        }

        var compatibility = EngineCompatibilityService.Load(manifestPath);
        var robustDir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifestPath)!, "..", "RobustToolbox"));
        var checkedOut = EngineCompatibilityService.ReadCheckedOutCommit(robustDir);
        compatibility.EnsureCompatible(new EngineCompatibilityReport(
            checkedOut ?? compatibility.Manifest.TestedRobustCommit,
            compatibility.Manifest.EngineApiVersion,
            ["type-event-subscribe"]));

        if (compatibility.Manifest.DynamicNativeSystemOrdering)
        {
            throw new EngineCompatibilityException(EngineCompatibilityManifest.NativeOrderingWarning);
        }
    }

    /// <summary>
    /// Saves the current persistent state snapshot to disk for crash-safe survival across server restarts.
    /// </summary>
    public void SavePersistentState(string snapshotName = "default")
    {
        _persistentStateStore.SaveState(snapshotName, Host.State);
    }

    public override void Update(float frameTime)
    {
        // Process pending hot-reload transactions at tick boundary before advancing simulation
        _hotReloadManager.ProcessPendingTransactions();
        base.Update(frameTime);
    }
}
