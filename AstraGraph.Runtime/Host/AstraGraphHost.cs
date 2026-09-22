using System.Collections.Concurrent;
using AstraGraph.Core;
using AstraGraph.State;
using AstraGraph.VM;

namespace AstraGraph.Runtime;

/// <summary>
/// Native host coordinating the execution of AstraGraph systems, event routing,
/// latent continuations, dynamic components, and state storage.
/// </summary>
public sealed class AstraGraphHost
{
    private readonly ConcurrentDictionary<GraphId, BytecodeProgram> _activePrograms = new();

    public GraphScheduler Scheduler { get; }
    public GraphEventRouter EventRouter { get; }
    public ContinuationScheduler Continuations { get; }
    public DynamicComponentStore Components { get; }
    public AstraStateStore State { get; }
    public AstraVm Vm { get; }
    public IVmHostServices HostServices { get; }
    public Debugging.GraphDebugger Debugger { get; }

    public AstraGraphHost(
        AstraVm? vm = null,
        DynamicComponentStore? components = null,
        AstraStateStore? state = null,
        IVmHostServices? hostServices = null,
        Debugging.GraphDebugger? debugger = null)
    {
        Vm = vm ?? new AstraVm();
        Components = components ?? new DynamicComponentStore();
        State = state ?? new AstraStateStore();
        HostServices = hostServices ?? new DefaultVmHostServices();
        Debugger = debugger ?? new Debugging.GraphDebugger();
        Scheduler = new GraphScheduler();
        EventRouter = new GraphEventRouter();
        Continuations = new ContinuationScheduler(Vm, HostServices);
    }

    public void RegisterProgram(BytecodeProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        _activePrograms[program.Id] = program;
    }

    public void UnregisterProgram(GraphId graphId)
    {
        _activePrograms.TryRemove(graphId, out _);
        EventRouter.UnsubscribeGraph(graphId);
        Scheduler.UnregisterGraph(graphId);
        Continuations.CancelByGraph(graphId);
    }

    public BytecodeProgram? GetProgram(GraphId graphId) =>
        _activePrograms.GetValueOrDefault(graphId);

    public void Update(double currentTimeSeconds, int currentTick)
    {
        // 1. Update systems DAG
        Scheduler.Update(currentTimeSeconds, currentTick);

        // 2. Update latent continuations (Delay, DoAfter, etc.)
        Continuations.Update(currentTimeSeconds, currentTick);
    }
}
