using AstraGraph.Core;
using AstraGraph.Runtime.Security;

namespace AstraGraph.Editor.Protocol;

public sealed class AuthoringClientSession
{
    private readonly AuthoringServerSession _server;
    private string? _sessionId;
    private AstraPermission _permissions = AstraPermission.None;

    public bool IsAuthenticated => !string.IsNullOrEmpty(_sessionId);
    public string? SessionId => _sessionId;
    public AstraPermission Permissions => _permissions;

    public AuthoringClientSession(AuthoringServerSession server)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
    }

    public Task<AuthHandshakeResponse> ConnectAsync(string authorToken, string authorName, string clientVersion = "1.0.0")
    {
        var response = _server.HandleHandshake(new AuthHandshakeRequest(clientVersion, authorToken, authorName));
        if (response.Status == AuthoringStatusCode.Success)
        {
            _sessionId = response.SessionId;
            _permissions = response.Permissions;
        }
        return Task.FromResult(response);
    }

    public Task<GraphListResponse> ListGraphsAsync()
    {
        EnsureAuthenticated();
        var response = _server.HandleGraphList(new GraphListRequest(_sessionId!));
        return Task.FromResult(response);
    }

    public Task<DraftSaveResponse> SaveDraftAsync(GraphId graphId, RevisionId baseRevisionId, string draftJson, string authorMessage = "")
    {
        EnsureAuthenticated();
        var response = _server.HandleDraftSave(new DraftSaveRequest(_sessionId!, graphId, baseRevisionId, draftJson, authorMessage));
        return Task.FromResult(response);
    }

    public Task<DraftCompileResponse> CompileDraftAsync(GraphId graphId, string draftJson)
    {
        EnsureAuthenticated();
        var response = _server.HandleDraftCompile(new DraftCompileRequest(_sessionId!, graphId, draftJson));
        return Task.FromResult(response);
    }

    public async Task<DraftPublishResponse> PublishDraftAsync(GraphId graphId, RevisionId baseRevisionId, string draftJson, string publishMessage)
    {
        EnsureAuthenticated();
        return await _server.HandleDraftPublishAsync(new DraftPublishRequest(_sessionId!, graphId, baseRevisionId, draftJson, publishMessage)).ConfigureAwait(false);
    }

    public async Task<RollbackResponse> RollbackAsync(GraphId graphId, RevisionId targetRevision)
    {
        EnsureAuthenticated();
        return await _server.HandleRollbackAsync(new RollbackRequest(_sessionId!, graphId, targetRevision)).ConfigureAwait(false);
    }

    public Task<CatalogQueryResponse> QueryCatalogAsync(string? searchFilter = null, GraphSide? targetSide = null)
    {
        EnsureAuthenticated();
        var response = _server.HandleCatalogQuery(new CatalogQueryRequest(_sessionId!, searchFilter, targetSide));
        return Task.FromResult(response);
    }

    public Task<DebuggerCommandResponse> SendDebuggerCommandAsync(GraphId graphId, DebuggerAction action, NodeId? targetNode = null)
    {
        EnsureAuthenticated();
        var response = _server.HandleDebuggerCommand(new DebuggerCommandRequest(_sessionId!, graphId, action, targetNode));
        return Task.FromResult(response);
    }

    private void EnsureAuthenticated()
    {
        if (!IsAuthenticated)
        {
            throw new InvalidOperationException("AuthoringClientSession is not authenticated. Call ConnectAsync first.");
        }
    }
}
