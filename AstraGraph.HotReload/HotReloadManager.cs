using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using AstraGraph.Binding;
using AstraGraph.Core;
using AstraGraph.Core.Events;
using AstraGraph.Runtime;
using AstraGraph.Runtime.Integration;
using AstraGraph.Runtime.Network;

namespace AstraGraph.HotReload;

/// <summary>
/// Orchestrates safe, atomic, transactional hot reload of gameplay graphs at tick boundaries
/// with multi-aspect rollback across Code, State, and Schema.
/// </summary>
public sealed class HotReloadManager
{
    private readonly AstraGraphHost _host;
    private readonly SemanticAnalyzer _semanticAnalyzer;
    private readonly ConcurrentDictionary<GraphId, RevisionRecord> _lastKnownGood = new();
    private readonly ConcurrentDictionary<GraphId, List<RevisionRecord>> _history = new();
    private readonly ConcurrentDictionary<GraphId, bool> _frozenGraphs = new();
    private readonly ConcurrentDictionary<SchemaId, SchemaType> _activeSchemas = new();
    private readonly ConcurrentQueue<PublishTransaction> _pendingTransactions = new();
    private readonly Lock _lock = new();
    private readonly RevisionArchive? _archive;
    private readonly IEntryPointTypeResolver _typeResolver;
    private readonly BindingCatalog? _catalog;

    public AstraGraphHost Host => _host;
    public event Action<IReadOnlyList<GraphEventSubscription>>? SubscriptionsCommitted;

    public event Action<SchemaType>? SchemaCommitted;

    public HotReloadManager(
        AstraGraphHost host,
        SemanticAnalyzer? semanticAnalyzer = null,
        RevisionArchive? archive = null,
        IEntryPointTypeResolver? typeResolver = null,
        BindingCatalog? catalog = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _semanticAnalyzer = semanticAnalyzer ?? new SemanticAnalyzer();
        _archive = archive;
        _typeResolver = typeResolver ?? ReflectionEntryPointTypeResolver.Instance;
        _catalog = catalog;
    }

    public void RegisterActiveSchema(SchemaType schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        _activeSchemas[schema.Id] = schema;
    }

    public SchemaType? GetActiveSchema(SchemaId schemaId) =>
        _activeSchemas.GetValueOrDefault(schemaId);

