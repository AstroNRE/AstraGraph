using System.Collections.Concurrent;
using AstraGraph.Core;
using AstraGraph.Runtime;

namespace AstraGraph.HotReload;

/// <summary>
/// Orchestrates safe, atomic, transactional hot reload of gameplay graphs at tick boundaries
/// with automatic Last Known Good (LKG) fallback and history logging.
/// </summary>
public sealed class HotReloadManager
{
    private readonly AstraGraphHost _host;
    private readonly SemanticAnalyzer _semanticAnalyzer;
    private readonly ConcurrentDictionary<GraphId, BytecodeProgram> _lastKnownGood = new();
    private readonly ConcurrentDictionary<GraphId, List<RevisionRecord>> _history = new();
    private readonly ConcurrentDictionary<GraphId, bool> _frozenGraphs = new();
    private readonly Lock _lock = new();

    public HotReloadManager(AstraGraphHost host, SemanticAnalyzer? semanticAnalyzer = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _semanticAnalyzer = semanticAnalyzer ?? new SemanticAnalyzer();
    }

    private readonly ConcurrentDictionary<SchemaId, SchemaType> _activeSchemas = new();

    public void RegisterActiveSchema(SchemaType schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        _activeSchemas[schema.Id] = schema;
    }

    public PublishResult Publish(
        GraphDocument draft,
        string author = "System",
        string message = "",
        SchemaType? declaredSchema = null)
    {
        ArgumentNullException.ThrowIfNull(draft);

        // 1. Semantic Analysis & Type Checking
        var semanticResult = _semanticAnalyzer.Analyze(draft);
        if (!semanticResult.Success || semanticResult.Program is null)
        {
            return new PublishResult(false, null, semanticResult.Diagnostics, "Semantic analysis failed with errors.");
        }

        // 2. Schema Migration Planning
        SchemaMigrationPlan? migrationPlan = null;
        if (declaredSchema != null && _activeSchemas.TryGetValue(declaredSchema.Id, out var oldSchema))
        {
            migrationPlan = StateMigrationPlanner.CreatePlan(oldSchema, declaredSchema);
            if (!migrationPlan.CanAutoMigrate)
            {
                semanticResult.Diagnostics.ReportError("MIG001", $"Schema '{declaredSchema.Name}' has incompatible field changes that cannot be auto-migrated.");
                return new PublishResult(false, null, semanticResult.Diagnostics, "Incompatible schema changes require explicit migration.");
            }
        }

        BytecodeProgram newProgram;
        var newRevisionId = RevisionId.New();
        var semanticHash = AstraHash.ComputeSemanticHash(draft);

        try
        {
            // 3. Compile to IR and Bytecode
            var irProgram = AstToIrCompiler.Compile(semanticResult.Program);
            newProgram = IrToBytecodeCompiler.Compile(irProgram, newRevisionId, semanticHash);
        }
        catch (Exception ex)
        {
            semanticResult.Diagnostics.ReportError("PUB001", $"Compilation failed: {ex.Message}");
            return new PublishResult(false, null, semanticResult.Diagnostics, ex.Message);
        }

        // 4. Safe Tick Boundary: Freeze dispatch and execute migration & swap
        lock (_lock)
        {
            _frozenGraphs[draft.Id] = true;
            try
            {
                var currentProgram = _host.GetProgram(draft.Id);
                var parentRev = currentProgram?.Revision;

                if (currentProgram != null)
                {
                    _lastKnownGood[draft.Id] = currentProgram;
                }

                // 5. Execute state migration if schema evolved
                if (declaredSchema != null)
                {
                    if (migrationPlan != null)
                    {
                        _host.Components.MigrateSchema(declaredSchema.Id, declaredSchema, migrationPlan.Execute);
                    }
                    _activeSchemas[declaredSchema.Id] = declaredSchema;
                }

                // 6. Atomic Pointer Swap in Host
                _host.RegisterProgram(newProgram);

                // Register event subscriptions
                _host.EventRouter.UnsubscribeGraph(draft.Id);
                foreach (var ep in newProgram.EntryPoints)
                {
                    var epName = newProgram.Constants[ep.NameConstantIndex].Value?.ToString() ?? "unnamed";
                    _host.EventRouter.Subscribe(
                        componentType: null,
                        eventType: typeof(object), // generic router dispatch
                        graphId: draft.Id,
                        entryPointName: epName,
                        handler: (comp, ev) =>
                        {
                            if (IsDispatchAllowed(draft.Id))
                            {
                                _host.Vm.Execute(newProgram, ep, hostServices: null);
                            }
                        });
                }

                // 7. Append to Revision History
                var record = new RevisionRecord(
                    draft.Id,
                    newRevisionId,
                    parentRev,
                    semanticHash,
                    author,
                    DateTimeOffset.UtcNow,
                    message,
                    newProgram);

                var historyList = _history.GetOrAdd(draft.Id, _ => []);
                historyList.Add(record);
            }
            finally
            {
                // 8. Unfreeze dispatch
                _frozenGraphs.TryRemove(draft.Id, out _);
            }
        }

        return new PublishResult(true, newRevisionId, semanticResult.Diagnostics);
    }

    public bool Rollback(GraphId graphId)
    {
        lock (_lock)
        {
            if (_lastKnownGood.TryGetValue(graphId, out var lkg))
            {
                _host.RegisterProgram(lkg);
                return true;
            }
            return false;
        }
    }

    public bool IsDispatchAllowed(GraphId graphId) => !_frozenGraphs.ContainsKey(graphId);

    public IReadOnlyList<RevisionRecord> GetRevisionHistory(GraphId graphId)
    {
        lock (_lock)
        {
            if (_history.TryGetValue(graphId, out var list))
            {
                return [.. list];
            }
            return [];
        }
    }
}
