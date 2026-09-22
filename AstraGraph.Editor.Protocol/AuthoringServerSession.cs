using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Reflection;
using AstraGraph.Binding;
using AstraGraph.Core;
using AstraGraph.Editor.Core;
using AstraGraph.Editor.Core.Search;
using AstraGraph.HotReload;
using AstraGraph.UI.Compiler;
using AstraGraph.UI.Model;
using AstraGraph.UI.Runtime;
using AstraGraph.Persistence.Audit;
using AstraGraph.Runtime.Debugging;
using AstraGraph.Runtime.Profiling;
using AstraGraph.Runtime.Security;

namespace AstraGraph.Editor.Protocol;

public interface IAuthoringSessionHost
{
    AuthoringServerSession Session { get; }
}

public sealed class AuthoringServerSession : IAuthoringMessageHandler
{
    private sealed record ActiveSession(
        string SessionId,
        AstraUser User,
        DateTime ConnectedAtUtc);

    private readonly Func<string, AstraUser?> _userAuthenticator;
    private readonly HotReloadManager? _hotReloadManager;
    private readonly AuditLogger? _auditLogger;
    private readonly GraphDebugger? _debugger;
    private readonly GraphProfiler? _profiler;
    private readonly BindingCatalog? _bindingCatalog;

    private readonly ConcurrentDictionary<string, ActiveSession> _sessions = new();
    private readonly ConcurrentDictionary<int, Action<AuthoringMessage>> _outbound = new();
    private int _outboundId;
    private readonly ConcurrentDictionary<GraphId, (RevisionId HeadRevision, string DraftJson)> _activeDrafts = new();
    private readonly ConcurrentDictionary<GraphId, GraphSummaryDto> _graphs = new();
    private readonly ConcurrentDictionary<GraphId, string> _liveSources = new();
    private readonly ConcurrentDictionary<RevisionId, string> _revisionSources = new();
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _watches = new();
    private readonly ConcurrentDictionary<SchemaId, SchemaType> _schemas = new();
    private readonly ConcurrentDictionary<SchemaId, string> _schemaKinds = new();
    private readonly ConcurrentDictionary<string, EnumType> _enums = new();
    private readonly ConcurrentDictionary<string, GraphId> _profilerWatch = new();
    private string _lastMigration = "No schema migration";
    private readonly ConcurrentDictionary<GraphId, string> _status = new();
    private readonly ConcurrentDictionary<RevisionId, int> _activation = new();
    private int _authoringTick;
    private readonly ConcurrentDictionary<GraphId, UiDocument> _uiDocuments = new();
    private NodePaletteIndexer? _palette;

    public AuthoringServerSession(
        Func<string, AstraUser?> userAuthenticator,
        HotReloadManager? hotReloadManager = null,
        AuditLogger? auditLogger = null,
        GraphDebugger? debugger = null,
        GraphProfiler? profiler = null,
        BindingCatalog? bindingCatalog = null)
    {
        _userAuthenticator = userAuthenticator ?? throw new ArgumentNullException(nameof(userAuthenticator));
        _hotReloadManager = hotReloadManager;
        _auditLogger = auditLogger;
        _debugger = debugger;
        _profiler = profiler;
        _bindingCatalog = bindingCatalog;
    }

    public void RegisterGraph(GraphSummaryDto summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        _graphs[summary.Id] = summary;
    }

