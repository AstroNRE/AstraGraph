using AstraGraph.Binding;
using AstraGraph.Core;
using AstraGraph.Persistence;
using AstraGraph.Persistence.Discovery;
using AstraGraph.Persistence.State;
using AstraGraph.Runtime;
using AstraGraph.Runtime.Security;
using AstraGraph.State;

namespace AstraGraph.HotReload;

/// <summary>
/// Composition root Content and the Robust host systems call into.
/// </summary>
public sealed class AstraGraphFacade :
    IAstraGraphManager,
    IAstraRuntime,
    IAstraTypeRegistry,
    IAstraBindingRegistry,
    IAstraStateStore,
    IAstraComponentStore,
    IAstraEventRouter,
    IAstraHotReload
{
    private readonly BootstrapLoader? _bootstrap;
    private readonly AstraBootstrapService? _pipeline;
    private bool _initialized;

    public AstraGraphFacade(
        AstraGraphHost host,
        HotReloadManager hotReload,
        BindingCatalog catalog,
        IAstraPermissionProvider permissions,
        BootstrapLoader? bootstrap = null,
        AstraBootstrapService? pipeline = null,
        PersistentStateStore? persistentState = null)
    {
        Host = host ?? throw new ArgumentNullException(nameof(host));
        Reloader = hotReload ?? throw new ArgumentNullException(nameof(hotReload));
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        Permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _bootstrap = bootstrap;
        _pipeline = pipeline ?? (bootstrap == null ? null : new AstraBootstrapService(host, hotReload, bootstrap, persistentState));
        PersistentState = persistentState;
    }

    public BootstrapLoader? Discovery => _bootstrap;
    public PersistentStateStore? PersistentState { get; }

    public static AstraGraphFacade ForServer(
        AstraGraphHost host,
        StorageLayout layout,
        IAstraPermissionProvider permissions)
    {
        return ForServer(host, layout, permissions, compatibilityManifestPath: null);
    }

    public static AstraGraphFacade ForServer(AstraGraphHost host, AstraServerHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var permissions = options.PermissionProvider
            ?? throw new InvalidOperationException("PermissionProvider must be resolved before creating the server facade.");
        return ForServer(host, options.Storage, permissions, options.CompatibilityManifestPath);
    }

    private static AstraGraphFacade ForServer(
        AstraGraphHost host,
        StorageLayout layout,
        IAstraPermissionProvider permissions,
        string? compatibilityManifestPath)
    {
        ArgumentNullException.ThrowIfNull(layout);
        layout.EnsureDirectories();
        var catalog = new BindingCatalog();
        var hotReload = new HotReloadManager(host, archive: new RevisionArchive(layout), catalog: catalog);
        var loader = new BootstrapLoader(layout);
        var state = new PersistentStateStore(layout);
        var pipeline = new AstraBootstrapService(host, hotReload, loader, state, compatibilityManifestPath);
        return new AstraGraphFacade(host, hotReload, catalog, permissions, loader, pipeline, state);
    }

    public AstraGraphHost Host { get; }
    public HotReloadManager Reloader { get; }
    public BindingCatalog Catalog { get; }
    public IAstraPermissionProvider Permissions { get; }

    public IAstraRuntime Runtime => this;
    public IAstraTypeRegistry Types => this;
    public IAstraBindingRegistry Bindings => this;
    public IAstraStateStore State => this;
    public IAstraComponentStore Components => this;
    public IAstraEventRouter Events => this;
    public IAstraHotReload HotReload => this;

    public TypeRegistry Registry { get; } = TypeRegistry.Default;
    public AstraStateStore Store => Host.State;
    public DynamicComponentStore ComponentStore => Host.Components;
    DynamicComponentStore IAstraComponentStore.Components => Host.Components;
    public GraphEventRouter Router => Host.EventRouter;

    public int LastActivatedCount { get; private set; }

    public void Initialize()
    {
        LastActivatedCount = _pipeline?.Activate() ?? 0;
        _initialized = true;
    }

    public void Update(double timeSeconds, int tick)
    {
        if (!_initialized)
        {
            Initialize();
        }

        Host.Update(timeSeconds, tick);
    }

    public void Shutdown()
    {
        _initialized = false;
    }

    public BytecodeProgram? GetProgram(GraphId graphId) => Host.GetProgram(graphId);

    public void RegisterProgram(BytecodeProgram program) => Host.RegisterProgram(program);

    public void UnregisterProgram(GraphId graphId) => Host.UnregisterProgram(graphId);

    public void IndexAssembly(System.Reflection.Assembly assembly, Func<Type, bool>? filter = null) =>
        Catalog.IndexAssembly(assembly, filter);

    public void RemoveEntity(AstraEntityId entityId)
    {
        Host.Components.ClearEntity(entityId);
        Host.Continuations.CancelByEntity(entityId);
    }

    public bool Dispatch(object? component, object eventObject) =>
        Host.EventRouter.DispatchEvent(component, eventObject);

    public PublishOutcome Publish(GraphDocument draft, string author, string message, IReadOnlyList<SchemaType>? schemas = null)
    {
        SchemaType? declaredSchema = schemas is { Count: > 0 } ? schemas[0] : null;
        var result = Reloader.Publish(draft, author, message, declaredSchema);
        return new PublishOutcome(result.Success, result.PublishedRevisionId, result.ErrorMessage);
    }

    public bool Rollback(GraphId graphId) => Reloader.Rollback(graphId).Success;
}
