using System.Net;
using System.Net.WebSockets;
using System.Text;

namespace AstraGraph.Editor.Bridge;

/// <summary>
/// State of the loopback HTTP server.
/// </summary>
public enum LoopbackServerState { Stopped, Starting, Running, Stopping }

/// <summary>
/// Lightweight loopback-only HTTP + WebSocket server for the Astra Local Bridge.
/// Binds exclusively to 127.0.0.1 on a random ephemeral port.
/// </summary>
public sealed class LoopbackHttpServer : IAsyncDisposable
{
    private readonly BridgeSecurityPolicy _security;
    private readonly Func<HttpBridgeRequest, CancellationToken, Task<HttpBridgeResponse>> _httpHandler;
    private readonly Func<WebSocketBridgeContext, CancellationToken, Task> _webSocketHandler;

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public int Port { get; private set; }
    public LoopbackServerState State { get; private set; } = LoopbackServerState.Stopped;

    public LoopbackHttpServer(
        BridgeSecurityPolicy security,
        Func<HttpBridgeRequest, CancellationToken, Task<HttpBridgeResponse>> httpHandler,
        Func<WebSocketBridgeContext, CancellationToken, Task> webSocketHandler)
    {
        _security = security;
        _httpHandler = httpHandler;
        _webSocketHandler = webSocketHandler;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (State != LoopbackServerState.Stopped)
            throw new InvalidOperationException($"Cannot start: current state is {State}.");

        State = LoopbackServerState.Starting;

        // Find a free port
        Port = FindFreePort();

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        State = LoopbackServerState.Running;

        _acceptLoop = RunAcceptLoopAsync(_cts.Token);
        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (State != LoopbackServerState.Running)
            return;

        State = LoopbackServerState.Stopping;
        _cts?.Cancel();
        _listener?.Stop();

        if (_acceptLoop != null)
        {
            try { await _acceptLoop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        State = LoopbackServerState.Stopped;
    }

    private async Task RunAcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener!.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) { break; }

            // Fire-and-forget each connection so we don't block accepts
            _ = HandleContextAsync(ctx, ct);
        }
    }

    private async Task HandleContextAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var remoteEp = ctx.Request.RemoteEndPoint;
        var originCheck = BridgeSecurityPolicy.ValidateRemoteEndPoint(remoteEp);
        if (!originCheck.IsAllowed)
        {
            ctx.Response.StatusCode = 403;
            ctx.Response.Close();
            return;
        }

        string? originHeader = ctx.Request.Headers["Origin"];
        var originResult = _security.ValidateOrigin(originHeader, Port);
        if (!originResult.IsAllowed)
        {
            ctx.Response.StatusCode = 403;
            await WriteResponseAsync(ctx.Response, HttpBridgeResponse.Forbidden(originResult.RejectionReason ?? "Forbidden"), ct);
            return;
        }

        if (ctx.Request.IsWebSocketRequest)
        {
            await HandleWebSocketAsync(ctx, ct);
        }
        else
        {
            await HandleHttpAsync(ctx, ct);
        }
    }

    private async Task HandleHttpAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var req = await BuildRequestAsync(ctx.Request, ct);
        HttpBridgeResponse response;
        try
        {
            response = await _httpHandler(req, ct);
        }
        catch (Exception ex)
        {
            response = new HttpBridgeResponse
            {
                StatusCode = 500,
                StatusDescription = "Internal Server Error",
                ContentType = "text/plain; charset=utf-8",
                Content = Encoding.UTF8.GetBytes(ex.Message)
            };
        }

        await WriteResponseAsync(ctx.Response, response, ct);
    }

    private async Task HandleWebSocketAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        HttpListenerWebSocketContext wsCtx;
        try
        {
            wsCtx = await ctx.AcceptWebSocketAsync(subProtocol: null).ConfigureAwait(false);
        }
        catch
        {
            ctx.Response.StatusCode = 400;
            ctx.Response.Close();
            return;
        }

        var req = await BuildRequestAsync(ctx.Request, ct);
        var bridgeCtx = new WebSocketBridgeContext
        {
            WebSocket = wsCtx.WebSocket,
            HandshakeRequest = req,
            RemoteEndPoint = ctx.Request.RemoteEndPoint
        };

        try
        {
            await _webSocketHandler(bridgeCtx, ct);
        }
        finally
        {
            if (wsCtx.WebSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await wsCtx.WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Session ended", CancellationToken.None);
                }
                catch { /* best-effort */ }
            }
        }
    }

    private static async Task<HttpBridgeRequest> BuildRequestAsync(HttpListenerRequest req, CancellationToken ct)
    {
        byte[] body = Array.Empty<byte>();
        if (req.HasEntityBody)
        {
            using var ms = new MemoryStream();
            await req.InputStream.CopyToAsync(ms, ct);
            body = ms.ToArray();
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string? key in req.Headers.AllKeys ?? Array.Empty<string>())
        {
            if (key != null)
                headers[key] = req.Headers[key] ?? string.Empty;
        }


        return new HttpBridgeRequest
        {
            Method = req.HttpMethod,
            Path = req.Url?.AbsolutePath ?? "/",
            QueryString = req.Url?.Query ?? string.Empty,
            Headers = headers,
            RemoteEndPoint = req.RemoteEndPoint,
            Body = body
        };
    }

    private static async Task WriteResponseAsync(HttpListenerResponse resp, HttpBridgeResponse response, CancellationToken ct)
    {
        try
        {
            resp.StatusCode = response.StatusCode;
            resp.StatusDescription = response.StatusDescription;
            resp.ContentType = response.ContentType;
            foreach (var (k, v) in response.Headers)
                resp.Headers[k] = v;

            resp.ContentLength64 = response.Content.Length;
            if (response.Content.Length > 0)
                await resp.OutputStream.WriteAsync(response.Content, ct);
        }
        finally
        {
            resp.Close();
        }
    }

    private static int FindFreePort()
    {
        using var tempListener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        tempListener.Start();
        int port = ((IPEndPoint)tempListener.LocalEndpoint).Port;
        tempListener.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _listener?.Close();
        _cts?.Dispose();
    }
}