    public AuthHandshakeResponse HandleHandshake(AuthHandshakeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.AuthorToken))
        {
            return new AuthHandshakeResponse(AuthoringStatusCode.Unauthorized, string.Empty, AstraPermission.None, "Missing author token.");
        }

        var user = _userAuthenticator(request.AuthorToken);
        if (user == null)
        {
            return new AuthHandshakeResponse(AuthoringStatusCode.Unauthorized, string.Empty, AstraPermission.None, "Invalid or expired author token.");
        }

        var sessionId = Guid.NewGuid().ToString("N");
        _sessions[sessionId] = new ActiveSession(sessionId, user, DateTime.UtcNow);

        return new AuthHandshakeResponse(AuthoringStatusCode.Success, sessionId, user.Permissions);
    }

    public void Publish(AuthoringMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        PublishOutbound(message);
    }

    public IDisposable SubscribeOutbound(Action<AuthoringMessage> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        var id = Interlocked.Increment(ref _outboundId);
        _outbound[id] = listener;
        return new OutboundSubscription(() => _outbound.TryRemove(id, out _));
    }

    public void ReplaceUser(string userId, AstraUser? updated)
    {
        foreach (var pair in _sessions.ToArray())
        {
            if (pair.Value.User.Id != userId)
            {
                continue;
            }

            if (updated == null)
            {
                _sessions.TryRemove(pair.Key, out _);
            }
            else
            {
                _sessions[pair.Key] = pair.Value with { User = updated };
            }

            PublishOutbound(new SessionUpdatedMsg
            {
                SessionId = pair.Key,
                Permissions = updated?.Permissions ?? AstraPermission.None
            });
        }
    }

    private void PublishOutbound(AuthoringMessage message)
    {
        foreach (var listener in _outbound.Values)
        {
            listener(message);
        }
    }

    private sealed class OutboundSubscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    private GraphSummaryDto DescribeGraph(GraphSummaryDto graph)
    {
        var owner = "";
        var tags = "";
        var overrideOf = "";
        if (_liveSources.TryGetValue(graph.Id, out var json))
        {
            try
            {
                var document = GraphSerializer.Deserialize(json);
                owner = document.Metadata.Author;
                tags = string.Join(", ", document.Metadata.Tags);
                document.Metadata.CustomAttributes.TryGetValue("overrideOf", out overrideOf!);
            }
            catch (System.Text.Json.JsonException)
            {
                owner = "";
            }
        }

        return graph with
        {
            Status = _status.TryGetValue(graph.Id, out var status) ? status : "Live",
            HasDraft = _activeDrafts.ContainsKey(graph.Id),
            Owner = owner,
            Tags = tags,
            OverrideOf = overrideOf ?? ""
        };
    }

    public GraphListResponse HandleGraphList(GraphListRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_sessions.TryGetValue(request.SessionId, out _))
        {
            return new GraphListResponse(AuthoringStatusCode.Unauthorized, [], "Invalid session.");
        }

        var graphs = _graphs.Values
            .Select(DescribeGraph)
            .ToList();
        return new GraphListResponse(AuthoringStatusCode.Success, graphs, ClientCount: _sessions.Count, MigrationSummary: _lastMigration);
    }

    public DraftSaveResponse HandleDraftSave(DraftSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_sessions.TryGetValue(request.SessionId, out var session))
        {
            return new DraftSaveResponse(AuthoringStatusCode.Unauthorized, null, false, null, "Invalid session.");
        }

        if (!AstraAuthorizationService.CanEditDraft(session.User))
        {
            return new DraftSaveResponse(AuthoringStatusCode.Unauthorized, null, false, null, "Insufficient permissions to edit drafts.");
        }

        // Optimistic concurrency check
        if (_graphs.TryGetValue(request.GraphId, out var summary))
        {
            if (request.BaseRevisionId != summary.ActiveRevision)
            {
                return new DraftSaveResponse(
                    AuthoringStatusCode.Conflict,
                    null,
                    true,
                    $"Base revision mismatch. Expected head revision '{summary.ActiveRevision}', got '{request.BaseRevisionId}'.",
                    "Conflict detected: another author has published a new revision.");
            }
        }

        var draftRevision = RevisionId.New();
        _activeDrafts[request.GraphId] = (request.BaseRevisionId, request.DraftJson);

        return new DraftSaveResponse(AuthoringStatusCode.Success, draftRevision, false);
    }

    public GraphFetchResponse DiscardDraft(string sessionId, GraphId graphId)
    {
        if (!_sessions.ContainsKey(sessionId))
        {
            return new GraphFetchResponse(AuthoringStatusCode.Unauthorized, string.Empty, RevisionId.Empty, RevisionId.Empty, false, "Invalid session.");
        }

        _activeDrafts.TryRemove(graphId, out _);
        return HandleGraphFetch(new GraphFetchRequest(sessionId, graphId));
    }

    public DraftCompileResponse HandleDraftCompile(DraftCompileRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_sessions.TryGetValue(request.SessionId, out var session))
        {
            return new DraftCompileResponse(AuthoringStatusCode.Unauthorized, true, [], null, "Invalid session.");
        }

        if (!AstraAuthorizationService.CanCompile(session.User))
        {
            return new DraftCompileResponse(AuthoringStatusCode.Unauthorized, true, [], null, "Insufficient permissions to compile.");
        }

        try
        {
            var graphDoc = GraphSerializer.Deserialize(request.DraftJson);
            var analyzer = new SemanticAnalyzer();
            var analysis = analyzer.Analyze(graphDoc);

            if (analysis.Diagnostics.HasErrors)
            {
                return FinishCompile(request.GraphId, new DraftCompileResponse(AuthoringStatusCode.ValidationError, true, analysis.Diagnostics, Stage: "Type Check"));
            }

            var irProgram = AstToIrCompiler.Compile(analysis.Program!);
            var verification = IrVerifier.Verify(irProgram);

            if (verification.HasErrors)
            {
                return FinishCompile(request.GraphId, new DraftCompileResponse(AuthoringStatusCode.ValidationError, true, verification, Stage: "Verify"));
            }

            var bytecode = IrToBytecodeCompiler.Compile(irProgram);
            return FinishCompile(request.GraphId, new DraftCompileResponse(AuthoringStatusCode.Success, false, analysis.Diagnostics, bytecode.SemanticHash, Stage: "Verify"));
        }
        catch (Exception ex)
        {
            return FinishCompile(request.GraphId, new DraftCompileResponse(AuthoringStatusCode.InternalError, true, [], null, $"Compilation exception: {ex.Message}", "Parse"));
        }
    }

    private DraftCompileResponse FinishCompile(GraphId graphId, DraftCompileResponse response)
    {
        if (graphId == GraphId.Empty || !_graphs.ContainsKey(graphId)) return response;
        if (_status.TryGetValue(graphId, out var status) && status == "Disabled") return response;
        if (response.HasErrors) _status[graphId] = "Failed";
        else _status.TryRemove(graphId, out _);
        return response;
    }

    public Task<DraftPublishResponse> HandleDraftPublishAsync(DraftPublishRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_sessions.TryGetValue(request.SessionId, out var session))
        {
            return Task.FromResult(new DraftPublishResponse(AuthoringStatusCode.Unauthorized, null, [], "Invalid session."));
        }

        GraphDocument graphDoc;
        try
        {
            graphDoc = GraphSerializer.Deserialize(request.DraftJson);
        }
        catch (Exception ex)
        {
            return Task.FromResult(new DraftPublishResponse(AuthoringStatusCode.ValidationError, null, [], $"Malformed JSON: {ex.Message}"));
        }

        var targetProfile = SecurityProfile.Gameplay;
        if (!AstraAuthorizationService.CanPublish(session.User, graphDoc.Side, targetProfile))
        {
            return Task.FromResult(new DraftPublishResponse(AuthoringStatusCode.Unauthorized, null, [], $"Insufficient permissions to publish to side '{graphDoc.Side}'."));
        }

        // Concurrency check
        if (_graphs.TryGetValue(request.GraphId, out var summary) && request.BaseRevisionId != summary.ActiveRevision)
        {
            return Task.FromResult(new DraftPublishResponse(AuthoringStatusCode.Conflict, null, [], $"Conflict: head revision is '{summary.ActiveRevision}', requested base was '{request.BaseRevisionId}'."));
        }

        var compileResult = HandleDraftCompile(new DraftCompileRequest(request.SessionId, request.GraphId, request.DraftJson));
        if (compileResult.HasErrors)
        {
            return Task.FromResult(new DraftPublishResponse(AuthoringStatusCode.ValidationError, null, compileResult.Diagnostics, "Compilation failed before publish."));
        }

        var newRevision = RevisionId.New();

        if (_hotReloadManager != null)
        {
            var publishResult = _hotReloadManager.Publish(graphDoc, session.User.Name, request.PublishMessage);

            if (!publishResult.Success)
            {
                return Task.FromResult(new DraftPublishResponse(AuthoringStatusCode.InternalError, null, [], "Hot reload transaction rejected by server."));
            }
            newRevision = publishResult.PublishedRevisionId ?? newRevision;
        }

        // Update local summary
        _graphs[request.GraphId] = new GraphSummaryDto(
            request.GraphId,
            graphDoc.Name,
            graphDoc.Kind,
            graphDoc.Side,
            newRevision,
            (_graphs.TryGetValue(request.GraphId, out var prev) ? prev.RevisionCount : 0) + 1);
        _revisionSources[newRevision] = request.DraftJson;
        _liveSources[request.GraphId] = request.DraftJson;
        if (_activeDrafts.TryGetValue(request.GraphId, out var pending) && pending.DraftJson == request.DraftJson)
        {
            _activeDrafts.TryRemove(request.GraphId, out _);
        }
        _status.TryRemove(request.GraphId, out _);

        if (_auditLogger != null)
        {
            _auditLogger.Append(new AuditRecord(
                DateTimeOffset.UtcNow,
                "Publish",
                request.GraphId.Value,
                newRevision.Value,
                session.User.Name,
                request.PublishMessage,
                compileResult.BytecodeHash,
                true));
        }

        var activationTick = 0;
        if (graphDoc.Side is GraphSide.Shared or GraphSide.SharedPredicted)
        {
            var hostTick = _hotReloadManager?.Host.CurrentTick ?? 0;
            var counter = hostTick > 0 ? hostTick : System.Threading.Interlocked.Increment(ref _authoringTick);
            activationTick = counter + 4;
            _activation[newRevision] = activationTick;
        }

        return Task.FromResult(new DraftPublishResponse(
            AuthoringStatusCode.Success,
            newRevision,
            [],
            ActivationTick: activationTick,
            MigrationSummary: _lastMigration,
            ClientCount: _sessions.Count));
    }

    public Task<RollbackResponse> HandleRollbackAsync(RollbackRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_sessions.TryGetValue(request.SessionId, out var session))
        {
            return Task.FromResult(new RollbackResponse(AuthoringStatusCode.Unauthorized, RevisionId.New(), "Invalid session."));
        }

        if (!AstraAuthorizationService.CanRollback(session.User))
        {
            return Task.FromResult(new RollbackResponse(AuthoringStatusCode.Unauthorized, RevisionId.New(), "Insufficient permissions to rollback."));
        }

        var active = request.TargetRevision;
        if (_hotReloadManager != null)
        {
            var rollbackResult = _hotReloadManager.Rollback(request.GraphId, request.TargetRevision);
            if (!rollbackResult.Success)
            {
                var current = _graphs.TryGetValue(request.GraphId, out var head) ? head.ActiveRevision : RevisionId.Empty;
                return Task.FromResult(new RollbackResponse(AuthoringStatusCode.InternalError, current, rollbackResult.ErrorMessage ?? "Rollback failed on server."));
            }

            active = rollbackResult.PublishedRevisionId ?? request.TargetRevision;
            if (_revisionSources.TryGetValue(request.TargetRevision, out var source))
            {
                _revisionSources[active] = source;
                _liveSources[request.GraphId] = source;
            }

            _activeDrafts.TryRemove(request.GraphId, out _);
        }

        if (_graphs.TryGetValue(request.GraphId, out var existing))
        {
            _graphs[request.GraphId] = existing with { ActiveRevision = active };
        }

        return Task.FromResult(new RollbackResponse(AuthoringStatusCode.Success, active, null, request.TargetRevision));
    }

    public Task<RollbackResponse> RestoreLastKnownGoodAsync(string sessionId, GraphId graphId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return Task.FromResult(new RollbackResponse(AuthoringStatusCode.Unauthorized, RevisionId.Empty, "Invalid session."));
        }

        if (!AstraAuthorizationService.CanRollback(session.User))
        {
            return Task.FromResult(new RollbackResponse(AuthoringStatusCode.Unauthorized, RevisionId.Empty, "Insufficient permissions to rollback."));
        }

        var history = _hotReloadManager?.GetRevisionHistory(graphId) ?? [];
        if (history.Count < 2)
        {
            var current = _graphs.TryGetValue(graphId, out var head) ? head.ActiveRevision : RevisionId.Empty;
            return Task.FromResult(new RollbackResponse(AuthoringStatusCode.ValidationError, current, "No last known good revision."));
        }

        return HandleRollbackAsync(new RollbackRequest(sessionId, graphId, history[^2].RevisionId));
    }

    public GraphCreateResponse HandleGraphCreate(GraphCreateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_sessions.TryGetValue(request.SessionId, out var session))
        {
            return new GraphCreateResponse(AuthoringStatusCode.Unauthorized, null, string.Empty, "Invalid session.");
        }

        if (!AstraAuthorizationService.CanEditDraft(session.User))
        {
            return new GraphCreateResponse(AuthoringStatusCode.Unauthorized, null, string.Empty, "Insufficient permissions to create a graph.");
        }

        var name = string.IsNullOrWhiteSpace(request.Name) ? "New graph" : request.Name.Trim();
        var document = new GraphDocument
        {
            Id = GraphId.New(),
            Name = name,
            Kind = request.Kind,
            Side = request.Side
        };
        var source = GraphSerializer.Serialize(document, writeIndented: false);
        var summary = new GraphSummaryDto(document.Id, name, request.Kind, request.Side, RevisionId.Empty, 0);
        _graphs[document.Id] = summary;
        _liveSources[document.Id] = source;
        return new GraphCreateResponse(AuthoringStatusCode.Success, summary, source);
    }

    public GraphMutationResponse HandleGraphRename(GraphRenameRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_sessions.TryGetValue(request.SessionId, out var session))
        {
            return new GraphMutationResponse(AuthoringStatusCode.Unauthorized, null, "Invalid session.");
        }

        if (!AstraAuthorizationService.CanEditDraft(session.User))
        {
            return new GraphMutationResponse(AuthoringStatusCode.Unauthorized, null, "Insufficient permissions to rename a graph.");
        }

        if (!_graphs.TryGetValue(request.GraphId, out var summary))
        {
            return new GraphMutationResponse(AuthoringStatusCode.NotFound, null, "Graph was not found.");
        }

        var name = request.Name.Trim();
        if (name.Length == 0)
        {
            return new GraphMutationResponse(AuthoringStatusCode.ValidationError, summary, "Graph name is required.");
        }

        var renamed = summary with { Name = name };
        _graphs[request.GraphId] = renamed;
        if (_liveSources.TryGetValue(request.GraphId, out var live))
        {
            _liveSources[request.GraphId] = RenameSource(live, name);
        }

        if (_activeDrafts.TryGetValue(request.GraphId, out var draft))
        {
            _activeDrafts[request.GraphId] = (draft.HeadRevision, RenameSource(draft.DraftJson, name));
        }

        return new GraphMutationResponse(AuthoringStatusCode.Success, renamed);
    }

    public GraphMutationResponse HandleGraphDelete(GraphDeleteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_sessions.TryGetValue(request.SessionId, out var session))
        {
            return new GraphMutationResponse(AuthoringStatusCode.Unauthorized, null, "Invalid session.");
        }

        if (!AstraAuthorizationService.CanEditDraft(session.User))
        {
            return new GraphMutationResponse(AuthoringStatusCode.Unauthorized, null, "Insufficient permissions to delete a graph.");
        }

        if (!_graphs.TryRemove(request.GraphId, out var summary))
        {
            return new GraphMutationResponse(AuthoringStatusCode.NotFound, null, "Graph was not found.");
        }

        _activeDrafts.TryRemove(request.GraphId, out _);
        _liveSources.TryRemove(request.GraphId, out _);
        _status.TryRemove(request.GraphId, out _);
        _hotReloadManager?.Deactivate(request.GraphId);
        return new GraphMutationResponse(AuthoringStatusCode.Success, summary);
    }

    public GraphMutationResponse HandleGraphDisable(GraphId graphId, string sessionId, bool disabled)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return new GraphMutationResponse(AuthoringStatusCode.Unauthorized, null, "Invalid session.");
        }

        if (!AstraAuthorizationService.CanEditDraft(session.User))
        {
            return new GraphMutationResponse(AuthoringStatusCode.Unauthorized, null, "Insufficient permissions to disable a graph.");
        }

        if (!_graphs.TryGetValue(graphId, out var summary))
        {
            return new GraphMutationResponse(AuthoringStatusCode.NotFound, null, "Graph was not found.");
        }

        if (disabled)
        {
            _status[graphId] = "Disabled";
            _hotReloadManager?.Deactivate(graphId);
        }
        else
        {
            _status.TryRemove(graphId, out _);
        }

        var updated = summary with { Status = disabled ? "Disabled" : "Live" };
        _graphs[graphId] = updated;
        return new GraphMutationResponse(AuthoringStatusCode.Success, updated);
    }

    public GraphFetchResponse HandleGraphFetch(GraphFetchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_sessions.TryGetValue(request.SessionId, out _))
        {
            return new GraphFetchResponse(AuthoringStatusCode.Unauthorized, string.Empty, RevisionId.Empty, RevisionId.Empty, false, "Invalid session.");
        }

        var active = _graphs.TryGetValue(request.GraphId, out var summary) ? summary.ActiveRevision : RevisionId.Empty;
        if (_activeDrafts.TryGetValue(request.GraphId, out var draft))
        {
            return new GraphFetchResponse(AuthoringStatusCode.Success, draft.DraftJson, active, active, true);
        }

        if (_liveSources.TryGetValue(request.GraphId, out var live))
        {
            return new GraphFetchResponse(AuthoringStatusCode.Success, live, active, active, false);
        }

        return new GraphFetchResponse(AuthoringStatusCode.NotFound, string.Empty, active, active, false, "No live source or draft.");
    }

    private static string RenameSource(string json, string name)
    {
        var node = JsonNode.Parse(json) as JsonObject;
        if (node == null)
        {
            return json;
        }

        node["name"] = name;
        return node.ToJsonString();
    }

    public CatalogQueryResponse HandleCatalogQuery(CatalogQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_sessions.TryGetValue(request.SessionId, out _))
        {
            return new CatalogQueryResponse(AuthoringStatusCode.Unauthorized, [], "Invalid session.");
        }

        var list = new List<CatalogEntryDto>();
        if (_bindingCatalog != null)
        {
            var searchResults = string.IsNullOrEmpty(request.SearchFilter)
                ? _bindingCatalog.Search(string.Empty)
                : _bindingCatalog.Search(request.SearchFilter);

            foreach (var desc in searchResults)
            {
                if (request.TargetSide.HasValue && desc.Side != request.TargetSide.Value && desc.Side != GraphSide.Shared)
                {
                    continue;
                }

                if (!IsBrowserBinding(desc.Name))
                {
                    continue;
                }

                list.Add(ToCatalogEntry(desc));
            }
        }

        return new CatalogQueryResponse(AuthoringStatusCode.Success, list);
    }

    private static CatalogEntryDto ToCatalogEntry(NativeMethodDescriptor desc)
    {
        var node = BindingNodeFactory.Create(desc);
        return new CatalogEntryDto(
            Signature: desc.Descriptor,
            Category: desc.DeclaringTypeName,
            IsPure: desc.IsPure || desc.Name.StartsWith("op_", StringComparison.Ordinal),
            IsPredictionSafe: desc.IsDeterministic,
            Side: desc.Side,
            Documentation: desc.Name,
            BindingId: desc.Descriptor,
            DeclaringType: desc.DeclaringTypeName,
            MethodName: desc.Name,
            Parameters: node.Pins
                .Where(pin => pin.Kind == PinKind.Data)
                .Select(pin => new CatalogParameterDto(pin.Name, pin.DataType, pin.Direction.ToString()))
                .ToArray(),
            ReturnType: desc.ReturnType.TypeName,
            SecurityProfile: desc.RequiredProfile.ToString(),
            Cost: desc.Cost,
            IsObsolete: desc.Method.GetCustomAttribute<ObsoleteAttribute>() != null);
    }

    public string? MaterializeBinding(string bindingId)
    {
        var method = _bindingCatalog?.FindMethod(bindingId);
        if (method == null)
        {
            return null;
        }

        var shell = new GraphDocument { Name = method.Name, Nodes = [BindingNodeFactory.Create(method)] };
        return GraphSerializer.Serialize(shell, writeIndented: false);
    }

    public static IReadOnlyList<PinCompatibilityDto> CompatiblePins(PinWire source, IReadOnlyList<PinWire> candidates, IReadOnlyList<WireEnds> existing)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        return candidates
            .Select(candidate =>
            {
                try
                {
                    var result = ValidateWire(source, candidate, existing);
                    return new PinCompatibilityDto(candidate.Id, result.IsValid, result.ErrorReason);
                }
                catch (Exception ex) when (ex is FormatException or ArgumentException)
                {
                    return new PinCompatibilityDto(candidate.Id, false, "Pin identity is not a guid.");
                }
            })
            .ToArray();
    }

    public IReadOnlyList<ConnectionSuggestionDto> SuggestConnections(PinWire pin, string? query)
    {
        var suggestions = ContextCompletionEngine.SuggestForPin(ToVisual(pin), Palette(), query);
        return suggestions
            .Select(item =>
            {
                item.Item.DefaultProperties.TryGetValue("MethodDescriptor", out var bindingId);
                return new ConnectionSuggestionDto(
                    bindingId ?? string.Empty,
                    item.Item.NodeType,
                    item.Item.DisplayName,
                    item.TargetPinToConnect.Name,
                    item.Score);
            })
            .Where(item => string.IsNullOrEmpty(item.BindingId) || IsBrowserBinding(BindingName(item.BindingId)))
            .Take(24)
            .ToArray();
    }

    private NodePaletteIndexer Palette()
    {
        if (_palette != null)
        {
            return _palette;
        }

        var indexer = new NodePaletteIndexer();
        if (_bindingCatalog != null)
        {
            indexer.IndexBindingCatalog(_bindingCatalog);
        }

        _palette = indexer;
        return indexer;
    }

    private string BindingName(string bindingId) => _bindingCatalog?.FindMethod(bindingId)?.Name ?? bindingId;

    public static ConnectionValidationResult ValidateWire(PinWire source, PinWire target, IReadOnlyList<WireEnds> existing)
    {
        var pins = new[] { ToVisual(source), ToVisual(target) };
        var connections = existing.Select(wire => new VisualConnection(PinId.FromString(wire.SourcePinId), PinId.FromString(wire.TargetPinId))).ToArray();
        return PinConnectionValidator.Validate(pins[0], pins[1], connections);
    }

    private static VisualPin ToVisual(PinWire pin) => new(
        PinId.FromString(pin.Id),
        NodeId.FromString(pin.NodeId),
        pin.Name,
        Enum.Parse<PinDirection>(pin.Direction, ignoreCase: true),
        Enum.Parse<PinKind>(pin.Kind, ignoreCase: true),
        pin.DataType);

    internal static bool IsBrowserBinding(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('<') || name.Contains('>'))
        {
            return false;
        }

        return name is not ("Equals" or "GetHashCode" or "ToString" or "Deconstruct" or "PrintMembers" or "GetType" or "MemberwiseClone");
    }

    public DebuggerCommandResponse HandleDebuggerCommand(DebuggerCommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_sessions.TryGetValue(request.SessionId, out var session))
        {
            return new DebuggerCommandResponse(AuthoringStatusCode.Unauthorized, false, "Invalid session.");
        }

        if (!AstraAuthorizationService.CanDebug(session.User))
        {
            return new DebuggerCommandResponse(AuthoringStatusCode.Unauthorized, false, "Insufficient permissions for debugging.");
        }

        if (_debugger == null)
        {
            return new DebuggerCommandResponse(AuthoringStatusCode.InternalError, false, "Debugger is not active on host.");
        }

        switch (request.Action)
        {
            case DebuggerAction.SetBreakpoint when request.TargetNode.HasValue:
                if (!TryBreakpointCondition(request.Condition, out var condition, out var conditionError))
                {
                    return new DebuggerCommandResponse(AuthoringStatusCode.ValidationError, false, conditionError);
                }

                _debugger.SetBreakpoint(new Breakpoint(request.TargetNode.Value, BreakpointMode.GraphPause, condition));
                break;
            case DebuggerAction.RemoveBreakpoint when request.TargetNode.HasValue:
                _debugger.RemoveBreakpoint(request.TargetNode.Value);
                break;
            case DebuggerAction.Pause:
                _debugger.Pause();
                break;
            case DebuggerAction.Resume:
                _debugger.Resume();
                break;
            case DebuggerAction.StepInto:
                _debugger.StepInto();
                break;
            case DebuggerAction.StepOver:
                _debugger.StepOver();
                break;
            case DebuggerAction.StepOut:
                _debugger.StepOut();
                break;
        }

        return new DebuggerCommandResponse(AuthoringStatusCode.Success, true);
    }

    public async Task<AuthoringMessage?> HandleAsync(AuthoringMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message switch
        {
            AuthHandshakeRequestMsg req =>
                HandleHandshakeMsg(req),

            GraphListRequestMsg req =>
                HandleGraphListMsg(req),

            DraftSaveRequestMsg req =>
                HandleDraftSaveMsg(req),

            DraftDiscardRequestMsg req =>
                HandleDraftDiscardMsg(req),

            UiSaveRequestMsg req =>
                HandleUiSaveMsg(req),

            UiListRequestMsg req =>
                HandleUiListMsg(req),

            GraphFetchRequestMsg req =>
                HandleGraphFetchMsg(req),

            GraphCreateRequestMsg req =>
                HandleGraphCreateMsg(req),

            GraphRenameRequestMsg req =>
                HandleGraphRenameMsg(req),

            GraphDeleteRequestMsg req =>
                HandleGraphDeleteMsg(req),

            GraphDisableRequestMsg req =>
                HandleGraphDisableMsg(req),

            CatalogQueryRequestMsg req =>
                HandleCatalogQueryMsg(req),

            BindingMaterializeRequestMsg req =>
                HandleBindingMaterializeMsg(req),

            ConnectionValidateRequestMsg req =>
                HandleConnectionValidateMsg(req),

            ConnectionCompatibleRequestMsg req =>
                HandleConnectionCompatibleMsg(req),

            ConnectionSuggestRequestMsg req =>
                HandleConnectionSuggestMsg(req),

            HistoryListRequestMsg req =>
                HandleHistoryListMsg(req),

            HistoryDiffRequestMsg req =>
                HandleHistoryDiffMsg(req),

            HistoryRollbackRequestMsg req =>
                await HandleHistoryRollbackMsgAsync(req),

            HistoryLkgRequestMsg req =>
                await HandleHistoryLkgMsgAsync(req),

            UiCompileRequestMsg req =>
                CompileUi(req.SessionId, req.Document),

            DraftRebaseRequestMsg req =>
                RebaseDraft(req.SessionId, req.GraphId),

            DraftMergeRequestMsg req =>
                MergeDraft(req.SessionId, req.GraphId, req.OtherDraftJson),

            GraphTestRequestMsg req =>
                RunGraphTest(req.SessionId, req.DraftJson),

            ProfilerSubscribeRequestMsg req =>
                SubscribeProfiler(req.SessionId, req.GraphId, req.Unsubscribe),

            DraftCompileRequestMsg req =>
                HandleDraftCompileMsg(req),

            DraftPublishRequestMsg req =>
                await HandleDraftPublishMsgAsync(req),

            DebuggerCommandRequestMsg req =>
                HandleDebuggerCommandMsg(req),

            DebuggerInspectRequestMsg req =>
                HandleDebuggerInspectMsg(req),

            DebuggerWatchRequestMsg req =>
                HandleDebuggerWatchMsg(req),

            ProfilerSnapshotRequestMsg req =>
                HandleProfilerSnapshotMsg(req),

            AuditQueryRequestMsg req =>
                HandleAuditQueryMsg(req),

            SchemaListRequestMsg req =>
                HandleSchemaListMsg(req),

            SchemaSaveRequestMsg req =>
                HandleSchemaSaveMsg(req),

            SandboxRunRequestMsg req =>
                HandleSandboxRunMsg(req),

            RuntimeStatusRequestMsg req =>
                HandleRuntimeStatusMsg(req),

            PingMsg =>
                new PongMsg { MessageId = Guid.NewGuid().ToString("N") },

            _ => new ErrorMsg { Code = "unsupported_message", Detail = $"Message kind '{message.Kind}' is not supported." }
        };
    }

    private AuthHandshakeResponseMsg HandleHandshakeMsg(AuthHandshakeRequestMsg req)
    {
        if (req.ProtocolVersion != 0 && req.ProtocolVersion != AuthoringProtocol.Version)
        {
            return new AuthHandshakeResponseMsg
            {
                Status = AuthoringStatusCode.IncompatibleVersion,
                ProtocolVersion = AuthoringProtocol.Version,
                ErrorMessage = $"Astra Studio protocol {req.ProtocolVersion} is not supported. This server speaks protocol {AuthoringProtocol.Version}."
            };
        }

        var resp = HandleHandshake(new AuthHandshakeRequest(req.ClientVersion, req.AuthorToken, req.AuthorName));
        return new AuthHandshakeResponseMsg
        {
            Status = resp.Status,
            SessionId = resp.SessionId,
            Permissions = resp.Permissions,
            ErrorMessage = resp.ErrorMessage,
            ProtocolVersion = AuthoringProtocol.Version,
            CanCompile = resp.Permissions.HasFlag(AstraPermission.Compile),
            CanPublish = resp.Permissions.HasFlag(AstraPermission.PublishServer) || resp.Permissions.HasFlag(AstraPermission.PublishShared),
            CanDebug = resp.Permissions.HasFlag(AstraPermission.Debug) && _debugger != null,
            CanProfile = resp.Permissions.HasFlag(AstraPermission.Debug) && _profiler != null,
            HasBindingCatalog = _bindingCatalog != null,
            HasNativeEngine = false,
            HasPrediction = false,
            HasBui = false
        };
    }

    private GraphListResponseMsg HandleGraphListMsg(GraphListRequestMsg req)
    {
        var resp = HandleGraphList(new GraphListRequest(req.SessionId));
        return new GraphListResponseMsg
        {
            Status = resp.Status,
            Graphs = resp.Graphs,
            ErrorMessage = resp.ErrorMessage,
            ClientCount = resp.ClientCount,
            MigrationSummary = resp.MigrationSummary
        };
    }

    private DraftSaveResponseMsg HandleDraftSaveMsg(DraftSaveRequestMsg req)
    {
        var resp = HandleDraftSave(new DraftSaveRequest(req.SessionId, req.GraphId, req.BaseRevisionId, req.DraftJson, req.AuthorMessage));
        return new DraftSaveResponseMsg
        {
            Status = resp.Status,
            DraftRevisionId = resp.DraftRevisionId,
            HasConflict = resp.HasConflict,
            ErrorMessage = resp.ErrorMessage
        };
    }

    private GraphFetchResponseMsg HandleGraphFetchMsg(GraphFetchRequestMsg req)
    {
        var resp = HandleGraphFetch(new GraphFetchRequest(req.SessionId, req.GraphId));
        return new GraphFetchResponseMsg
        {
            Status = resp.Status,
            DraftJson = resp.DraftJson,
            BaseRevisionId = resp.BaseRevisionId,
            ActiveRevisionId = resp.ActiveRevisionId,
            FromDraft = resp.FromDraft,
            ErrorMessage = resp.ErrorMessage
        };
    }

    private GraphCreateResponseMsg HandleGraphCreateMsg(GraphCreateRequestMsg req)
    {
        var resp = HandleGraphCreate(new GraphCreateRequest(req.SessionId, req.Name, req.DocumentKind, req.Side));
        return new GraphCreateResponseMsg
        {
            Status = resp.Status,
            Graph = resp.Graph,
            SourceJson = resp.SourceJson,
            ErrorMessage = resp.ErrorMessage
        };
    }

    private GraphRenameResponseMsg HandleGraphRenameMsg(GraphRenameRequestMsg req)
    {
        var resp = HandleGraphRename(new GraphRenameRequest(req.SessionId, req.GraphId, req.Name));
        return new GraphRenameResponseMsg
        {
            Status = resp.Status,
            Graph = resp.Graph,
            ErrorMessage = resp.ErrorMessage
        };
    }

    private GraphDeleteResponseMsg HandleGraphDeleteMsg(GraphDeleteRequestMsg req)
    {
        var resp = HandleGraphDelete(new GraphDeleteRequest(req.SessionId, req.GraphId));
        return new GraphDeleteResponseMsg
        {
            Status = resp.Status,
            ErrorMessage = resp.ErrorMessage
        };
    }

    private GraphMutationResponseMsg HandleGraphDisableMsg(GraphDisableRequestMsg req)
    {
        var resp = HandleGraphDisable(req.GraphId, req.SessionId, req.Disabled);
        return new GraphMutationResponseMsg
        {
            Status = resp.Status,
            Graph = resp.Graph,
            ErrorMessage = resp.ErrorMessage
        };
    }

    private BindingMaterializeResponseMsg HandleBindingMaterializeMsg(BindingMaterializeRequestMsg req)
    {
        if (!_sessions.ContainsKey(req.SessionId))
        {
            return new BindingMaterializeResponseMsg { Status = AuthoringStatusCode.Unauthorized, ErrorMessage = "Invalid session." };
        }

        var json = MaterializeBinding(req.BindingId);
        return json == null
            ? new BindingMaterializeResponseMsg { Status = AuthoringStatusCode.NotFound, ErrorMessage = "Binding was not found." }
            : new BindingMaterializeResponseMsg { Status = AuthoringStatusCode.Success, NodeJson = json };
    }

    private ConnectionValidateResponseMsg HandleConnectionValidateMsg(ConnectionValidateRequestMsg req)
    {
        if (!_sessions.ContainsKey(req.SessionId))
        {
            return new ConnectionValidateResponseMsg { Status = AuthoringStatusCode.Unauthorized, Valid = false, Reason = "Invalid session." };
        }

        if (req.Source == null || req.Target == null)
        {
            return new ConnectionValidateResponseMsg { Status = AuthoringStatusCode.ValidationError, Valid = false, Reason = "Both pins are required." };
        }

        try
        {
            var result = ValidateWire(req.Source, req.Target, req.Connections);
            return new ConnectionValidateResponseMsg { Status = AuthoringStatusCode.Success, Valid = result.IsValid, Reason = result.ErrorReason };
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return new ConnectionValidateResponseMsg { Status = AuthoringStatusCode.ValidationError, Valid = false, Reason = "Pin identity is not a guid." };
        }
    }

    private ConnectionCompatibleResponseMsg HandleConnectionCompatibleMsg(ConnectionCompatibleRequestMsg req)
    {
        if (!_sessions.ContainsKey(req.SessionId))
        {
            return new ConnectionCompatibleResponseMsg { Status = AuthoringStatusCode.Unauthorized, Reason = "Invalid session." };
        }

        if (req.Source == null)
        {
            return new ConnectionCompatibleResponseMsg { Status = AuthoringStatusCode.ValidationError, Reason = "A source pin is required." };
        }

        return new ConnectionCompatibleResponseMsg
        {
            Status = AuthoringStatusCode.Success,
            Pins = CompatiblePins(req.Source, req.Candidates, req.Connections)
        };
    }

    private ConnectionSuggestResponseMsg HandleConnectionSuggestMsg(ConnectionSuggestRequestMsg req)
    {
        if (!_sessions.ContainsKey(req.SessionId))
        {
            return new ConnectionSuggestResponseMsg { Status = AuthoringStatusCode.Unauthorized, Reason = "Invalid session." };
        }

        if (req.Pin == null)
        {
            return new ConnectionSuggestResponseMsg { Status = AuthoringStatusCode.ValidationError, Reason = "A pin is required." };
        }

        try
        {
            return new ConnectionSuggestResponseMsg
            {
                Status = AuthoringStatusCode.Success,
                Suggestions = SuggestConnections(req.Pin, req.Query)
            };
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return new ConnectionSuggestResponseMsg { Status = AuthoringStatusCode.ValidationError, Reason = "Pin identity is not a guid." };
        }
    }

    private CatalogQueryResponseMsg HandleCatalogQueryMsg(CatalogQueryRequestMsg req)
    {
        var resp = HandleCatalogQuery(new CatalogQueryRequest(req.SessionId, req.SearchFilter));
        return new CatalogQueryResponseMsg
        {
            Status = resp.Status,
            Entries = resp.Entries,
            ErrorMessage = resp.ErrorMessage
        };
    }

    public IReadOnlyList<SemanticChangeDto> DiffDraft(GraphId graphId, RevisionId revisionId, string draftJson)
    {
        if (!_revisionSources.TryGetValue(revisionId, out var published) && !_liveSources.TryGetValue(graphId, out published))
        {
            return [];
        }

        var diff = SemanticDiffEngine.Diff(GraphSerializer.Deserialize(published), GraphSerializer.Deserialize(draftJson));
        var changes = new List<SemanticChangeDto>();
        changes.AddRange(diff.NodesAdded.Select(node => new SemanticChangeDto("node.added", node.Name)));
        changes.AddRange(diff.NodesRemoved.Select(node => new SemanticChangeDto("node.removed", node.Name)));
        changes.AddRange(diff.NodesModified.Select(pair => new SemanticChangeDto("node.modified", pair.New.Name)));
        changes.AddRange(diff.ConnectionsAdded.Select(wire => new SemanticChangeDto("connection.added", wire.ToPin.ToString())));
        changes.AddRange(diff.ConnectionsRemoved.Select(wire => new SemanticChangeDto("connection.removed", wire.ToPin.ToString())));
        changes.AddRange(diff.VariablesAdded.Select(variable => new SemanticChangeDto("variable.added", variable.Name)));
        changes.AddRange(diff.VariablesRemoved.Select(variable => new SemanticChangeDto("variable.removed", variable.Name)));
        changes.AddRange(diff.VariablesModified.Select(pair => new SemanticChangeDto("variable.modified", pair.New.Name)));
        return changes;
    }

    private HistoryDiffResponseMsg HandleHistoryDiffMsg(HistoryDiffRequestMsg req)
    {
        if (!_sessions.ContainsKey(req.SessionId))
        {
            return new HistoryDiffResponseMsg { Status = AuthoringStatusCode.Unauthorized, ErrorMessage = "Invalid session." };
        }

        if (!_revisionSources.ContainsKey(req.RevisionId) && !_liveSources.ContainsKey(req.GraphId))
        {
            return new HistoryDiffResponseMsg { Status = AuthoringStatusCode.NotFound, ErrorMessage = "Revision source was not found." };
        }

        try
        {
            return new HistoryDiffResponseMsg { Status = AuthoringStatusCode.Success, Changes = DiffDraft(req.GraphId, req.RevisionId, req.DraftJson) };
        }
        catch (System.Text.Json.JsonException)
        {
            return new HistoryDiffResponseMsg { Status = AuthoringStatusCode.ValidationError, ErrorMessage = "Draft JSON is not a graph." };
        }
    }

    private HistoryListResponseMsg HandleHistoryListMsg(HistoryListRequestMsg req)
    {
        if (!_sessions.ContainsKey(req.SessionId))
        {
            return new HistoryListResponseMsg { Status = AuthoringStatusCode.Unauthorized, ErrorMessage = "Invalid session." };
        }

        if (_hotReloadManager == null)
        {
            return new HistoryListResponseMsg { Status = AuthoringStatusCode.Success, Revisions = [] };
        }

        var records = _hotReloadManager.GetRevisionHistory(req.GraphId)
            .Select(record => new RevisionSummaryDto(
                record.RevisionId.ToString(),
                record.Author,
                record.Timestamp.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                record.Message,
                record.SemanticHash,
                record.ParentRevisionId?.ToString() ?? "",
                _activation.TryGetValue(record.RevisionId, out var tick) ? tick : 0))
            .ToArray();
        return new HistoryListResponseMsg
        {
            Status = AuthoringStatusCode.Success,
            Records = records,
            Revisions = records.Select(record => record.RevisionId + "|" + record.Message).ToArray()
        };
    }

    private async Task<HistoryLkgResponseMsg> HandleHistoryLkgMsgAsync(HistoryLkgRequestMsg req)
    {
        var resp = await RestoreLastKnownGoodAsync(req.SessionId, req.GraphId);
        return new HistoryLkgResponseMsg
        {
            Status = resp.Status,
            GraphId = req.GraphId,
            ActiveRevision = resp.CurrentRevision,
            ActivatedRevision = resp.ActivatedRevision ?? resp.CurrentRevision,
            ErrorMessage = resp.ErrorMessage
        };
    }

    private async Task<AuthoringMessage> HandleHistoryRollbackMsgAsync(HistoryRollbackRequestMsg req)
    {
        var resp = await HandleRollbackAsync(new RollbackRequest(req.SessionId, req.GraphId, req.TargetRevisionId));
        return new HistoryRollbackResponseMsg
        {
            Status = resp.Status,
            GraphId = req.GraphId,
            ActiveRevision = resp.CurrentRevision,
            ActivatedRevision = resp.ActivatedRevision ?? req.TargetRevisionId,
            ErrorMessage = resp.ErrorMessage
        };
    }

    private DraftCompileResponseMsg HandleDraftCompileMsg(DraftCompileRequestMsg req)
    {
        var resp = HandleDraftCompile(new DraftCompileRequest(req.SessionId, req.GraphId, req.DraftJson));
        return new DraftCompileResponseMsg
        {
            Status = resp.Status,
            HasErrors = resp.HasErrors,
            Diagnostics = resp.Diagnostics,
            BytecodeHash = resp.BytecodeHash,
            ErrorMessage = resp.ErrorMessage,
            Stage = resp.Stage
        };
    }

    private async Task<DraftPublishResponseMsg> HandleDraftPublishMsgAsync(DraftPublishRequestMsg req)
    {
        var resp = await HandleDraftPublishAsync(new DraftPublishRequest(req.SessionId, req.GraphId, req.BaseRevisionId, req.DraftJson, req.PublishMessage));
        return new DraftPublishResponseMsg
        {
            Status = resp.Status,
            PublishedRevision = resp.PublishedRevision,
            Diagnostics = resp.Diagnostics,
            ErrorMessage = resp.ErrorMessage,
            ActivationTick = resp.ActivationTick,
            MigrationSummary = resp.MigrationSummary,
            ClientCount = resp.ClientCount
        };
    }

    private DebuggerCommandResponseMsg HandleDebuggerCommandMsg(DebuggerCommandRequestMsg req)
    {
        var resp = HandleDebuggerCommand(new DebuggerCommandRequest(req.SessionId, req.GraphId, req.Action, req.TargetNode, req.Condition));
        return new DebuggerCommandResponseMsg
        {
            Status = resp.Status,
            IsSuccess = resp.IsSuccess,
            ErrorMessage = resp.ErrorMessage
        };
    }

    public DebuggerInspectResponseMsg InspectDebugger(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return new DebuggerInspectResponseMsg { Status = AuthoringStatusCode.Unauthorized, ErrorMessage = "Invalid session." };
        }

        if (!AstraAuthorizationService.CanDebug(session.User))
        {
            return new DebuggerInspectResponseMsg { Status = AuthoringStatusCode.Unauthorized, ErrorMessage = "Insufficient permissions for debugging." };
        }

        var suspension = _debugger?.CurrentSuspension;
        var registers = suspension?.Registers;
        var locals = new List<DebugValueDto>();
        if (registers != null)
        {
            for (var index = 0; index < registers.Length; index++)
            {
                locals.Add(new DebugValueDto($"r{index}", $"r{index}", registers[index].ToString()));
            }
        }

        var watches = new List<DebugValueDto>();
        if (_watches.TryGetValue(sessionId, out var stored))
        {
            foreach (var (name, expression) in stored)
            {
                watches.Add(new DebugValueDto(name, expression, EvaluateWatch(expression, registers)));
            }
        }

        var trace = _debugger?.GetRecentTrace(12)
            .Select(entry => new DebugTraceDto(entry.NodeId.ToString(), entry.InstructionPointer))
            .ToArray() ?? [];

        return new DebuggerInspectResponseMsg
        {
            Status = AuthoringStatusCode.Success,
            SuspendedNodeId = suspension?.NodeId.ToString(),
            Locals = locals,
            Watches = watches,
            Trace = trace,
            EventPayload = EventPayload(suspension?.NodeId, registers)
        };
    }

    public DraftRebaseResponseMsg RebaseDraft(string sessionId, GraphId graphId)
    {
        if (!_sessions.ContainsKey(sessionId))
        {
            return new DraftRebaseResponseMsg { Status = AuthoringStatusCode.Unauthorized, ErrorMessage = "Invalid session." };
        }

        if (!_activeDrafts.TryGetValue(graphId, out var draft))
        {
            return new DraftRebaseResponseMsg { Status = AuthoringStatusCode.ValidationError, ErrorMessage = "No draft to rebase." };
        }

        if (!_graphs.TryGetValue(graphId, out var summary))
        {
            return new DraftRebaseResponseMsg { Status = AuthoringStatusCode.NotFound, ErrorMessage = "Graph was not found." };
        }

        if (draft.HeadRevision == summary.ActiveRevision)
        {
            return new DraftRebaseResponseMsg { Status = AuthoringStatusCode.Success, DraftJson = draft.DraftJson, BaseRevisionId = summary.ActiveRevision };
        }

        if (!_revisionSources.TryGetValue(draft.HeadRevision, out var baseJson) || !_liveSources.TryGetValue(graphId, out var liveJson))
        {
            return new DraftRebaseResponseMsg { Status = AuthoringStatusCode.ValidationError, ErrorMessage = "Base revision source is missing." };
        }

        var merged = SemanticMerge.Merge(GraphSerializer.Deserialize(baseJson), GraphSerializer.Deserialize(draft.DraftJson), GraphSerializer.Deserialize(liveJson));
        if (merged.Document == null)
        {
            return new DraftRebaseResponseMsg { Status = AuthoringStatusCode.Conflict, Conflicts = string.Join("; ", merged.Conflicts), ErrorMessage = "Rebase has conflicts." };
        }

        var json = GraphSerializer.Serialize(merged.Document, writeIndented: false);
        _activeDrafts[graphId] = (summary.ActiveRevision, json);
        return new DraftRebaseResponseMsg { Status = AuthoringStatusCode.Success, DraftJson = json, BaseRevisionId = summary.ActiveRevision };
    }

    public DraftMergeResponseMsg MergeDraft(string sessionId, GraphId graphId, string otherDraftJson)
    {
        if (!_sessions.ContainsKey(sessionId))
        {
            return new DraftMergeResponseMsg { Status = AuthoringStatusCode.Unauthorized, ErrorMessage = "Invalid session." };
        }

        if (!_liveSources.TryGetValue(graphId, out var liveJson) || !_graphs.TryGetValue(graphId, out var summary))
        {
            return new DraftMergeResponseMsg { Status = AuthoringStatusCode.NotFound, ErrorMessage = "Graph was not found." };
        }

        var hasDraft = _activeDrafts.TryGetValue(graphId, out var draft);
        var oursJson = hasDraft ? draft.DraftJson : liveJson;
        var baseId = hasDraft ? draft.HeadRevision : summary.ActiveRevision;
        var baseJson = _revisionSources.TryGetValue(baseId, out var stored) ? stored : liveJson;
        MergeResult merged;
        try
        {
            merged = SemanticMerge.Merge(GraphSerializer.Deserialize(baseJson), GraphSerializer.Deserialize(oursJson), GraphSerializer.Deserialize(otherDraftJson));
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or FormatException)
        {
            return new DraftMergeResponseMsg { Status = AuthoringStatusCode.ValidationError, ErrorMessage = ex.Message };
        }

        if (merged.Document == null)
        {
            return new DraftMergeResponseMsg { Status = AuthoringStatusCode.Conflict, Conflicts = string.Join("; ", merged.Conflicts), ErrorMessage = "Merge has conflicts." };
        }

        var json = GraphSerializer.Serialize(merged.Document, writeIndented: false);
        _activeDrafts[graphId] = (summary.ActiveRevision, json);
        return new DraftMergeResponseMsg { Status = AuthoringStatusCode.Success, DraftJson = json };
    }

    public GraphTestResponseMsg RunGraphTest(string sessionId, string draftJson)
    {
        if (!_sessions.ContainsKey(sessionId))
        {
            return new GraphTestResponseMsg { Status = AuthoringStatusCode.Unauthorized, ErrorMessage = "Invalid session." };
        }

        GraphDocument document;
        try
        {
            document = GraphSerializer.Deserialize(draftJson);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or FormatException)
        {
            return new GraphTestResponseMsg { Status = AuthoringStatusCode.ValidationError, ErrorMessage = ex.Message };
        }

        if (!document.Metadata.CustomAttributes.TryGetValue("expected", out var expected))
        {
            return new GraphTestResponseMsg { Status = AuthoringStatusCode.ValidationError, ErrorMessage = "Graph has no expected result." };
        }

        var run = RunSandbox(sessionId, draftJson);
        var actual = run.Result ?? "";
        var passed = !run.HasErrors && string.Equals(actual, expected, StringComparison.Ordinal);
        return new GraphTestResponseMsg
        {
            Status = passed ? AuthoringStatusCode.Success : AuthoringStatusCode.ValidationError,
            Passed = passed,
            Actual = actual,
            Expected = expected,
            ErrorMessage = passed ? null : run.Result
        };
    }

    public ProfilerSubscribeResponseMsg SubscribeProfiler(string sessionId, GraphId graphId, bool unsubscribe)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return new ProfilerSubscribeResponseMsg { Status = AuthoringStatusCode.Unauthorized, ErrorMessage = "Invalid session." };
        }

        if (!AstraAuthorizationService.CanDebug(session.User))
        {
            return new ProfilerSubscribeResponseMsg { Status = AuthoringStatusCode.Unauthorized, ErrorMessage = "Insufficient permissions for profiling." };
        }

        if (unsubscribe) _profilerWatch.TryRemove(sessionId, out _);
        else _profilerWatch[sessionId] = graphId;
        return new ProfilerSubscribeResponseMsg { Status = AuthoringStatusCode.Success, Snapshot = CaptureProfiler(graphId, false) };
    }

    public void RememberWatch(string sessionId, string name, string expression, bool remove)
    {
        var stored = _watches.GetOrAdd(sessionId, _ => []);
        if (remove) stored.Remove(name);
        else stored[name] = expression;
    }

    public ProfilerSnapshotDto CaptureProfiler(GraphId graphId, bool reset)
    {
        var metric = _profiler?.GetMetrics(graphId) ?? new AstraGraph.Runtime.Profiling.GraphPerformanceMetric(0, 0, 0, 0, 0, 0);
        var hottest = _profiler?.GetHottestNodes(8)
            .Select(item => new ProfilerNodeDto(item.Key.ToString(), item.Value, _profiler.NodeMicroseconds(item.Key)))
            .ToArray() ?? [];
        var snapshot = new ProfilerSnapshotDto(
            metric.Invocations,
            metric.AverageMicroseconds,
            metric.InstructionsExecuted,
            metric.NativeCallsExecuted,
            metric.Yields,
            hottest,
            metric.P95Microseconds,
            metric.BudgetViolations,
            metric.AllocatedBytes,
            _hotReloadManager?.Host.PendingReplicationBytes() ?? 0,
            AstraGraph.Runtime.MixedQueryEngine.Iterations,
            _profiler?.RecentSamples(graphId) ?? []);
        if (reset) _profiler?.Reset();
        return snapshot;
    }

    public IReadOnlyList<AuditEntryDto> QueryAudit()
    {
        if (_auditLogger == null) return [];
        return _auditLogger.ReadRecords(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddMinutes(5))
            .Select(record => new AuditEntryDto(
                record.Timestamp.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                record.Action,
                record.Author,
                record.Message,
                record.Success,
                record.GraphId?.ToString()))
            .ToArray();
    }

    public SchemaSaveResponseMsg SaveSchema(string sessionId, SchemaDto? schema)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return new SchemaSaveResponseMsg { Status = AuthoringStatusCode.Unauthorized, ErrorMessage = "Invalid session." };
        }

        if (!AstraAuthorizationService.CanEditDraft(session.User))
        {
            return new SchemaSaveResponseMsg { Status = AuthoringStatusCode.Unauthorized, ErrorMessage = "Insufficient permissions to edit a schema." };
        }

        if (schema == null || string.IsNullOrWhiteSpace(schema.Name))
        {
            return new SchemaSaveResponseMsg { Status = AuthoringStatusCode.ValidationError, ErrorMessage = "Schema name is required." };
        }

        var kind = string.IsNullOrWhiteSpace(schema.Kind)
            ? schema.IsComponent ? "Component" : "Struct"
            : schema.Kind.Trim();
        if (kind.Equals("Enum", StringComparison.OrdinalIgnoreCase))
        {
            return SaveEnum(schema);
        }

        if (kind is not ("Component" or "Struct" or "Interface" or "Contract"))
        {
            return new SchemaSaveResponseMsg { Status = AuthoringStatusCode.ValidationError, ErrorMessage = $"Unknown schema kind '{kind}'." };
        }

        if (!SchemaId.TryParse(string.IsNullOrWhiteSpace(schema.Id) ? null : schema.Id, out var schemaId) || schemaId == SchemaId.Empty)
        {
            schemaId = SchemaId.New();
        }

        _enums.TryRemove(schemaId.ToString(), out _);

        var fields = new List<SchemaField>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in schema.Fields ?? [])
        {
            if (string.IsNullOrWhiteSpace(field.Name) || !names.Add(field.Name))
            {
                return new SchemaSaveResponseMsg { Status = AuthoringStatusCode.ValidationError, ErrorMessage = "Schema fields need unique names." };
            }

            if (!TryType(field.TypeName, out var type))
            {
                return new SchemaSaveResponseMsg { Status = AuthoringStatusCode.ValidationError, ErrorMessage = $"Unknown field type '{field.TypeName}'." };
            }

            var fieldId = FieldId.TryParse(field.Id, out var parsed) && parsed != FieldId.Empty ? parsed : FieldId.New();
            var options = SchemaFieldOptions.None;
            if (field.Persistent) options |= SchemaFieldOptions.Persistent;
            if (field.Replicated) options |= SchemaFieldOptions.Replicated;
            fields.Add(new SchemaField(fieldId, field.Name.Trim(), type, field.DefaultValue, options));
        }

        var isComponent = kind.Equals("Component", StringComparison.OrdinalIgnoreCase);
        var stored = new SchemaType(schemaId, schema.Name.Trim(), isComponent, fields);
        if (kind.Equals("Interface", StringComparison.OrdinalIgnoreCase) || kind.Equals("Contract", StringComparison.OrdinalIgnoreCase))
        {
            _schemaKinds[schemaId] = kind;
        }
        else
        {
            _schemaKinds.TryRemove(schemaId, out _);
        }
        var migration = _schemas.TryGetValue(schemaId, out var previous)
            ? StateMigrationPlanner.CreatePlan(previous, stored).Steps
                .Select(step => new SemanticChangeDto(step.Action.ToString(), $"{step.SourceFieldName} → {step.TargetFieldName}"))
                .ToArray()
            : [];
        _schemas[schemaId] = stored;
        _lastMigration = migration.Length == 0 ? "No schema migration" : string.Join("; ", migration.Select(change => $"{change.Kind} {change.Detail}"));
        return new SchemaSaveResponseMsg { Status = AuthoringStatusCode.Success, Schema = ToSchemaDto(stored), Changes = migration };
    }

    private SchemaSaveResponseMsg SaveEnum(SchemaDto schema)
    {
        if (!SymbolId.TryParse(schema.Id, out var symbolId) || symbolId == SymbolId.Empty)
        {
            symbolId = SymbolId.New();
        }

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in schema.Members ?? [])
        {
            var name = member.Trim();
            if (name.Length == 0 || !seen.Add(name))
            {
                return new SchemaSaveResponseMsg { Status = AuthoringStatusCode.ValidationError, ErrorMessage = "Enum members need unique names." };
            }

            names.Add(name);
        }

        if (names.Count == 0)
        {
            return new SchemaSaveResponseMsg { Status = AuthoringStatusCode.ValidationError, ErrorMessage = "Enum needs at least one member." };
        }

        if (SchemaId.TryParse(symbolId.ToString(), out var schemaId))
        {
            _schemas.TryRemove(schemaId, out _);
        }

        var stored = new EnumType(symbolId, schema.Name.Trim(), names.Select((name, index) => new EnumMember(name, index)).ToArray());
        _enums[symbolId.ToString()] = stored;
        return new SchemaSaveResponseMsg { Status = AuthoringStatusCode.Success, Schema = ToEnumDto(stored) };
    }

    public IReadOnlyList<SchemaDto> ListSchemas() => _schemas.Values.Select(ToSchemaDto)
        .Concat(_enums.Values.Select(ToEnumDto))
        .OrderBy(schema => schema.Name)
        .ToArray();

    private SchemaDto ToSchemaDto(SchemaType schema) => new(
        schema.Id.ToString(),
        schema.Name,
        schema.IsComponentSchema,
        schema.Fields.Select(field => new SchemaFieldDto(
            field.Id.ToString(),
            field.Name,
            field.Type.TypeName,
            field.DefaultValue,
            field.IsPersistent,
            field.IsReplicated)).ToArray(),
        _schemaKinds.TryGetValue(schema.Id, out var kind) ? kind : schema.IsComponentSchema ? "Component" : "Struct");

    private static SchemaDto ToEnumDto(EnumType type) => new(
        type.Id.ToString(),
        type.Name,
        false,
        [],
        "Enum",
        type.Members.Select(member => member.Name).ToArray());

    private static bool TryType(string typeName, out AstraType type)
    {
        typeName = typeName.Trim();
        if (typeName.EndsWith('?') && typeName.Length > 1 && TryType(typeName[..^1], out var inner))
        {
            type = new NullableType(inner);
            return true;
        }

        if (TryCollection("List<", CollectionKind.List, typeName, out type) || TryCollection("Set<", CollectionKind.Set, typeName, out type))
        {
            return true;
        }

        if (typeName.StartsWith("Dictionary<", StringComparison.Ordinal) && typeName.EndsWith('>'))
        {
            var body = typeName["Dictionary<".Length..^1];
            var comma = body.IndexOf(',');
            if (comma > 0 && TryType(body[..comma], out var key) && TryType(body[(comma + 1)..], out var element))
            {
                type = new CollectionType(CollectionKind.Dictionary, element, key);
                return true;
            }
        }

        return TryPrimitive(typeName, out type);
    }

    private static bool TryCollection(string prefix, CollectionKind kind, string typeName, out AstraType type)
    {
        type = PrimitiveType.String;
        if (!typeName.StartsWith(prefix, StringComparison.Ordinal) || !typeName.EndsWith('>')) return false;
        if (!TryType(typeName[prefix.Length..^1], out var element)) return false;
        type = new CollectionType(kind, element);
        return true;
    }

    private static bool TryPrimitive(string typeName, out AstraType type)
    {
        AstraType? parsed = typeName switch
        {
            "int32" or "System.Int32" => PrimitiveType.Int32,
            "int64" or "System.Int64" => PrimitiveType.Int64,
            "float32" or "System.Single" => PrimitiveType.Float32,
            "float64" or "System.Double" => PrimitiveType.Float64,
            "bool" or "System.Boolean" => PrimitiveType.Bool,
            "string" or "System.String" => PrimitiveType.String,
            _ => null
        };
        type = parsed ?? PrimitiveType.String;
        return parsed != null;
    }

    private static bool TryBreakpointCondition(string? text, out Func<IReadOnlyList<AstraValue>, bool>? condition, out string? error)
    {
        condition = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        var parts = text.Split("==", StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && parts[0].Length > 1 && parts[0][0] == 'r' && int.TryParse(parts[0].AsSpan(1), out var index) && long.TryParse(parts[1], out var expected))
        {
            condition = registers => index < registers.Count && registers[index].AsInt64() == expected;
            return true;
        }

        error = "Condition must look like r0==7.";
        return false;
    }

    public SandboxRunDto RunSandbox(string sessionId, string draftJson)
    {
        var compiled = HandleDraftCompile(new DraftCompileRequest(sessionId, GraphId.Empty, draftJson));
        if (compiled.HasErrors)
        {
            return new SandboxRunDto(compiled.Stage, compiled.BytecodeHash, compiled.ErrorMessage, true);
        }

        try
        {
            var document = GraphSerializer.Deserialize(draftJson);
            var analysis = new SemanticAnalyzer().Analyze(document);
            var ir = AstToIrCompiler.Compile(analysis.Program!);
            var bytecode = IrToBytecodeCompiler.Compile(ir);
            var function = bytecode.EntryPoints.FirstOrDefault() ?? bytecode.Functions.FirstOrDefault();
            if (function == null) return new SandboxRunDto("Verify", bytecode.SemanticHash, "No function to run", true);
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var executed = new AstraGraph.VM.AstraVm().Execute(bytecode, function, onNodeElapsed: (node, microseconds) =>
            {
                _profiler?.RecordNodeTime(node, microseconds);
                if (_profilerWatch.Values.Contains(document.Id))
                {
                    PublishOutbound(new ProfilerSnapshotResponseMsg { Status = AuthoringStatusCode.Success, Snapshot = CaptureProfiler(document.Id, false) });
                }
            });
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            var elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1_000_000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (document.Id != GraphId.Empty)
            {
                _profiler?.RecordElapsed(document.Id, elapsed);
                _profiler?.RecordAllocation(document.Id, allocated);
                if (executed.Status == AstraGraph.VM.VmExecutionStatus.ExceededBudget)
                {
                    _profiler?.RecordBudgetViolation(document.Id);
                }
            }

            var failed = executed.Status is AstraGraph.VM.VmExecutionStatus.Faulted or AstraGraph.VM.VmExecutionStatus.ExceededBudget;
            return new SandboxRunDto("Verify", bytecode.SemanticHash, executed.Exception?.Message ?? executed.ReturnValue.ToString(), failed);
        }
        catch (Exception ex)
        {
            return new SandboxRunDto(compiled.Stage, compiled.BytecodeHash, ex.Message, true);
        }
    }

    public IReadOnlyList<RuntimeGraphDto> RuntimeGraphs() => _graphs.Values
        .Select(graph => new RuntimeGraphDto(graph.Id.ToString(), graph.Name, graph.ActiveRevision.ToString(), graph.RevisionCount))
        .OrderBy(graph => graph.Name)
        .ToArray();

    public IReadOnlyList<RuntimeEntityDto> RuntimeEntities() => (_hotReloadManager?.Host.InspectEntities() ?? [])
        .Select(item => new RuntimeEntityDto(
            item.EntityId,
            item.SchemaName,
            item.Fields.Select(field => new RuntimeFieldDto(field.Name, field.Value)).ToArray()))
        .ToArray();

    private string EvaluateWatch(string expression, AstraValue[]? registers)
    {
        if (expression.StartsWith("entity:", StringComparison.Ordinal))
        {
            var body = expression["entity:".Length..];
            var first = body.IndexOf('.');
            var second = first < 0 ? -1 : body.IndexOf('.', first + 1);
            if (first > 0 && second > first && int.TryParse(body[..first], out var entityId))
            {
                var schema = body[(first + 1)..second];
                var field = body[(second + 1)..];
                var match = RuntimeEntities().FirstOrDefault(item => item.EntityId == entityId && item.Schema == schema);
                return match?.Fields.FirstOrDefault(item => item.Name == field)?.Value ?? "(unavailable)";
            }
        }

        if (registers != null && expression.Length > 1 && expression[0] == 'r' && int.TryParse(expression.AsSpan(1), out var index) && index >= 0 && index < registers.Length)
        {
            return registers[index].ToString();
        }

        return "(unavailable)";
    }

    private string? EventPayload(NodeId? nodeId, AstraValue[]? registers)
    {
        if (nodeId == null || registers is not { Length: > 0 }) return null;
        foreach (var json in _liveSources.Values)
        {
            try
            {
                var node = GraphSerializer.Deserialize(json).FindNode(nodeId.Value);
                if (node != null && node.NodeType.Contains("Event", StringComparison.OrdinalIgnoreCase))
                {
                    return registers[0].ToString();
                }
            }
            catch (System.Text.Json.JsonException)
            {
                continue;
            }
        }

        return null;
    }

    private DebuggerInspectResponseMsg HandleDebuggerInspectMsg(DebuggerInspectRequestMsg req) => InspectDebugger(req.SessionId);

    private DebuggerWatchResponseMsg HandleDebuggerWatchMsg(DebuggerWatchRequestMsg req)
    {
        if (!_sessions.TryGetValue(req.SessionId, out var session))
        {
            return new DebuggerWatchResponseMsg { Status = AuthoringStatusCode.Unauthorized, ErrorMessage = "Invalid session." };
        }

        if (!AstraAuthorizationService.CanDebug(session.User))
        {
            return new DebuggerWatchResponseMsg { Status = AuthoringStatusCode.Unauthorized, ErrorMessage = "Insufficient permissions for debugging." };
        }

        if (string.IsNullOrWhiteSpace(req.Name))
        {
            return new DebuggerWatchResponseMsg { Status = AuthoringStatusCode.ValidationError, ErrorMessage = "Watch name is required." };
        }

        RememberWatch(req.SessionId, req.Name.Trim(), req.Expression, req.Remove);
        return new DebuggerWatchResponseMsg { Status = AuthoringStatusCode.Success };
    }

    private ProfilerSnapshotResponseMsg HandleProfilerSnapshotMsg(ProfilerSnapshotRequestMsg req)
    {
        if (!_sessions.TryGetValue(req.SessionId, out var session))
        {
            return new ProfilerSnapshotResponseMsg { Status = AuthoringStatusCode.Unauthorized, ErrorMessage = "Invalid session." };
        }

        if (!AstraAuthorizationService.CanDebug(session.User))
        {
            return new ProfilerSnapshotResponseMsg { Status = AuthoringStatusCode.Unauthorized, ErrorMessage = "Insufficient permissions for profiling." };
        }

        return new ProfilerSnapshotResponseMsg { Status = AuthoringStatusCode.Success, Snapshot = CaptureProfiler(req.GraphId, req.Reset) };
    }

    private AuditQueryResponseMsg HandleAuditQueryMsg(AuditQueryRequestMsg req)
    {
        if (!_sessions.TryGetValue(req.SessionId, out var session))
        {
            return new AuditQueryResponseMsg { Status = AuthoringStatusCode.Unauthorized, ErrorMessage = "Invalid session." };
        }

        if (!AstraAuthorizationService.HasPermission(session.User, AstraPermission.ViewGraphs))
        {
            return new AuditQueryResponseMsg { Status = AuthoringStatusCode.Unauthorized, ErrorMessage = "Insufficient permissions for audit." };
        }

        return new AuditQueryResponseMsg { Status = AuthoringStatusCode.Success, Entries = QueryAudit() };
    }

    private SchemaListResponseMsg HandleSchemaListMsg(SchemaListRequestMsg req)
    {
        if (!_sessions.ContainsKey(req.SessionId))
        {
            return new SchemaListResponseMsg { Status = AuthoringStatusCode.Unauthorized, ErrorMessage = "Invalid session." };
        }

        return new SchemaListResponseMsg { Status = AuthoringStatusCode.Success, Schemas = ListSchemas() };
    }

    private SchemaSaveResponseMsg HandleSchemaSaveMsg(SchemaSaveRequestMsg req) => SaveSchema(req.SessionId, req.Schema);

    private DraftDiscardResponseMsg HandleDraftDiscardMsg(DraftDiscardRequestMsg req)
    {
        var fetched = DiscardDraft(req.SessionId, req.GraphId);
        return new DraftDiscardResponseMsg
        {
            Status = fetched.Status,
            DraftJson = fetched.DraftJson,
            BaseRevisionId = fetched.BaseRevisionId,
            ActiveRevisionId = fetched.ActiveRevisionId,
            FromDraft = fetched.FromDraft,
            ErrorMessage = fetched.ErrorMessage
        };
    }

    public UiSaveResponseMsg SaveUi(string sessionId, UiDocumentDto? document)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return new UiSaveResponseMsg { Status = AuthoringStatusCode.Unauthorized, ErrorMessage = "Invalid session." };
        }

        if (!AstraAuthorizationService.CanEditDraft(session.User))
        {
            return new UiSaveResponseMsg { Status = AuthoringStatusCode.Unauthorized, ErrorMessage = "Insufficient permissions to edit UI." };
        }

        if (document?.Root == null || string.IsNullOrWhiteSpace(document.Name))
        {
            return new UiSaveResponseMsg { Status = AuthoringStatusCode.ValidationError, ErrorMessage = "UI document needs a name and a root control." };
        }

        if (!GraphId.TryParse(document.Id, out var id) || id == GraphId.Empty)
        {
            id = GraphId.New();
        }

        var built = new UiDocument
        {
            Id = id,
            Name = document.Name.Trim(),
            Title = document.Name.Trim(),
            Kind = GraphKind.UI,
            Side = GraphSide.Client,
            DefaultWidth = document.Width <= 0 ? 400 : document.Width,
            DefaultHeight = document.Height <= 0 ? 300 : document.Height,
            Root = BuildUiNode(document.Root, 0),
            Bindings = (document.Bindings ?? []).Select(binding => new UiBindingDefinition
            {
                BindingId = string.IsNullOrWhiteSpace(binding.BindingId) ? Guid.NewGuid().ToString("D") : binding.BindingId,
                ElementId = binding.ElementId,
                TargetProperty = binding.TargetProperty,
                StateVariable = binding.StateVariable,
                Direction = Enum.TryParse<BindingDirection>(binding.Direction, ignoreCase: true, out var direction) ? direction : BindingDirection.OneWay
            }).ToList(),
            Events = (document.Events ?? []).Select(item => new UiEventSubscription
            {
                SubscriptionId = string.IsNullOrWhiteSpace(item.SubscriptionId) ? Guid.NewGuid().ToString("D") : item.SubscriptionId,
                ElementId = item.ElementId,
                EventName = item.EventName,
                TargetAction = item.TargetAction
            }).ToList(),
            LocalStateDefaults = (document.LocalState ?? new Dictionary<string, string>()).ToDictionary(pair => pair.Key, pair => (object?)pair.Value)
        };
        _uiDocuments[id] = built;
        return new UiSaveResponseMsg { Status = AuthoringStatusCode.Success, Document = ToUiDto(built) };
    }

    public UiCompileResponseMsg CompileUi(string sessionId, UiDocumentDto? document)
    {
        if (!_sessions.ContainsKey(sessionId))
        {
            return new UiCompileResponseMsg { Status = AuthoringStatusCode.Unauthorized, ErrorMessage = "Invalid session." };
        }

        if (document?.Root == null || string.IsNullOrWhiteSpace(document.Name))
        {
            return new UiCompileResponseMsg { Status = AuthoringStatusCode.ValidationError, ErrorMessage = "UI document needs a name and a root control." };
        }

        try
        {
            var saved = SaveUi(sessionId, document);
            if (saved.Status != AuthoringStatusCode.Success || saved.Document == null)
            {
                return new UiCompileResponseMsg { Status = saved.Status, ErrorMessage = saved.ErrorMessage };
            }

            var stored = _uiDocuments[GraphId.FromString(saved.Document.Id)];
            var compiled = UiCompiler.Compile(stored);
            var mounted = MountControls(stored.Root);
            return new UiCompileResponseMsg
            {
                Status = compiled.Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error) ? AuthoringStatusCode.ValidationError : AuthoringStatusCode.Success,
                Diagnostics = compiled.Diagnostics,
                ElementCount = mounted.Count,
                MaxDepth = UiDepth(stored.Root, 1),
                BindingCount = stored.Bindings.Count,
                Mounted = string.Join(", ", mounted)
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            return new UiCompileResponseMsg { Status = AuthoringStatusCode.ValidationError, ErrorMessage = ex.Message };
        }
    }

    public IReadOnlyList<UiDocumentDto> ListUi() => _uiDocuments.Values.Select(ToUiDto).OrderBy(document => document.Name).ToArray();

    private static UiElementNode BuildUiNode(UiNodeDto dto, int depth)
    {
        if (depth > 32)
        {
            throw new InvalidOperationException("UI tree is too deep.");
        }

        if (!Enum.TryParse<UiElementType>(dto.ElementType, ignoreCase: true, out var elementType))
        {
            throw new InvalidOperationException($"Unknown UI control '{dto.ElementType}'.");
        }

        if (!Enum.TryParse<UiOrientation>(dto.Orientation, ignoreCase: true, out var orientation))
        {
            orientation = UiOrientation.Vertical;
        }

        return new UiElementNode
        {
            Id = string.IsNullOrWhiteSpace(dto.Id) ? Guid.NewGuid().ToString("D") : dto.Id,
            ElementType = elementType,
            Name = dto.Name,
            Text = dto.Text,
            Visible = dto.Visible,
            Enabled = dto.Enabled,
            Orientation = orientation,
            MinWidth = dto.MinWidth,
            MinHeight = dto.MinHeight,
            StyleClasses = (dto.StyleClasses ?? []).Where(item => !string.IsNullOrWhiteSpace(item)).ToList(),
            CustomProperties = new Dictionary<string, object?> { ["valueSource"] = string.IsNullOrWhiteSpace(dto.ValueSource) ? "Constant" : dto.ValueSource },
            Children = (dto.Children ?? []).Select(child => BuildUiNode(child, depth + 1)).ToList()
        };
    }

    private static List<string> MountControls(UiElementNode node)
    {
        var factory = UiFactory();
        var mounted = new List<string>();
        void Walk(UiElementNode current)
        {
            var control = factory.CreateControl(current.Id, current.ElementType, current.Name, current.MinWidth, current.MinHeight, current.Orientation);
            var source = current.CustomProperties.TryGetValue("valueSource", out var mode) ? mode?.ToString() : "Constant";
            control.SetProperty("Text", current.Text);
            control.SetProperty("valueSource", source);
            mounted.Add($"{control.ElementType}:{source}");
            foreach (var child in current.Children) Walk(child);
        }

        Walk(node);
        return mounted;
    }

    private static IRobustUiControlFactory UiFactory()
    {
        var native = Type.GetType("AstraGraph.Robust.Client.RobustUiControlFactory, AstraGraph.Robust.Client");
        if (native != null && Activator.CreateInstance(native) is IRobustUiControlFactory factory)
        {
            var available = native.GetMethod("IsNativeUiAvailable")?.Invoke(null, null);
            if (available is true) return factory;
        }

        return new MockRobustUiControlFactory();
    }

    private static int UiDepth(UiElementNode node, int depth) =>
        node.Children.Count == 0 ? depth : node.Children.Max(child => UiDepth(child, depth + 1));

    private static UiDocumentDto ToUiDto(UiDocument document) => new(
        document.Id.ToString(),
        document.Name,
        document.DefaultWidth,
        document.DefaultHeight,
        ToUiNode(document.Root),
        document.Bindings.Select(binding => new UiBindingDto(binding.BindingId, binding.ElementId, binding.TargetProperty, binding.StateVariable, binding.Direction.ToString())).ToArray(),
        document.Events.Select(item => new UiEventDto(item.SubscriptionId, item.ElementId, item.EventName, item.TargetAction)).ToArray(),
        document.LocalStateDefaults.ToDictionary(pair => pair.Key, pair => pair.Value?.ToString() ?? ""));

    private static UiNodeDto ToUiNode(UiElementNode node) => new(
        node.Id,
        node.ElementType.ToString(),
        node.Name,
        node.Text,
        node.Children.Select(ToUiNode).ToArray(),
        node.Visible,
        node.Enabled,
        node.Orientation.ToString(),
        node.MinWidth,
        node.MinHeight,
        node.StyleClasses,
        node.CustomProperties.TryGetValue("valueSource", out var source) ? source?.ToString() ?? "Constant" : "Constant");

    private UiSaveResponseMsg HandleUiSaveMsg(UiSaveRequestMsg req)
    {
        try
        {
            return SaveUi(req.SessionId, req.Document);
        }
        catch (InvalidOperationException ex)
        {
            return new UiSaveResponseMsg { Status = AuthoringStatusCode.ValidationError, ErrorMessage = ex.Message };
        }
    }

    private UiListResponseMsg HandleUiListMsg(UiListRequestMsg req)
    {
        if (!_sessions.ContainsKey(req.SessionId))
        {
            return new UiListResponseMsg { Status = AuthoringStatusCode.Unauthorized, ErrorMessage = "Invalid session." };
        }

        return new UiListResponseMsg { Status = AuthoringStatusCode.Success, Documents = ListUi() };
    }

    private SandboxRunResponseMsg HandleSandboxRunMsg(SandboxRunRequestMsg req)
    {
        if (!_sessions.ContainsKey(req.SessionId))
        {
            return new SandboxRunResponseMsg { Status = AuthoringStatusCode.Unauthorized, ErrorMessage = "Invalid session." };
        }

        var run = RunSandbox(req.SessionId, req.DraftJson);
        return new SandboxRunResponseMsg
        {
            Status = run.HasErrors ? AuthoringStatusCode.ValidationError : AuthoringStatusCode.Success,
            Stage = run.Stage,
            SemanticHash = run.SemanticHash,
            Result = run.Result,
            HasErrors = run.HasErrors,
            ErrorMessage = run.HasErrors ? run.Result : null
        };
    }

    private RuntimeStatusResponseMsg HandleRuntimeStatusMsg(RuntimeStatusRequestMsg req)
    {
        if (!_sessions.ContainsKey(req.SessionId))
        {
            return new RuntimeStatusResponseMsg { Status = AuthoringStatusCode.Unauthorized, ErrorMessage = "Invalid session." };
        }

        return new RuntimeStatusResponseMsg { Status = AuthoringStatusCode.Success, Graphs = RuntimeGraphs(), Entities = RuntimeEntities() };
    }
}
