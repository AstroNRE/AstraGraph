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
using AstraGraph.Runtime.Security;
using Robust.Shared.IoC;

namespace AstraGraph.Robust.Server;

/// <summary>
/// Server-side RobustToolbox EntitySystem managing persistent storage, hot reload transactions,
/// restart bootstrap recovery, and admin permissions for AstraGraph on live servers.
/// </summary>
public sealed class ServerAstraGraphSystem : SharedAstraGraphSystem
{
    private StorageLayout? _storageLayout;
    private AstraGraphFacade _facade = default!;
    private HotReloadManager _hotReloadManager = default!;
    private IAstraPermissionProvider _permissionProvider = default!;
    private PersistentStateStore _persistentStateStore = default!;
    private BootstrapLoader _bootstrapLoader = default!;
    private AstraAuthoringService _authoringService = default!;
    private AstraServerHostOptions? _pendingOptions;
    private AstraServerHostOptions _hostOptions = default!;
    private string? _resolvedManifestPath;
    private string? _robustIdentity;
    private bool _subscriptionsWired;
    private bool _composed;
    private bool _reported;

    public StorageLayout StorageLayout => _storageLayout ?? throw new InvalidOperationException("AstraGraph server host is not initialized.");
    public AstraGraphFacade Facade => _facade;
    public HotReloadManager HotReloadManager => _hotReloadManager;
    public IAstraPermissionProvider PermissionProvider => _permissionProvider;
    public PersistentStateStore PersistentStateStore => _persistentStateStore;
    public BootstrapLoader BootstrapLoader => _bootstrapLoader;
    public IAstraAuthoringService AuthoringService => _authoringService;
    public AstraServerHostOptions HostOptions => _hostOptions;

    public ServerAstraGraphSystem()
    {
    }

    /// <summary>
    /// Legacy entry point. Prefer <see cref="AstraServerHostOptions"/> before initialization.
    /// </summary>
    public ServerAstraGraphSystem(StorageLayout storageLayout)
    {
        _storageLayout = storageLayout;
    }

    public ServerAstraGraphSystem(AstraServerHostOptions options)
    {
        Configure(options);
    }

    public void Configure(AstraServerHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (_composed)
        {
            throw new InvalidOperationException("AstraGraph server host is already initialized.");
        }

        if (options.PermissionProvider == null && options.AdminDirectory == null)
        {
            throw new InvalidOperationException("AstraGraph server authoring requires IAstraAdminDirectory or IAstraPermissionProvider.");
        }

        _pendingOptions = options;
    }

