using AstraGraph.Binding;
using AstraGraph.Runtime;
using AstraGraph.VM;
using AstraGraph.Runtime.Network;
using AstraGraph.State;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace AstraGraph.Robust.Shared;

/// <summary>
/// Native RobustToolbox EntitySystem acting as the shared host for AstraGraph visual gameplay systems.
/// Hooks simulation ticks, frame updates, ECS queries, and entity lifecycle cleanup into the Astra runtime.
/// </summary>
public class SharedAstraGraphSystem : EntitySystem
{
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly INetManager _net = default!;

    private AstraGraphHost? _host;
    private RobustEcsQueryBridge? _queryBridge;
    private RobustEventBusSubscriptionAdapter? _eventAdapter;

    public AstraGraphHost Host
    {
        get => _host ??= new AstraGraphHost();
        protected set => _host = value;
    }

    public RobustEcsQueryBridge? QueryBridge => _queryBridge;
    public RobustEventBusSubscriptionAdapter? EventAdapter => _eventAdapter;
    public MixedQueryEngine? QueryEngine { get; private set; }
    public IAstraNetworkTransport? SyncTransport { get; private set; }
    public RobustPredictionAdapter? Prediction { get; private set; }

    public SharedActivationCoordinator? Activation { get; private set; }

    public override void Initialize()
    {
        base.Initialize();

        // 1. Initialize standalone ECS stores & host
        var componentStore = new DynamicComponentStore();
        if (_entMan != null)
        {
            _queryBridge = new RobustEcsQueryBridge(_entMan);
            QueryEngine = new MixedQueryEngine(componentStore, _queryBridge);
        }

        IVmHostServices? services = _entMan != null ? new RobustVmHostServices(_entMan) : null;
        _host = new AstraGraphHost(components: componentStore, hostServices: services);
        Activation = new SharedActivationCoordinator(_host);
        _eventAdapter = new RobustEventBusSubscriptionAdapter(_host.EventRouter);
        if (_net != null)
        {
            SyncTransport = new RobustNetManagerTransport(_net);
        }

        if (_gameTiming != null)
        {
            Prediction = new RobustPredictionAdapter(_gameTiming, new PredictionReconciler(componentStore));
        }

        // 2. Register Host in IoC for access across the engine
        try
        {
            IoCManager.RegisterInstance<AstraGraphHost>(_host, overwrite: true);
        }
        catch
        {
        }

        // 3. Hook entity lifecycle events for deterministic cleanup
        if (_entMan != null)
        {
            _entMan.EntityDeleted += OnEntityDeleted;
        }

        Log.Info("SharedAstraGraphSystem initialized successfully.");
    }

    public override void Shutdown()
    {
        if (_entMan != null)
        {
            _entMan.EntityDeleted -= OnEntityDeleted;
        }
        base.Shutdown();
    }

    private void OnEntityDeleted(Entity<MetaDataComponent> entity)
    {
        var uid = (int)entity.Owner.Id;
        Host.Components.ClearEntity(uid);
        Host.Continuations.CancelByEntity(uid);
        Host.State.ClearEntity(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Use synchronized simulation uptime from IGameTiming rather than raw frame delta
        var currentTimeSeconds = _gameTiming != null ? _gameTiming.CurTime.TotalSeconds : 0.0;
        var currentTick = _entMan != null ? (int)_entMan.CurrentTick.Value : 0;

        Host.Update(currentTimeSeconds, currentTick);
        Activation?.Update(currentTick);
    }

    public void AttachGameplay(BindingCatalog catalog)
    {
        if (_entMan == null)
        {
            return;
        }

        GameplayBindings.Index(catalog, _entMan);
        if (Host.HostServices is RobustVmHostServices services)
        {
            services.UseCatalog(catalog);
        }
    }
}