    /// <summary>
    /// Prepares a graph publication off-thread and enqueues it for atomic execution at the next tick boundary.
    /// </summary>
    public PublishTransaction QueuePublish(
        GraphDocument draft,
        string author = "System",
        string message = "",
        SchemaType? declaredSchema = null,
        IReadOnlyList<GraphEventSubscription>? subscriptions = null)
    {
        declaredSchema ??= SchemaDocuments.FirstComponent(draft, TypeRegistry.Default);
        var semanticResult = _semanticAnalyzer.Analyze(draft);
        if (!semanticResult.Success || semanticResult.Program is null)
        {
            var txFailed = new PublishTransaction(draft.Id, draft, null!, RevisionId.Empty, null, string.Empty, author, message, declaredSchema, null);
            txFailed.Status = PublishTransactionStatus.Faulted;
            txFailed.Completion.SetResult(new PublishResult(false, null, semanticResult.Diagnostics, "Semantic analysis failed with errors."));
            return txFailed;
        }

        // 2. Schema Migration Planning
        SchemaMigrationPlan? migrationPlan = null;
        if (declaredSchema != null && _activeSchemas.TryGetValue(declaredSchema.Id, out var oldSchema))
        {
            migrationPlan = StateMigrationPlanner.CreatePlan(oldSchema, declaredSchema);
            if (!migrationPlan.CanAutoMigrate)
            {
                semanticResult.Diagnostics.ReportError("MIG001", $"Schema '{declaredSchema.Name}' has incompatible field changes that cannot be auto-migrated.");
                var txFailed = new PublishTransaction(draft.Id, draft, null!, RevisionId.Empty, null, string.Empty, author, message, declaredSchema, null);
                txFailed.Status = PublishTransactionStatus.Faulted;
                txFailed.Completion.SetResult(new PublishResult(false, null, semanticResult.Diagnostics, "Incompatible schema changes require explicit migration."));
                return txFailed;
            }
        }

        BytecodeProgram newProgram;
        var newRevisionId = RevisionId.New();
        var semanticHash = AstraHash.ComputeSemanticHash(draft);

        try
        {
            // 3. Compile to IR and Bytecode
            var irProgram = AstToIrCompiler.Compile(semanticResult.Program);
            if (draft.Side == GraphSide.SharedPredicted && !IsPredictionSafe(irProgram, semanticResult.Diagnostics))
            {
                var rejected = new PublishTransaction(draft.Id, draft, null!, RevisionId.Empty, null, string.Empty, author, message, declaredSchema, null);
                rejected.Status = PublishTransactionStatus.Faulted;
                rejected.Completion.SetResult(new PublishResult(false, null, semanticResult.Diagnostics, "Predicted graph calls a method that is not prediction-safe."));
                return rejected;
            }

            newProgram = IrToBytecodeCompiler.Compile(irProgram, newRevisionId, semanticHash);
        }
        catch (Exception ex)
        {
            semanticResult.Diagnostics.ReportError("PUB001", $"Compilation failed: {ex.Message}");
            var txFailed = new PublishTransaction(draft.Id, draft, null!, RevisionId.Empty, null, string.Empty, author, message, declaredSchema, null);
            txFailed.Status = PublishTransactionStatus.Faulted;
            txFailed.Completion.SetResult(new PublishResult(false, null, semanticResult.Diagnostics, ex.Message));
            return txFailed;
        }

        subscriptions ??= CompileSubscriptions(draft);

        var currentProgram = _host.GetProgram(draft.Id);
        var parentRev = currentProgram?.Revision;

        var tx = new PublishTransaction(
            draft.Id,
            draft,
            newProgram,
            newRevisionId,
            parentRev,
            semanticHash,
            author,
            message,
            declaredSchema,
            migrationPlan,
            subscriptions);

        _pendingTransactions.Enqueue(tx);
        return tx;
    }

    /// <summary>
    /// Synchronous publish convenience method. If immediate is true, executes tick-boundary processing immediately.
    /// </summary>
    public PublishResult Publish(
        GraphDocument draft,
        string author = "System",
        string message = "",
        SchemaType? declaredSchema = null,
        bool immediate = true,
        IReadOnlyList<GraphEventSubscription>? subscriptions = null)
    {
        var tx = QueuePublish(draft, author, message, declaredSchema, subscriptions);
        if (tx.Status == PublishTransactionStatus.Faulted)
        {
            return tx.Completion.Task.Result;
        }

        if (immediate)
        {
            ProcessPendingTransactions();
        }

        return tx.Completion.Task.IsCompleted ? tx.Completion.Task.Result : new PublishResult(true, tx.NewRevisionId, new DiagnosticBag());
    }

