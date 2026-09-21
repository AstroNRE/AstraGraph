using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

namespace AstraGraph.Editor.Bridge;

/// <summary>
/// Status of the Astra Local Bridge for in-game status display.
/// </summary>
public sealed record BridgeStatusInfo(
    bool IsRunning,
    int Port,
    int ActiveWebSocketConnections,
    DateTimeOffset? StartedAt);

/// <summary>
/// The top-level Astra Local Bridge that ties together the loopback HTTP server,
/// nonce management, security policy, web asset serving and WebSocket session handling.
/// </summary>
public sealed class AstraLocalBridge : IAsyncDisposable
{
    private readonly SessionNonceManager _nonceManager;
    private readonly BridgeSecurityPolicy _security;
    private readonly IWebAssetProvider _assetProvider;
    private readonly Func<WebSocketBridgeContext, CancellationToken, Task> _sessionHandler;

    private LoopbackHttpServer? _server;
    private DateTimeOffset? _startedAt;
    private readonly ConcurrentDictionary<string, byte> _activeConnections = new();

    private static readonly System.Text.Json.JsonSerializerOptions _jsonOptions =
        new() { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };

    public bool IsRunning => _server?.State == LoopbackServerState.Running;
    public int Port => _server?.Port ?? 0;

    public AstraLocalBridge(
        SessionNonceManager nonceManager,
        BridgeSecurityPolicy security,
        IWebAssetProvider assetProvider,
        Func<WebSocketBridgeContext, CancellationToken, Task> sessionHandler)
    {
        _nonceManager = nonceManager;
        _security = security;
        _assetProvider = assetProvider;
        _sessionHandler = sessionHandler;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_server != null)
            throw new InvalidOperationException("Bridge is already running.");

        _server = new LoopbackHttpServer(_security, HandleHttpRequestAsync, HandleWebSocketAsync);
        await _server.StartAsync(ct);
        _startedAt = DateTimeOffset.UtcNow;
    }

    public async Task StopAsync()
    {
        if (_server == null) return;
        await _server.StopAsync();
        _nonceManager.Clear();
        _server = null;
        _startedAt = null;
    }

    /// <summary>
    /// Creates a new single-use nonce and returns the launch URL for Astra Studio.
    /// </summary>
    public string CreateLaunchUrl(StudioLaunchContext? context = null, TimeSpan? nonceTtl = null)
    {
        if (!IsRunning)
            throw new InvalidOperationException("Bridge must be running before creating a launch URL.");

        var nonceInfo = _nonceManager.CreateNonce(nonceTtl);
        return BrowserLauncher.BuildStudioUrl(Port, nonceInfo.Nonce, context);
    }

    /// <summary>
    /// Opens Astra Studio in the default system browser with a freshly generated nonce.
    /// </summary>
    public void OpenInBrowser(StudioLaunchContext? context = null)
    {
        string url = CreateLaunchUrl(context);
        BrowserLauncher.OpenUrl(url);
    }

    public BridgeStatusInfo GetStatus() => new(
        IsRunning: IsRunning,
        Port: Port,
        ActiveWebSocketConnections: _activeConnections.Count,
        StartedAt: _startedAt);

    // ─── HTTP Request Handling ──────────────────────────────────────────────

    private async Task<HttpBridgeResponse> HandleHttpRequestAsync(HttpBridgeRequest req, CancellationToken ct)
    {
        // Normalize path
        string path = req.Path.TrimEnd('/');
        if (string.IsNullOrEmpty(path))
            path = "/index.html";

        // Try to serve static asset
        var asset = _assetProvider.TryGetAsset(path);
        if (asset != null)
        {
            var resp = new HttpBridgeResponse
            {
                StatusCode = 200,
                StatusDescription = "OK",
                ContentType = asset.ContentType,
                Content = asset.Content
            };
            if (!string.IsNullOrEmpty(asset.ETag))
            {
                resp.Headers["ETag"] = asset.ETag;
                resp.Headers["Cache-Control"] = "no-cache";
            }
            return resp;
        }

        // API endpoint: bridge status
        if (path == "/api/status" && req.Method == "GET")
        {
            var status = GetStatus();
            string json = JsonSerializer.Serialize(status, _jsonOptions);
            return HttpBridgeResponse.Json(json);
        }

        return HttpBridgeResponse.NotFound($"Resource '{path}' not found.");
    }

    // ─── WebSocket Handling ─────────────────────────────────────────────────

    private async Task HandleWebSocketAsync(WebSocketBridgeContext ctx, CancellationToken ct)
    {
        // Expect nonce in query string: /ws?nonce=<value>
        string? nonce = ctx.HandshakeRequest.QueryParameters["nonce"];

        if (!_nonceManager.TryRedeemNonce(nonce, out var nonceInfo))
        {
            await ctx.WebSocket.CloseAsync(
                WebSocketCloseStatus.PolicyViolation,
                "Invalid or expired nonce.",
                CancellationToken.None);
            return;
        }

        ctx.RedeemedNonce = nonceInfo;

        string connectionId = Guid.NewGuid().ToString("N");
        _activeConnections.TryAdd(connectionId, 0);

        try
        {
            await _sessionHandler(ctx, ct);
        }
        finally
        {
            _activeConnections.TryRemove(connectionId, out _);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_server != null)
            await _server.DisposeAsync();
    }
}

/// <summary>
/// Provides static web assets to be served by the loopback HTTP server.
/// </summary>
public interface IWebAssetProvider
{
    WebAsset? TryGetAsset(string path);
}

/// <summary>
/// A single web asset (HTML, JS, CSS, etc.) served by the bridge.
/// </summary>
public sealed record WebAsset(
    string Path,
    string ContentType,
    byte[] Content,
    string? ETag = null);
