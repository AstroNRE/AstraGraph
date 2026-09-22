using AstraGraph.Core;
using AstraGraph.HotReload;
using AstraGraph.Persistence;
using AstraGraph.Persistence.Discovery;
using AstraGraph.Persistence.State;
using AstraGraph.Robust.Shared;
using AstraGraph.Runtime;
using AstraGraph.Runtime.Debugging;
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
    private HotReloadManager _hotReloadManager = default!;
    private RobustAdminPermissionProvider _permissionProvider = default!;
    private PersistentStateStore _persistentStateStore = default!;
    private BootstrapLoader _bootstrapLoader = default!;
    private AstraAuthoringService _authoringService = default!;

    public StorageLayout StorageLayout => _storageLayout;
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

        _hotReloadManager ??= new HotReloadManager(Host);
        _permissionProvider ??= new RobustAdminPermissionProvider();
        _persistentStateStore ??= new PersistentStateStore(_storageLayout);
        _bootstrapLoader ??= new BootstrapLoader(_storageLayout);
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

        // Step 1: Purge abandoned crash/journal temporary files
        _bootstrapLoader.RecoverOnStartup();

        // Step 2: Restore persistent variables into Host.State
        try
        {
            _persistentStateStore.RestoreState("default", Host.State);
        }
        catch (Exception ex)
        {
            Log.Warning($"Could not restore persistent state snapshot: {ex.Message}");
        }

        // Step 3: Discover all graphs with tier precedence (Override > Live > Project)
        var discoveredGraphs = _bootstrapLoader.DiscoverAll();

        // Step 4: Publish / Activate discovered graphs
        foreach (var discovered in discoveredGraphs)
        {
            try
            {
                var doc = discovered.Document;
                if (doc.Kind == GraphKind.System)
                {
                    _hotReloadManager.Publish(doc, author: "Bootstrap", message: "Server startup bootstrap");

                    // Register in GraphScheduler if not already scheduled
                    Host.Scheduler.RegisterSystem(new SystemRegistration(
                        doc.Id,
                        doc.Name,
                        Before: [],
                        After: [],
                        Priority: 0,
                        UpdateCallback: (time, tick) =>
                        {
                            if (Host.GetProgram(doc.Id) is { } program)
                            {
                                foreach (var ep in program.EntryPoints)
                                {
                                    Host.Vm.Execute(program, ep, hostServices: null);
                                }
                            }
                        }));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error bootstrapping graph '{discovered.RelativePath}': {ex.Message}");
            }
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