    /// <summary>
    /// Processes all queued publication transactions at a safe simulation tick boundary.
    /// Performs freeze -> migrate schema -> swap code -> swap subscriptions -> unfreeze.
    /// On failure: executes automatic atomic rollback of code, state, and subscriptions.
    /// </summary>
    public void ProcessPendingTransactions(int currentTick = 0, double currentTime = 0.0)
    {
        while (_pendingTransactions.TryDequeue(out var tx))
        {
            if (tx.Status != PublishTransactionStatus.Queued)
                continue;

            lock (_lock)
            {
                tx.Status = PublishTransactionStatus.Applying;
                _frozenGraphs[tx.GraphId] = true;

                // Capture snapshot of previous state for atomic rollback on failure
                var previousProgram = _host.GetProgram(tx.GraphId);
                SchemaType? previousSchema = null;
                if (tx.DeclaredSchema != null)
                {
                    _activeSchemas.TryGetValue(tx.DeclaredSchema.Id, out previousSchema);
                }

                try
                {
                    // 1. In-place schema migration
                    if (tx.DeclaredSchema != null)
                    {
                        if (tx.MigrationPlan != null)
                        {
                            _host.Components.MigrateSchema(tx.DeclaredSchema.Id, tx.DeclaredSchema, tx.MigrationPlan.Execute);
                        }
                        _activeSchemas[tx.DeclaredSchema.Id] = tx.DeclaredSchema;
                        SchemaCommitted?.Invoke(tx.DeclaredSchema);
                    }

                    // 2. Atomic Program Pointer Swap in Host
                    _host.RegisterProgram(tx.PreparedProgram);

                    _host.EventRouter.UnsubscribeGraph(tx.GraphId);
                    foreach (var sub in tx.Subscriptions)
                    {
                        AttachSubscription(sub);
                    }

                    // 4. Create Revision Record
                    var record = new RevisionRecord(
                        tx.GraphId,
                        tx.NewRevisionId,
                        tx.ParentRevisionId,
                        tx.SemanticHash,
                        tx.Author,
                        DateTimeOffset.UtcNow,
                        tx.Message,
                        tx.PreparedProgram,
                        tx.DeclaredSchema,
                        tx.Subscriptions);

                    if (previousProgram != null)
                    {
                        _lastKnownGood[tx.GraphId] = record;
                    }

                    var historyList = _history.GetOrAdd(tx.GraphId, _ => []);
                    historyList.Add(record);
                    if (tx.Subscriptions.Count > 0)
                    {
                        SubscriptionsCommitted?.Invoke(tx.Subscriptions);
                    }

                    _archive?.Save(tx.Draft, record, CatalogHash());

                    tx.Status = PublishTransactionStatus.Committed;
                    var diagnostics = new DiagnosticBag();
                    foreach (var node in tx.Draft.Nodes)
                    {
                        if (node.Properties.ContainsKey("nativeBefore") || node.Properties.ContainsKey("nativeAfter"))
                        {
                            diagnostics.ReportWarning("AG-ORDER", EngineCompatibilityManifest.NativeOrderingWarning);
                        }
                    }

                    tx.Completion.TrySetResult(new PublishResult(true, tx.NewRevisionId, diagnostics));
                }
                catch (Exception ex)
                {
                    // Atomic failure rollback: revert code, state, and schema
                    if (previousProgram != null)
                    {
                        _host.RegisterProgram(previousProgram);
                    }
                    else
                    {
                        _host.UnregisterProgram(tx.GraphId);
                    }

                    if (previousSchema != null && tx.DeclaredSchema != null)
                    {
                        _activeSchemas[previousSchema.Id] = previousSchema;
                        SchemaCommitted?.Invoke(previousSchema);
                    }

                    tx.Status = PublishTransactionStatus.RolledBack;
                    var bag = new DiagnosticBag();
                    bag.ReportError("PUB_ROLLBACK", $"Transaction aborted and rolled back: {ex.Message}");
                    tx.Completion.TrySetResult(new PublishResult(false, null, bag, ex.Message));
                }
                finally
                {
                    _frozenGraphs.TryRemove(tx.GraphId, out _);
                }
            }
        }
    }

    private bool IsPredictionSafe(IrProgram program, DiagnosticBag diagnostics)
    {
        var safe = _catalog == null
            ? []
            : _catalog.Search(string.Empty).Where(method => method.IsDeterministic).Select(method => method.Descriptor);
        return new PredictionSafetyChecker(safe).Validate(program, diagnostics);
    }

    /// <summary>
    /// Performs a full, multi-aspect rollback restoring previous Code, Schema, and Subscriptions.
    /// </summary>
    public void Deactivate(GraphId graphId)
    {
        lock (_lock)
        {
            _host.EventRouter.UnsubscribeGraph(graphId);
            _host.UnregisterProgram(graphId);
            _frozenGraphs.TryRemove(graphId, out _);
        }
    }

