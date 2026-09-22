using System.Collections.Concurrent;
using AstraGraph.Core;
using AstraGraph.Runtime.Network;
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
    public int ExecutedEntryPoints { get; private set; }
    public Debugging.GraphDebugger Debugger { get; }
    public GraphFaultLog Faults { get; }

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
        Faults = new GraphFaultLog();
        Scheduler = new GraphScheduler(Faults);
        EventRouter = new GraphEventRouter(Faults);
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

    public void NoteEntryExecuted() => ExecutedEntryPoints++;

    public IReadOnlyList<EntityComponentView> InspectEntities()
    {
        var list = new List<EntityComponentView>();
        foreach (var schemaId in Components.GetAllSchemas())
        {
            foreach (var entity in Components.GetEntitiesWithComponent(schemaId))
            {
                var storage = Components.GetComponent(entity, schemaId);
                var fields = new EntityFieldView[storage.FieldCount];
                for (var slot = 0; slot < storage.FieldCount; slot++)
                {
                    fields[slot] = new EntityFieldView(storage.Schema.Fields[slot].Name, storage.GetField(slot).ToString());
                }

                list.Add(new EntityComponentView(entity.Value, storage.Schema.Name, fields));
            }
        }

        return list.OrderBy(item => item.EntityId).ThenBy(item => item.SchemaName).ToList();
    }

    public int PendingReplicationBytes() => DeltaReplicationManager.MeasurePendingBytes(Components);

    public void Update(double currentTimeSeconds, int currentTick)
    {
        // 1. Update systems DAG
        Scheduler.Update(currentTimeSeconds, currentTick);

        // 2. Update latent continuations (Delay, DoAfter, etc.)
        Continuations.Update(currentTimeSeconds, currentTick);
    }
}

public sealed record EntityFieldView(string Name, string Value);

public sealed record EntityComponentView(int EntityId, string SchemaName, IReadOnlyList<EntityFieldView> Fields);
