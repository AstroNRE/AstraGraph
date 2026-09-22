using AstraGraph.Runtime;
using AstraGraph.State;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace AstraGraph.Robust.Shared;

/// <summary>
/// Native RobustToolbox EntitySystem acting as the shared host for AstraGraph visual gameplay systems.
/// Hooks simulation ticks, frame updates, and ECS queries into the Astra runtime.
/// </summary>
public class SharedAstraGraphSystem : EntitySystem
{
    [Dependency] private readonly IEntityManager _entMan = default!;

    private AstraGraphHost _host = default!;
    private RobustEcsQueryBridge _queryBridge = default!;
    private RobustEventBusSubscriptionAdapter _eventAdapter = default!;

    public AstraGraphHost Host => _host;
    public RobustEcsQueryBridge QueryBridge => _queryBridge;
    public RobustEventBusSubscriptionAdapter EventAdapter => _eventAdapter;

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

        Log.Info("SharedAstraGraphSystem initialized successfully.");
    }

    public MixedQueryEngine QueryEngine { get; private set; } = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var currentTick = (int)_entMan.CurrentTick.Value;
        _host.Update(frameTime, currentTick);
    }
}