    public override void Initialize()
    {
        base.Initialize();
        EnsureInitialized();

        try
        {
            IoCManager.RegisterInstance<HotReloadManager>(_hotReloadManager, overwrite: true);
            IoCManager.RegisterInstance<StorageLayout>(_storageLayout!, overwrite: true);
            IoCManager.RegisterInstance<PersistentStateStore>(_persistentStateStore, overwrite: true);
            IoCManager.RegisterInstance<BootstrapLoader>(_bootstrapLoader, overwrite: true);
            IoCManager.RegisterInstance<IAstraAuthoringService>(_authoringService, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error($"AstraGraph server registration failed: {ex}");
            throw;
        }

        ExecuteBootstrap();

        Log.Info("ServerAstraGraphSystem initialized with full restart bootstrap.");
    }

    public void EnsureInitialized()
    {
        if (_composed)
        {
            return;
        }

        var options = ResolveOptions();
        ValidateStorage(options.Storage);
        _resolvedManifestPath = EngineCompatibilityService.FindManifest(options.CompatibilityManifestPath);
        _permissionProvider = ResolvePermissions(options);
        var composed = options with
        {
            PermissionProvider = _permissionProvider,
            CompatibilityManifestPath = _resolvedManifestPath
        };
        _facade = AstraGraphFacade.ForServer(Host, composed);
        _storageLayout = composed.Storage;
        _hostOptions = composed;
        _hotReloadManager = _facade.Reloader;
        _persistentStateStore = _facade.PersistentState ?? new PersistentStateStore(_storageLayout);
        _bootstrapLoader = _facade.Discovery ?? new BootstrapLoader(_storageLayout);
        if (!_subscriptionsWired && EventAdapter != null)
        {
            _hotReloadManager.SubscriptionsCommitted += OnSubscriptionsCommitted;
            _subscriptionsWired = true;
        }

        AttachGameplay(_facade.Catalog);

        _authoringService = new AstraAuthoringService(
            _permissionProvider,
            _hotReloadManager,
            auditLogger: null,
            debugger: new GraphDebugger(),
            profiler: new GraphProfiler(),
            bindingCatalog: _facade.Catalog,
            storageLayout: _storageLayout,
            bootstrapLoader: _bootstrapLoader);

        _composed = true;
        AstraServerHost.MarkInitialized();
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

    private void EnsureEngineCompatible()
    {
        var manifestPath = _resolvedManifestPath ?? EngineCompatibilityService.FindManifest(_hostOptions.CompatibilityManifestPath);
        if (manifestPath == null)
        {
            throw new EngineCompatibilityException("Compatibility.json was not found. AstraGraph will not start against an unknown engine.");
        }

        var compatibility = EngineCompatibilityService.Load(manifestPath);
        _robustIdentity = EngineCompatibilityService.ResolveEngineIdentity(
            manifestPath,
            _hostOptions.RobustCommit,
            _hostOptions.RobustToolboxRoot);
        compatibility.EnsureCompatible(new EngineCompatibilityReport(
            _robustIdentity,
            compatibility.Manifest.EngineApiVersion,
            ["type-event-subscribe"]));

        if (compatibility.Manifest.DynamicNativeSystemOrdering)
        {
            throw new EngineCompatibilityException(EngineCompatibilityManifest.NativeOrderingWarning);
        }

        ReportHost(compatibility.Manifest.CompatibilityProfile);
    }

    private AstraServerHostOptions ResolveOptions()
    {
        if (_pendingOptions != null)
        {
            return _pendingOptions;
        }

        if (_storageLayout != null)
        {
            return new AstraServerHostOptions { Storage = _storageLayout };
        }

        if (AstraServerHost.TryPeek(out var registered) && registered != null)
        {
            if (registered.PermissionProvider == null && registered.AdminDirectory == null)
            {
                throw new InvalidOperationException("AstraGraph server authoring requires IAstraAdminDirectory or IAstraPermissionProvider.");
            }

            return registered;
        }

        var ioc = IoCManager.Instance;
        if (ioc != null && ioc.TryResolveType<IAstraServerHostConfiguration>(out var configuration) && configuration != null)
        {
            var fromContainer = configuration.GetOptions();
            if (fromContainer.PermissionProvider == null && fromContainer.AdminDirectory == null
                && configuration is not DefaultAstraServerHostConfiguration)
            {
                throw new InvalidOperationException("AstraGraph server authoring requires IAstraAdminDirectory or IAstraPermissionProvider.");
            }

            return fromContainer;
        }

        return AstraServerHostOptions.CreateFallback();
    }

    private static IAstraPermissionProvider ResolvePermissions(AstraServerHostOptions options)
    {
        if (options.PermissionProvider != null)
        {
            return options.PermissionProvider;
        }

        return new RobustAdminPermissionProvider(options.RequiredAdminFlag, options.AdminDirectory);
    }

    private static void ValidateStorage(StorageLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (string.IsNullOrWhiteSpace(layout.ProjectRoot) || string.IsNullOrWhiteSpace(layout.DataRoot)
            || File.Exists(layout.ProjectRoot))
        {
            throw new InvalidOperationException($"AstraGraph project root '{layout.ProjectRoot}' is invalid.");
        }

        try
        {
            layout.EnsureDirectories();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"AstraGraph data root '{layout.DataRoot}' could not be created.", ex);
        }
    }

    private void ReportHost(string? profile)
    {
        if (_reported)
        {
            return;
        }

        _reported = true;
        var providerName = _permissionProvider.GetType().Name;
        var directory = _hostOptions.AdminDirectory == null ? "not configured" : "configured";
        var layout = _storageLayout ?? throw new InvalidOperationException("AstraGraph server host is not initialized.");
        var message =
            "AstraGraph Server Host\n" +
            $"ProjectRoot: {layout.ProjectRoot}\n" +
            $"DataRoot: {layout.DataRoot}\n" +
            $"CompatibilityManifest: {_resolvedManifestPath}\n" +
            $"CompatibilityProfile: {profile}\n" +
            $"RobustIdentity: {_robustIdentity}\n" +
            $"PermissionProvider: {providerName}\n" +
            $"AdminDirectory: {directory}";
        try
        {
            Log.Info(message);
        }
        catch (NullReferenceException)
        {
            Console.WriteLine(message);
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
