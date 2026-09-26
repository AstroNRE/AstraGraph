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
    public int CurrentTick { get; private set; }
    public Debugging.GraphDebugger Debugger { get; }
    public GraphFaultLog Faults { get; }
    private readonly List<LatentEntry> _latent = [];

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
        _latent.RemoveAll(entry => entry.GraphId == graphId);
    }

    public void ScheduleLatent(
        GraphId graphId,
        BytecodeProgram program,
        BytecodeFunction function,
        ContinuationState yield,
        AstraEventInvocationContext? context,
        IReadOnlyDictionary<string, AstraValue> variables)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(yield);
        var registers = new AstraValue[yield.Arguments.Count];
        for (var index = 0; index < registers.Length; index++)
        {
            registers[index] = yield.Arguments[index];
        }

        var delay = yield.DelaySeconds > 0 ? yield.DelaySeconds : 1;
        var entry = new LatentEntry(
            graphId,
            program,
            function,
            registers,
            yield.NextInstructionPointer,
            delay,
            context,
            new Dictionary<string, AstraValue>(variables, StringComparer.Ordinal))
        {
            BarToken = StartBar(yield.Kind, context, delay)
        };
        _latent.Add(entry);
    }

    private int? StartBar(ContinuationKind kind, AstraEventInvocationContext? context, double seconds)
    {
        if (kind != ContinuationKind.DoAfter || BeginDoAfterBar == null || context == null)
        {
            return null;
        }

        if (context.Entity.Type is not (AstraValueType.EntityUid or AstraValueType.Int64))
        {
            return null;
        }

        var entity = context.Entity.AsEntityUid();
        return entity == 0 ? null : BeginDoAfterBar(entity, seconds);
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

    public int PendingLatent => _latent.Count;

    /// <summary>
    /// Starts the engine progress bar for a do-after on the player.
    /// Returns a token, or null when the bar is unavailable and the timer should run.
    /// </summary>
    public Func<int, double, int?>? BeginDoAfterBar { get; set; }

    /// <summary>
    /// 0 while the bar is running, 1 when it finishes, 2 when it is cancelled.
    /// </summary>
    public Func<int, int>? ReadDoAfterBar { get; set; }

    public void Update(double currentTimeSeconds, int currentTick)
    {
        CurrentTick = currentTick;
        // 1. Update systems DAG
        Scheduler.Update(currentTimeSeconds, currentTick);

        // 2. Update latent continuations (Delay, DoAfter, etc.)
        Continuations.Update(currentTimeSeconds, currentTick);
        ResumeLatent(currentTimeSeconds);
    }

    private void ResumeLatent(double currentTimeSeconds)
    {
        for (var index = _latent.Count - 1; index >= 0; index--)
        {
            var entry = _latent[index];
            if (entry.BarToken is int token && ReadDoAfterBar != null)
            {
                switch (ReadDoAfterBar(token))
                {
                    case 0:
                        continue;
                    case 2:
                        _latent.RemoveAt(index);
                        continue;
                }
            }
            else
            {
                if (entry.ReadyAt == 0)
                {
                    entry.ReadyAt = currentTimeSeconds + entry.DelaySeconds;
                }

                if (currentTimeSeconds < entry.ReadyAt)
                {
                    continue;
                }
            }

            HostServices.PushVariables();
            HostServices.ReplaceVariables(entry.Variables);
            if (entry.Context != null)
            {
                HostServices.PushEventContext(entry.Context);
            }

            VmExecutionResult result;
            Dictionary<string, AstraValue>? nextVariables = null;
            try
            {
                result = Vm.Execute(
                    entry.Program,
                    entry.Function,
                    initialRegisters: entry.Registers,
                    startIp: entry.InstructionPointer,
                    hostServices: HostServices,
                    debugHook: Debugger);
                if (result.IsYielded && result.YieldState != null)
                {
                    nextVariables = HostServices.SnapshotVariables();
                }
            }
            finally
            {
                if (entry.Context != null)
                {
                    HostServices.PopEventContext();
                }

                HostServices.PopVariables();
            }

            if (result.IsYielded && result.YieldState != null)
            {
                var again = result.YieldState;
                var registers = new AstraValue[again.Arguments.Count];
                for (var register = 0; register < registers.Length; register++)
                {
                    registers[register] = again.Arguments[register];
                }

                entry.Registers = registers;
                entry.InstructionPointer = again.NextInstructionPointer;
                entry.DelaySeconds = again.DelaySeconds > 0 ? again.DelaySeconds : 1;
                entry.ReadyAt = 0;
                entry.BarToken = StartBar(again.Kind, entry.Context, entry.DelaySeconds);
                entry.Variables = nextVariables ?? [];
                continue;
            }

            _latent.RemoveAt(index);
        }
    }

    private sealed class LatentEntry(
        GraphId graphId,
        BytecodeProgram program,
        BytecodeFunction function,
        AstraValue[] registers,
        int instructionPointer,
        double delaySeconds,
        AstraEventInvocationContext? context,
        Dictionary<string, AstraValue> variables)
    {
        public GraphId GraphId { get; } = graphId;
        public BytecodeProgram Program { get; } = program;
        public BytecodeFunction Function { get; } = function;
        public AstraValue[] Registers { get; set; } = registers;
        public int InstructionPointer { get; set; } = instructionPointer;
        public double DelaySeconds { get; set; } = delaySeconds;
        public double ReadyAt { get; set; }
        public int? BarToken { get; set; }
        public AstraEventInvocationContext? Context { get; } = context;
        public Dictionary<string, AstraValue> Variables { get; set; } = variables;
    }
}

public sealed record EntityFieldView(string Name, string Value);

public sealed record EntityComponentView(int EntityId, string SchemaName, IReadOnlyList<EntityFieldView> Fields);
