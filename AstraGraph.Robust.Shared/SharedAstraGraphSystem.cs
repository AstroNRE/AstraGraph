using AstraGraph.Runtime;
using AstraGraph.State;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
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

    private AstraGraphHost _host = default!;
    private RobustEcsQueryBridge _queryBridge = default!;
    private RobustEventBusSubscriptionAdapter _eventAdapter = default!;

    public AstraGraphHost Host => _host;
    public RobustEcsQueryBridge QueryBridge => _queryBridge;
    public RobustEventBusSubscriptionAdapter EventAdapter => _eventAdapter;
    public MixedQueryEngine QueryEngine { get; private set; } = default!;

    public override void Initialize()
    {
        base.Initialize();

        // 1. Initialize standalone ECS stores & host
        var componentStore = new DynamicComponentStore();
        _queryBridge = new RobustEcsQueryBridge(_entMan);
        QueryEngine = new MixedQueryEngine(componentStore, _queryBridge);

        _host = new AstraGraphHost(components: componentStore);
        _eventAdapter = new RobustEventBusSubscriptionAdapter(_host.EventRouter);

        // 2. Register Host in IoC for access across the engine
        IoCManager.RegisterInstance<AstraGraphHost>(_host, overwrite: true);

        // 3. Hook entity lifecycle events for deterministic cleanup
        _entMan.EntityDeleted += OnEntityDeleted;

        Log.Info("SharedAstraGraphSystem initialized successfully.");
    }

    public override void Shutdown()
    {
        _entMan.EntityDeleted -= OnEntityDeleted;
        base.Shutdown();
    }

    private void OnEntityDeleted(Entity<MetaDataComponent> entity)
    {
        var uid = (int)entity.Owner.Id;
        _host.Components.ClearEntity(uid);
        _host.Continuations.CancelByEntity(uid);
        _host.State.ClearEntity(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Use synchronized simulation uptime from IGameTiming rather than raw frame delta
        var currentTimeSeconds = _gameTiming != null ? _gameTiming.CurTime.TotalSeconds : 0.0;
        var currentTick = (int)_entMan.CurrentTick.Value;

        _host.Update(currentTimeSeconds, currentTick);
    }
}
