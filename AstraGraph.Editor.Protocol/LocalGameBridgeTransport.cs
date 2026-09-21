using System.Net.WebSockets;
using System.Text.Json;

namespace AstraGraph.Editor.Protocol;

/// <summary>
/// Transport implementation for the local loopback bridge.
/// Connects to ws://127.0.0.1:{port}/ws?nonce={nonce}, exchanges nonce for session,
/// then proxies authoring messages between the browser and the server via the game session channel.
/// </summary>
public sealed class LocalGameBridgeTransport : IAuthoringTransport
{
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;

    public TransportState State { get; private set; } = TransportState.Disconnected;
    public string? SessionId { get; private set; }
    public bool IsAuthenticated => SessionId != null && State == TransportState.Connected;

    public event Action<string>? OnConnected;
    public event Action<string?>? OnDisconnected;
    public event Action<AuthoringMessage>? OnMessageReceived;
    public event Action<Exception>? OnError;

    public async Task ConnectAsync(Uri endpoint, CancellationToken ct = default)
    {
        if (State is TransportState.Connected or TransportState.Connecting)
            throw new InvalidOperationException($"Already in state {State}.");

        State = TransportState.Connecting;
        _ws = new ClientWebSocket();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            await _ws.ConnectAsync(endpoint, ct);
            State = TransportState.Connected;

            // Wait for handshake response bearing SessionId
            SessionId = await ReceiveHandshakeAsync(_cts.Token);

            _receiveLoop = RunReceiveLoopAsync(_cts.Token);
            OnConnected?.Invoke(SessionId ?? "unknown");
        }
        catch (Exception ex)
        {
            State = TransportState.Faulted;
            OnError?.Invoke(ex);
            throw;
        }
    }

    public async Task SendMessageAsync(AuthoringMessage message, CancellationToken ct = default)
    {
        if (_ws == null || State != TransportState.Connected)
            throw new InvalidOperationException("Transport is not connected.");

        byte[] framed = MessageFramer.Frame(message);
        await _ws.SendAsync(framed, WebSocketMessageType.Binary, endOfMessage: true, ct);
    }

    public async Task DisconnectAsync()
    {
        if (State == TransportState.Disconnected) return;
        State = TransportState.Disconnecting;

        _cts?.Cancel();

        if (_ws?.State == WebSocketState.Open)
        {
            try
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnected", CancellationToken.None);
            }
            catch { /* best-effort */ }
        }

        if (_receiveLoop != null)
        {
            try { await _receiveLoop; }
            catch (OperationCanceledException) { }
        }

        State = TransportState.Disconnected;
        OnDisconnected?.Invoke(null);
    }

    private async Task<string?> ReceiveHandshakeAsync(CancellationToken ct)
    {
        // Expect the first message to be AuthHandshakeResponseMsg bearing SessionId
        var buffer = new byte[64 * 1024];
        var result = await _ws!.ReceiveAsync(buffer, ct);
        if (result.MessageType == WebSocketMessageType.Close)
            throw new InvalidOperationException("Server closed connection during handshake.");

        var payload = buffer[..result.Count];
        var (kind, msgPayload) = MessageFramer.Unframe(payload);

        if (kind == new AuthHandshakeResponseMsg().Kind)
        {
            var resp = JsonSerializer.Deserialize<AuthHandshakeResponseMsg>(msgPayload, AuthoringJsonContext.Default);
            return resp?.SessionId;
        }

        return null;
    }

    private async Task RunReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[256 * 1024];

        try
        {
            while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;

                do
                {
                    result = await _ws.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                byte[] raw = ms.ToArray();
                var (kind, payload) = MessageFramer.Unframe(raw);
                var msg = DeserializeMessage(kind, payload);
                if (msg != null)
                    OnMessageReceived?.Invoke(msg);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex)
        {
            State = TransportState.Faulted;
            OnError?.Invoke(ex);
        }
        finally
        {
            if (State == TransportState.Connected)
            {
                State = TransportState.Disconnected;
                OnDisconnected?.Invoke(null);
            }
        }
    }

    private static AuthoringMessage? DeserializeMessage(string kind, byte[] payload)
    {
        return kind switch
        {
            "auth.handshake.response" => JsonSerializer.Deserialize<AuthHandshakeResponseMsg>(payload, AuthoringJsonContext.Default),
            "graph.list.response" => JsonSerializer.Deserialize<GraphListResponseMsg>(payload, AuthoringJsonContext.Default),
            "draft.compile.response" => JsonSerializer.Deserialize<DraftCompileResponseMsg>(payload, AuthoringJsonContext.Default),
            "draft.publish.response" => JsonSerializer.Deserialize<DraftPublishResponseMsg>(payload, AuthoringJsonContext.Default),
            "debugger.command.response" => JsonSerializer.Deserialize<DebuggerCommandResponseMsg>(payload, AuthoringJsonContext.Default),
            "debug.stream.event" => JsonSerializer.Deserialize<DebugStreamEventMsg>(payload, AuthoringJsonContext.Default),
            "profiler.stream.event" => JsonSerializer.Deserialize<ProfilerStreamEventMsg>(payload, AuthoringJsonContext.Default),
            "ping" => JsonSerializer.Deserialize<PingMsg>(payload, AuthoringJsonContext.Default),
            "pong" => JsonSerializer.Deserialize<PongMsg>(payload, AuthoringJsonContext.Default),
            "error" => JsonSerializer.Deserialize<ErrorMsg>(payload, AuthoringJsonContext.Default),
            _ => null
        };
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _ws?.Dispose();
        _cts?.Dispose();
    }
}
