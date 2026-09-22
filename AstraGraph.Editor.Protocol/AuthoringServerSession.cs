using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Reflection;
using AstraGraph.Binding;
using AstraGraph.Core;
using AstraGraph.Editor.Core;
using AstraGraph.Editor.Core.Search;
using AstraGraph.HotReload;
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

    public GraphListResponse HandleGraphList(GraphListRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_sessions.TryGetValue(request.SessionId, out _))
        {
            return new GraphListResponse(AuthoringStatusCode.Unauthorized, [], "Invalid session.");
        }

        return new GraphListResponse(AuthoringStatusCode.Success, _graphs.Values.ToList());
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
        _activeDrafts[request.GraphId] = (draftRevision, request.DraftJson);

        return new DraftSaveResponse(AuthoringStatusCode.Success, draftRevision, false);
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
                return new DraftCompileResponse(AuthoringStatusCode.ValidationError, true, analysis.Diagnostics);
            }

            var irProgram = AstToIrCompiler.Compile(analysis.Program!);
            var verification = IrVerifier.Verify(irProgram);

            if (verification.HasErrors)
            {
                return new DraftCompileResponse(AuthoringStatusCode.ValidationError, true, verification);
            }

            var bytecode = IrToBytecodeCompiler.Compile(irProgram);
            return new DraftCompileResponse(AuthoringStatusCode.Success, false, analysis.Diagnostics, bytecode.SemanticHash);
        }
        catch (Exception ex)
        {
            return new DraftCompileResponse(AuthoringStatusCode.InternalError, true, [], null, $"Compilation exception: {ex.Message}");
        }
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
        _activeDrafts.TryRemove(request.GraphId, out _);

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

        return Task.FromResult(new DraftPublishResponse(AuthoringStatusCode.Success, newRevision, []));
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
        _hotReloadManager?.Deactivate(request.GraphId);
        return new GraphMutationResponse(AuthoringStatusCode.Success, summary);
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
                _debugger.SetBreakpoint(new Breakpoint(request.TargetNode.Value, BreakpointMode.GraphPause));
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

            GraphFetchRequestMsg req =>
                HandleGraphFetchMsg(req),

            GraphCreateRequestMsg req =>
                HandleGraphCreateMsg(req),

            GraphRenameRequestMsg req =>
                HandleGraphRenameMsg(req),

            GraphDeleteRequestMsg req =>
                HandleGraphDeleteMsg(req),

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

            DraftCompileRequestMsg req =>
                HandleDraftCompileMsg(req),

            DraftPublishRequestMsg req =>
                await HandleDraftPublishMsgAsync(req),

            DebuggerCommandRequestMsg req =>
                HandleDebuggerCommandMsg(req),

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
            ErrorMessage = resp.ErrorMessage
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
                record.SemanticHash))
            .ToArray();
        return new HistoryListResponseMsg
        {
            Status = AuthoringStatusCode.Success,
            Records = records,
            Revisions = records.Select(record => record.RevisionId + "|" + record.Message).ToArray()
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
            ErrorMessage = resp.ErrorMessage
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
            ErrorMessage = resp.ErrorMessage
        };
    }

    private DebuggerCommandResponseMsg HandleDebuggerCommandMsg(DebuggerCommandRequestMsg req)
    {
        var resp = HandleDebuggerCommand(new DebuggerCommandRequest(req.SessionId, req.GraphId, req.Action, req.TargetNode));
        return new DebuggerCommandResponseMsg
        {
            Status = resp.Status,
            IsSuccess = resp.IsSuccess,
            ErrorMessage = resp.ErrorMessage
        };
    }
}
