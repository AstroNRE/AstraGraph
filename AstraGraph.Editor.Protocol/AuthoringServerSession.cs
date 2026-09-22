using System.Collections.Concurrent;
using AstraGraph.Binding;
using AstraGraph.Core;
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

        if (_hotReloadManager != null)
        {
            var rollbackResult = _hotReloadManager.Rollback(request.GraphId);
            if (!rollbackResult.Success)
            {
                return Task.FromResult(new RollbackResponse(AuthoringStatusCode.InternalError, RevisionId.New(), rollbackResult.ErrorMessage ?? "Rollback failed on server."));
            }
        }

        var targetRev = request.TargetRevision;
        if (_graphs.TryGetValue(request.GraphId, out var existing))
        {
            _graphs[request.GraphId] = existing with { ActiveRevision = targetRev };
        }

        return Task.FromResult(new RollbackResponse(AuthoringStatusCode.Success, targetRev));
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

                list.Add(new CatalogEntryDto(
                    Signature: desc.Descriptor,
                    Category: desc.DeclaringTypeName,
                    IsPure: desc.IsPure,
                    IsPredictionSafe: desc.IsDeterministic,
                    Side: desc.Side,
                    Documentation: desc.Name));
            }
        }

        return new CatalogQueryResponse(AuthoringStatusCode.Success, list);
    }

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

            CatalogQueryRequestMsg req =>
                HandleCatalogQueryMsg(req),

            HistoryListRequestMsg req =>
                HandleHistoryListMsg(req),

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
        var resp = HandleHandshake(new AuthHandshakeRequest(req.ClientVersion, req.AuthorToken, req.AuthorName));
        return new AuthHandshakeResponseMsg
        {
            Status = resp.Status,
            SessionId = resp.SessionId,
            Permissions = resp.Permissions,
            ErrorMessage = resp.ErrorMessage,
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
        if (!_sessions.ContainsKey(req.SessionId))
        {
            return new GraphFetchResponseMsg { Status = AuthoringStatusCode.Unauthorized, ErrorMessage = "Invalid session." };
        }

        if (!_activeDrafts.TryGetValue(req.GraphId, out var draft))
        {
            return new GraphFetchResponseMsg { Status = AuthoringStatusCode.NotFound, ErrorMessage = "No saved draft." };
        }

        return new GraphFetchResponseMsg { Status = AuthoringStatusCode.Success, DraftJson = draft.DraftJson };
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

        var revisions = _hotReloadManager.GetRevisionHistory(req.GraphId)
            .Select(record => record.RevisionId + "|" + record.Message)
            .ToArray();
        return new HistoryListResponseMsg { Status = AuthoringStatusCode.Success, Revisions = revisions };
    }

    private async Task<AuthoringMessage> HandleHistoryRollbackMsgAsync(HistoryRollbackRequestMsg req)
    {
        var resp = await HandleRollbackAsync(new RollbackRequest(req.SessionId, req.GraphId, req.TargetRevisionId));
        return new HistoryListResponseMsg
        {
            Status = resp.Status,
            Revisions = [resp.CurrentRevision.ToString()],
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
