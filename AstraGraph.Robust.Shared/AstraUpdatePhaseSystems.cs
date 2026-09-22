using AstraGraph.Runtime;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Timing;

namespace AstraGraph.Robust.Shared;

/// <summary>
/// Pre-simulation host system running before primary simulation systems.
/// Drives graph systems registered in AstraGraphPhase.PreSimulation.
/// </summary>
public sealed class PreSharedAstraGraphSystem : EntitySystem
{
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;

    private SharedAstraGraphSystem? _shared;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(SharedAstraGraphSystem));
        _entMan.TrySystem(out _shared);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var shared = _shared;
        if (shared == null && !_entMan.TrySystem(out shared))
            return;

        _shared = shared;
        var curTime = _gameTiming != null ? _gameTiming.CurTime.TotalSeconds : 0.0;
        var curTick = (int)_entMan.CurrentTick.Value;
        shared.Host.Scheduler.UpdatePhase(AstraGraphPhase.PreSimulation, curTime, curTick);
    }
}

/// <summary>
/// Post-simulation host system running after primary simulation systems.
/// Drives graph systems registered in AstraGraphPhase.PostSimulation.
/// </summary>
public sealed class PostSharedAstraGraphSystem : EntitySystem
{
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;

    private SharedAstraGraphSystem? _shared;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesAfter.Add(typeof(SharedAstraGraphSystem));
        _entMan.TrySystem(out _shared);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var shared = _shared;
        if (shared == null && !_entMan.TrySystem(out shared))
            return;

        _shared = shared;
        var curTime = _gameTiming != null ? _gameTiming.CurTime.TotalSeconds : 0.0;
        var curTick = (int)_entMan.CurrentTick.Value;
        shared.Host.Scheduler.UpdatePhase(AstraGraphPhase.PostSimulation, curTime, curTick);
    }
}
