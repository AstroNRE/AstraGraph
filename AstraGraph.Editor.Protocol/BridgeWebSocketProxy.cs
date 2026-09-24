using System.Net.WebSockets;
using System.Text.Json;

namespace AstraGraph.Editor.Protocol;

/// <summary>
/// Server-side WebSocket proxy: accepts a raw WebSocket connection,
/// routes incoming authoring messages from the browser to an IAuthoringMessageHandler,
/// and forwards server-side events back to the browser.
/// </summary>
public sealed class BridgeWebSocketProxy : IAsyncDisposable
{
    private readonly IAuthoringMessageHandler _handler;
    private readonly int _receiveBufferSize;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;

    private WebSocket? _ws;
    public bool IsRunning => _receiveLoop != null && !_receiveLoop.IsCompleted;

    public BridgeWebSocketProxy(IAuthoringMessageHandler handler, int receiveBufferSize = 256 * 1024)
    {
        _handler = handler;
        _receiveBufferSize = receiveBufferSize;
    }

    /// <summary>
    /// Starts processing the WebSocket connection. Returns when the connection is closed.
    /// </summary>
    public async Task RunAsync(WebSocket webSocket, CancellationToken ct = default)
    {
        _ws = webSocket;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        IDisposable? outbound = null;
        var session = _handler as AuthoringServerSession ?? (_handler as IAuthoringSessionHost)?.Session;
        if (session != null)
        {
            outbound = session.SubscribeOutbound(message =>
            {
                if (_ws is { State: WebSocketState.Open })
                {
                    _ = SendAsync(message);
                }
            });
        }

        try
        {
            _receiveLoop = ProcessAsync(_ws, _cts.Token);
            await _receiveLoop;
        }
        finally
        {
            outbound?.Dispose();
        }
    }

    /// <summary>
    /// Sends an outbound AuthoringMessage to the connected browser.
    /// </summary>
    public async Task SendAsync(AuthoringMessage message, CancellationToken ct = default)
    {
        if (_ws is not { State: WebSocketState.Open } ws)
            throw new InvalidOperationException("WebSocket is not open.");

        byte[] framed = MessageFramer.Frame(message);
        await ws.SendAsync(framed, WebSocketMessageType.Binary, endOfMessage: true, ct);
    }