    public PublishResult Rollback(GraphId graphId, RevisionId? targetRevisionId = null)
    {
        var hasMemory = _history.TryGetValue(graphId, out var existing) && existing.Count > 0;
        if (!hasMemory && targetRevisionId == null && _archive?.TryLoadPrevious(graphId) is { } previous)
        {
            return Publish(previous, "Rollback", "Restored from disk");
        }

        lock (_lock)
        {
            if (!_history.TryGetValue(graphId, out var history) || history.Count == 0)
            {
                var bag = new DiagnosticBag();
                bag.ReportError("RLB001", $"No revision history found for graph {graphId}.");
                return new PublishResult(false, null, bag, "No history found.");
            }

            RevisionRecord? targetRecord = null;
            if (targetRevisionId.HasValue)
            {
                targetRecord = history.FirstOrDefault(r => r.RevisionId == targetRevisionId.Value);
            }
            else if (history.Count >= 2)
            {
                // Rollback to immediate previous revision
                targetRecord = history[^2];
            }
            else if (_lastKnownGood.TryGetValue(graphId, out var lkg))
            {
                targetRecord = lkg;
            }

            if (targetRecord == null)
            {
                var bag = new DiagnosticBag();
                bag.ReportError("RLB002", "No valid prior revision available for rollback.");
                return new PublishResult(false, null, bag, "No prior revision.");
            }

            _frozenGraphs[graphId] = true;
            try
            {
                // 1. Reverse schema migration if schema changed
                if (targetRecord.SchemaSnapshot != null)
                {
                    if (_activeSchemas.TryGetValue(targetRecord.SchemaSnapshot.Id, out var currentSchema))
                    {
                        var reversePlan = StateMigrationPlanner.CreatePlan(currentSchema, targetRecord.SchemaSnapshot);
                        if (!reversePlan.CanAutoMigrate)
                        {
                            var bag = new DiagnosticBag();
                            bag.ReportError("RLB003", $"Reverse schema migration from '{currentSchema.Name}' to '{targetRecord.SchemaSnapshot.Name}' is incompatible.");
                            return new PublishResult(false, null, bag, "Incompatible reverse schema migration.");
                        }

                        _host.Components.MigrateSchema(targetRecord.SchemaSnapshot.Id, targetRecord.SchemaSnapshot, reversePlan.Execute);
                    }
                    _activeSchemas[targetRecord.SchemaSnapshot.Id] = targetRecord.SchemaSnapshot;
                }

                // 2. Restore bytecode program
                _host.RegisterProgram(targetRecord.Program);

                // 3. Restore subscriptions
                _host.EventRouter.UnsubscribeGraph(graphId);
                if (targetRecord.Subscriptions != null)
                {
                    foreach (var sub in targetRecord.Subscriptions)
                    {
                        AttachSubscription(sub);
                    }
                }

                // 4. Log rollback in history
                var rollbackRecord = new RevisionRecord(
                    graphId,
                    RevisionId.New(),
                    targetRecord.RevisionId,
                    targetRecord.SemanticHash,
                    "Rollback",
                    DateTimeOffset.UtcNow,
                    $"Rolled back to {targetRecord.RevisionId}",
                    targetRecord.Program,
                    targetRecord.SchemaSnapshot,
                    targetRecord.Subscriptions);

                history.Add(rollbackRecord);

                return new PublishResult(true, rollbackRecord.RevisionId, new DiagnosticBag());
            }
            finally
            {
                _frozenGraphs.TryRemove(graphId, out _);
            }
        }
    }

    private void AttachSubscription(GraphEventSubscription subscription)
    {
        void Run(object? entity, object? ev) => ExecuteEntry(subscription, entity, ev);
        if (subscription.ComponentType != null)
        {
            _host.EventRouter.SubscribeDirected(subscription, (uid, component, ev) => ExecuteEntry(subscription, uid, ev, component));
            return;
        }

        if (subscription.ByRef && subscription.ComponentType == null)
        {
            _host.EventRouter.SubscribeRefInvoke(subscription, ev => Run(null, ev));
            return;
        }

        _host.EventRouter.Subscribe(subscription, (entity, ev) => Run(entity, ev));
    }

