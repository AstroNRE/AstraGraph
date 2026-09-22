using AstraGraph.HotReload;
using AstraGraph.Persistence;
using AstraGraph.Robust.Shared;
using Robust.Shared.IoC;

namespace AstraGraph.Robust.Server;

/// <summary>
/// Server-side RobustToolbox EntitySystem managing persistent storage, hot reload transactions,
/// and admin permissions for AstraGraph visual scripting on live servers.
/// </summary>
public sealed class ServerAstraGraphSystem : SharedAstraGraphSystem
{
    private StorageLayout _storageLayout = default!;
    private HotReloadManager _hotReloadManager = default!;
    private RobustAdminPermissionProvider _permissionProvider = default!;

    public StorageLayout StorageLayout => _storageLayout;
    public HotReloadManager HotReloadManager => _hotReloadManager;
    public RobustAdminPermissionProvider PermissionProvider => _permissionProvider;

    public override void Initialize()
    {
        base.Initialize();

        // 1. Initialize persistent storage layout
        _storageLayout = new StorageLayout(
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "AstraGraph"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "AstraGraph"));

        // 2. Initialize HotReloadManager
        _hotReloadManager = new HotReloadManager(Host);

        // 3. Initialize Admin Permission Provider
        _permissionProvider = new RobustAdminPermissionProvider();

        // 4. Register in IoC
        IoCManager.RegisterInstance<HotReloadManager>(_hotReloadManager, overwrite: true);
        IoCManager.RegisterInstance<StorageLayout>(_storageLayout, overwrite: true);

        Log.Info("ServerAstraGraphSystem initialized with HotReloadManager and StorageLayout.");
    }
}