    private async Task ProcessAsync(WebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[_receiveBufferSize];

        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;

                do
                {
                    result = await ws.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Acknowledged", CancellationToken.None);
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                byte[] raw = ms.ToArray();
                AuthoringMessage? msg;
                try
                {
                    var (kind, payload) = MessageFramer.Unframe(raw);
                    msg = DeserializeMessage(kind, payload);
                }
                catch (Exception ex)
                {
                    var errorMsg = new ErrorMsg { Code = "parse_error", Detail = ex.Message };
                    await SendAsync(errorMsg, ct);
                    continue;
                }

                if (msg == null) continue;

                // Dispatch to handler, send response if any
                var response = await _handler.HandleAsync(msg, ct);
                if (response != null)
                    await SendAsync(response, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException)
        {
            // Connection dropped
        }
    }

    private static AuthoringMessage? DeserializeMessage(string kind, byte[] payload)
    {
        return kind switch
        {
            "auth.handshake.request" => JsonSerializer.Deserialize<AuthHandshakeRequestMsg>(payload, AuthoringJsonContext.Default),
            "graph.list.request" => JsonSerializer.Deserialize<GraphListRequestMsg>(payload, AuthoringJsonContext.Default),
            "draft.save.request" => JsonSerializer.Deserialize<DraftSaveRequestMsg>(payload, AuthoringJsonContext.Default),
            "draft.discard.request" => JsonSerializer.Deserialize<DraftDiscardRequestMsg>(payload, AuthoringJsonContext.Default),
            "ui.save.request" => JsonSerializer.Deserialize<UiSaveRequestMsg>(payload, AuthoringJsonContext.Default),
            "ui.list.request" => JsonSerializer.Deserialize<UiListRequestMsg>(payload, AuthoringJsonContext.Default),
            "draft.compile.request" => JsonSerializer.Deserialize<DraftCompileRequestMsg>(payload, AuthoringJsonContext.Default),
            "draft.publish.request" => JsonSerializer.Deserialize<DraftPublishRequestMsg>(payload, AuthoringJsonContext.Default),
            "graph.fetch.request" => JsonSerializer.Deserialize<GraphFetchRequestMsg>(payload, AuthoringJsonContext.Default),
            "graph.create.request" => JsonSerializer.Deserialize<GraphCreateRequestMsg>(payload, AuthoringJsonContext.Default),
            "graph.rename.request" => JsonSerializer.Deserialize<GraphRenameRequestMsg>(payload, AuthoringJsonContext.Default),
            "graph.delete.request" => JsonSerializer.Deserialize<GraphDeleteRequestMsg>(payload, AuthoringJsonContext.Default),
            "graph.disable.request" => JsonSerializer.Deserialize<GraphDisableRequestMsg>(payload, AuthoringJsonContext.Default),
            "catalog.query.request" => JsonSerializer.Deserialize<CatalogQueryRequestMsg>(payload, AuthoringJsonContext.Default),
            "binding.materialize.request" => JsonSerializer.Deserialize<BindingMaterializeRequestMsg>(payload, AuthoringJsonContext.Default),
            "connection.validate.request" => JsonSerializer.Deserialize<ConnectionValidateRequestMsg>(payload, AuthoringJsonContext.Default),
            "connection.compatible.request" => JsonSerializer.Deserialize<ConnectionCompatibleRequestMsg>(payload, AuthoringJsonContext.Default),
            "connection.suggest.request" => JsonSerializer.Deserialize<ConnectionSuggestRequestMsg>(payload, AuthoringJsonContext.Default),
            "history.list.request" => JsonSerializer.Deserialize<HistoryListRequestMsg>(payload, AuthoringJsonContext.Default),
            "history.diff.request" => JsonSerializer.Deserialize<HistoryDiffRequestMsg>(payload, AuthoringJsonContext.Default),
            "history.rollback.request" => JsonSerializer.Deserialize<HistoryRollbackRequestMsg>(payload, AuthoringJsonContext.Default),
            "history.lkg.request" => JsonSerializer.Deserialize<HistoryLkgRequestMsg>(payload, AuthoringJsonContext.Default),
            "ui.compile.request" => JsonSerializer.Deserialize<UiCompileRequestMsg>(payload, AuthoringJsonContext.Default),
            "ui.catalog.request" => JsonSerializer.Deserialize<UiCatalogRequestMsg>(payload, AuthoringJsonContext.Default),
            "ui.preview.request" => JsonSerializer.Deserialize<UiPreviewRequestMsg>(payload, AuthoringJsonContext.Default),
            "ui.patch.request" => JsonSerializer.Deserialize<UiPatchRequestMsg>(payload, AuthoringJsonContext.Default),
            "debugger.command.request" => JsonSerializer.Deserialize<DebuggerCommandRequestMsg>(payload, AuthoringJsonContext.Default),
            "debugger.inspect.request" => JsonSerializer.Deserialize<DebuggerInspectRequestMsg>(payload, AuthoringJsonContext.Default),
            "debugger.watch.request" => JsonSerializer.Deserialize<DebuggerWatchRequestMsg>(payload, AuthoringJsonContext.Default),
            "profiler.snapshot.request" => JsonSerializer.Deserialize<ProfilerSnapshotRequestMsg>(payload, AuthoringJsonContext.Default),
            "audit.query.request" => JsonSerializer.Deserialize<AuditQueryRequestMsg>(payload, AuthoringJsonContext.Default),
            "schema.list.request" => JsonSerializer.Deserialize<SchemaListRequestMsg>(payload, AuthoringJsonContext.Default),
            "schema.save.request" => JsonSerializer.Deserialize<SchemaSaveRequestMsg>(payload, AuthoringJsonContext.Default),
            "sandbox.run.request" => JsonSerializer.Deserialize<SandboxRunRequestMsg>(payload, AuthoringJsonContext.Default),
            "draft.rebase.request" => JsonSerializer.Deserialize<DraftRebaseRequestMsg>(payload, AuthoringJsonContext.Default),
            "draft.merge.request" => JsonSerializer.Deserialize<DraftMergeRequestMsg>(payload, AuthoringJsonContext.Default),
            "test.run.request" => JsonSerializer.Deserialize<GraphTestRequestMsg>(payload, AuthoringJsonContext.Default),
            "profiler.subscribe.request" => JsonSerializer.Deserialize<ProfilerSubscribeRequestMsg>(payload, AuthoringJsonContext.Default),
            "runtime.status.request" => JsonSerializer.Deserialize<RuntimeStatusRequestMsg>(payload, AuthoringJsonContext.Default),
            "ping" => JsonSerializer.Deserialize<PingMsg>(payload, AuthoringJsonContext.Default),
            _ => null
        };
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_receiveLoop != null)
        {
            try { await _receiveLoop; }
            catch (OperationCanceledException) { }
        }
        _cts?.Dispose();
    }
}

/// <summary>
/// Handles deserialized authoring messages on the server side.
/// Returns an optional response message to send back to the browser.
/// </summary>
public interface IAuthoringMessageHandler
{
    Task<AuthoringMessage?> HandleAsync(AuthoringMessage message, CancellationToken ct);
}

/// <summary>
/// Null handler that responds to Ping with Pong and ignores everything else.
/// Useful in testing.
/// </summary>
public sealed class PingPongHandler : IAuthoringMessageHandler
{
    public Task<AuthoringMessage?> HandleAsync(AuthoringMessage message, CancellationToken ct)
    {
        AuthoringMessage? response = message is PingMsg ? new PongMsg() : null;
        return Task.FromResult(response);
    }
}