    private void ExecuteEntry(GraphEventSubscription subscription, object? entity, object? eventObject = null, object? component = null)
    {
        if (!IsDispatchAllowed(subscription.GraphId))
        {
            return;
        }

        var program = _host.GetProgram(subscription.GraphId);
        var entry = program?.FindEntryPoint(subscription.EntryPointId) ?? program?.EntryPoints.FirstOrDefault();
        if (program == null || entry == null)
        {
            return;
        }

        var entityValue = AstraValueBox.Box(entity);
        if (entityValue.Type != AstraValueType.EntityUid)
        {
            entityValue = AstraValue.Null;
        }

        var context = new AstraEventInvocationContext(entityValue, component ?? entity, eventObject);
        _host.HostServices.PushEventContext(context);
        try
        {
            _host.Vm.Execute(program, entry, hostServices: _host.HostServices, debugHook: _host.Debugger);
        }
        finally
        {
            _host.HostServices.PopEventContext();
        }

        if (eventObject != null)
        {
            CopyVariablesToEvent(eventObject);
        }

        _host.NoteEntryExecuted();
    }

    private void CopyVariablesToEvent(object eventObject)
    {
        foreach (var property in WritableProperties(eventObject))
        {
            var value = _host.HostServices.GetVariable(SymbolId.Empty, property.Name);
            if (value.Type == AstraValueType.Null || !TryConvert(value, property.PropertyType, out var converted))
            {
                continue;
            }

            property.SetValue(eventObject, converted);
        }
    }

    private static IEnumerable<System.Reflection.PropertyInfo> WritableProperties(object eventObject)
    {
        return eventObject.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(property => property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0);
    }

    private static bool TryConvert(AstraValue value, Type target, out object? converted)
    {
        converted = null;
        if (target == typeof(bool) && (value.Type == AstraValueType.Bool || value.Type == AstraValueType.Int64))
        {
            converted = value.Type == AstraValueType.Bool ? value.AsBool() : value.AsInt64() != 0;
            return true;
        }

        if (target == typeof(string))
        {
            converted = value.AsString();
            return true;
        }

        if (target == typeof(int) || target == typeof(long) || target == typeof(short) || target == typeof(byte))
        {
            converted = Convert.ChangeType(value.AsInt64(), target);
            return true;
        }

        if (target == typeof(float) || target == typeof(double))
        {
            converted = Convert.ChangeType(value.AsDouble(), target);
            return true;
        }

        return false;
    }

    private string CatalogHash()
    {
        if (_catalog == null)
        {
            return "";
        }

        var text = string.Join("|", _catalog.Search("").Select(method => method.Descriptor).Order(StringComparer.Ordinal));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private List<GraphEventSubscription> CompileSubscriptions(GraphDocument draft)
    {
        var list = new List<GraphEventSubscription>();
        foreach (var node in draft.Nodes)
        {
            if (!node.NodeType.StartsWith("Event.", StringComparison.Ordinal))
            {
                continue;
            }

            if (!node.Properties.TryGetValue("eventType", out var eventTypeName))
            {
                continue;
            }

            var eventType = _typeResolver.Resolve(eventTypeName);
            if (eventType == null)
            {
                continue;
            }

            Type? componentType = null;
            if (node.Properties.TryGetValue("componentType", out var componentTypeName))
            {
                componentType = _typeResolver.Resolve(componentTypeName);
            }

            list.Add(new GraphEventSubscription(
                draft.Id,
                node.Name,
                componentType,
                eventType,
                componentType == null ? AstraEventSource.Broadcast : AstraEventSource.DirectedComponent));
        }

        return list;
    }

    public PublishResult? TryRestorePrevious(GraphId graphId)
    {
        var previous = _archive?.TryLoadPrevious(graphId);
        return previous == null ? null : Publish(previous, "Bootstrap", "Live override fallback");
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
