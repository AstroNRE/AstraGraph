using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AstraGraph.Core;
using AstraGraph.Editor.Bridge;
using AstraGraph.Editor.Protocol;
using AstraGraph.HotReload;
using AstraGraph.VM;

namespace AstraGraph.Studio.DevHost;

public sealed class AstraStudioDevServer : IAsyncDisposable
{
    public const string Version = "1.0.0";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AstraLocalBridge _bridge;
    private readonly SessionNonceManager _nonces;
    private readonly DevHostOriginPolicy _origins;
    private readonly Action<string> _log;
    private readonly StandaloneDevSession? _standalone;
    private readonly bool _loopback;
    private readonly TaskCompletionSource _shutdown = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private AstraStudioDevServer(
        DevHostOptions options,
        string token,
        bool loopback,
        AstraLocalBridge bridge,
        SessionNonceManager nonces,
        DevHostOriginPolicy origins,
        StandaloneDevSession? standalone,
        IAstraDevHostBackend? backend,
        Action<string> log)
    {
        Options = options;
        Token = token;
        _loopback = loopback;
        _bridge = bridge;
        _nonces = nonces;
        _origins = origins;
        _standalone = standalone;
        Backend = backend;
        _log = log;
    }

    public DevHostOptions Options { get; }
    public string Token { get; }
    public IAstraDevHostBackend? Backend { get; }
    public AstraGraphFacade? Runtime => _standalone?.Facade;
    public int Port => _bridge.Port;
    public bool IsRunning => _bridge.IsRunning;

    public static async Task<AstraStudioDevServer> StartAsync(
        DevHostOptions options,
        IAstraDevHostBackend? backend = null,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        log ??= Console.WriteLine;
        var listen = ParseListen(options.ListenAddress);
        var loopback = IPAddress.IsLoopback(listen);
        if (!loopback && string.IsNullOrWhiteSpace(options.Token))
        {
            throw new InvalidOperationException("Startup aborted\nExternal Astra Studio binding requires authentication.");
        }

        if (options.Mode == DevHostMode.Attached && backend == null)
        {
            throw new InvalidOperationException("Attached mode requires IAstraDevHostBackend.");
        }

        var token = string.IsNullOrWhiteSpace(options.Token) ? DevAuthenticationService.CreateToken() : options.Token;
        StandaloneDevSession? standalone = null;
        IAuthoringMessageHandler? handler = null;
        if (options.Mode == DevHostMode.Standalone)
        {
            standalone = DevHostSessionFactory.CreateStandalone(options, token, log);
            handler = standalone.Handler;
        }
        else if (options.Mode == DevHostMode.Attached)
        {
            handler = backend!.AuthoringHandler;
        }

        var nonces = new SessionNonceManager();
        var origins = new DevHostOriginPolicy(options.AllowedOrigins);
        var assets = new DirectoryWebAssetProvider(ResolveAssets(options));
        AstraStudioDevServer? server = null;
        var bridge = new AstraLocalBridge(
            nonces,
            new BridgeSecurityPolicy(),
            assets,
            (ctx, socketCt) => server!.HandleSocketAsync(ctx, socketCt))
        {
            Routes = (req, routeCt) => server!.HandleRoutesAsync(req, routeCt)
        };
        server = new AstraStudioDevServer(options, token, loopback, bridge, nonces, origins, standalone, backend, log);
        await bridge.StartBoundAsync(listen, options.Port, loopbackClientsOnly: loopback, origins.Validate, ct);

        var address = loopback ? "127.0.0.1" : options.ListenAddress;
        log("[Studio] Listening on http://" + address + ":" + server.Port);
        log("Astra Studio DevHost");
        log("Mode: " + options.Mode);
        log("Address: http://" + address + ":" + server.Port);
        log("Token: ********");
        var launch = server.CreateLaunchUrl();
        log(launch);
        if (server.ShouldOpenBrowser)
        {
            try
            {
                BrowserLauncher.OpenUrl(launch);
            }
            catch (BrowserLaunchException ex)
            {
                log("[Studio] Browser was not opened: " + (ex.InnerException?.Message ?? ex.Message));
            }
        }

        return server;
    }

    public string CreateLaunchUrl()
    {
        if (Options.Mode == DevHostMode.Preview)
        {
            return $"http://127.0.0.1:{Port}/";
        }

        return _bridge.CreateLaunchUrl();
    }

    public string IssueNonce() => _nonces.CreateNonce().Nonce;

    public Task WaitForShutdownAsync() => _shutdown.Task;

    public void RequestShutdown() => _shutdown.TrySetResult();

    public VmExecutionResult? RunPublishedGraph(GraphId graphId)
    {
        var host = Runtime?.Host ?? throw new InvalidOperationException("Standalone runtime is not running.");
        var program = host.GetProgram(graphId);
        var entry = program?.EntryPoints.FirstOrDefault();
        if (program == null || entry == null)
        {
            return null;
        }

        return host.Vm.Execute(program, entry, hostServices: host.HostServices, debugHook: host.Debugger);
    }

    public DevHostStatus GetStatus() => new(
        Mode: Options.Mode.ToString().ToLowerInvariant(),
        RuntimeConnected: Options.Mode switch
        {
            DevHostMode.Standalone => true,
            DevHostMode.Attached => Backend?.Info.RuntimeConnected ?? false,
            _ => false
        },
        AuthoringAvailable: Options.Mode != DevHostMode.Preview,
        ActiveConnections: _bridge.GetStatus().ActiveWebSocketConnections,
        Version: Version,
        Engine: Options.Mode switch
        {
            DevHostMode.Standalone => "Standalone",
            DevHostMode.Attached => Backend?.Info.Engine ?? "Attached",
            _ => "None"
        },
        CompatibilityProfile: Options.Mode == DevHostMode.Attached ? Backend?.Info.CompatibilityProfile : null);

    public async Task StopAsync()
    {
        if (_bridge.IsRunning)
        {
            await _bridge.StopAsync();
        }

        _standalone?.Layout.CleanOrphanTempFiles();
        _standalone?.Facade.Shutdown();
        RequestShutdown();
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private bool ShouldOpenBrowser => Options.OpenBrowser ?? _loopback;

    private async Task HandleSocketAsync(WebSocketBridgeContext ctx, CancellationToken ct)
    {
        _log("[Studio] Client connected");
        var handler = Options.Mode switch
        {
            DevHostMode.Standalone => _standalone?.Handler,
            DevHostMode.Attached => Backend?.AuthoringHandler,
            _ => null
        };
        if (handler == null)
        {
            await ctx.WebSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Preview Mode", CancellationToken.None);
            return;
        }

        var proxy = new BridgeWebSocketProxy(handler);
        await proxy.RunAsync(ctx.WebSocket, ct);
    }

    private Task<HttpBridgeResponse?> HandleRoutesAsync(HttpBridgeRequest req, CancellationToken ct)
    {
        var path = req.Path.TrimEnd('/');
        if (string.IsNullOrEmpty(path))
        {
            path = "/";
        }

        if (req.Method == "POST" && path is "/api/publish" or "/api/compile")
        {
            return Task.FromResult<HttpBridgeResponse?>(Text(405, "Method Not Allowed", "Authoring operations use the WebSocket protocol."));
        }

        if (req.Method == "GET" && path == "/health")
        {
            return Task.FromResult<HttpBridgeResponse?>(HttpBridgeResponse.Ok("OK"));
        }

        if (req.Method == "GET" && path == "/api/status")
        {
            return Task.FromResult<HttpBridgeResponse?>(HttpBridgeResponse.Json(JsonSerializer.Serialize(GetStatus(), JsonOptions)));
        }

        if (req.Method == "GET" && path == "/api/config")
        {
            var config = new DevHostClientConfig(
                Options.Mode.ToString().ToLowerInvariant(),
                Options.Mode != DevHostMode.Preview,
                TargetLabel());
            return Task.FromResult<HttpBridgeResponse?>(HttpBridgeResponse.Json(JsonSerializer.Serialize(config, JsonOptions)));
        }

        if (req.Method == "GET" && path == "/api/session")
        {
            if (!CanMintNonce(req))
            {
                return Task.FromResult<HttpBridgeResponse?>(Text(401, "Unauthorized", "Authentication required."));
            }

            if (Options.Mode == DevHostMode.Preview)
            {
                return Task.FromResult<HttpBridgeResponse?>(HttpBridgeResponse.NotFound("Preview mode has no authoring session."));
            }

            var nonce = _nonces.CreateNonce().Nonce;
            return Task.FromResult<HttpBridgeResponse?>(HttpBridgeResponse.Json(JsonSerializer.Serialize(new { nonce }, JsonOptions)));
        }

        return Task.FromResult<HttpBridgeResponse?>(null);
    }

    private bool CanMintNonce(HttpBridgeRequest req)
    {
        if (_loopback)
        {
            return true;
        }

        if (PresentedToken(req) is { } presented && DevAuthenticationService.TokenMatches(Token, presented))
        {
            return true;
        }

        if (!req.Headers.TryGetValue("Origin", out var origin) || string.IsNullOrWhiteSpace(origin))
        {
            return false;
        }

        return _origins.Validate(origin, Port).IsAllowed;
    }

    private static string? PresentedToken(HttpBridgeRequest req)
    {
        if (req.Headers.TryGetValue("Authorization", out var authorization)
            && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authorization["Bearer ".Length..].Trim();
        }

        if (req.Headers.TryGetValue("X-Astra-Token", out var header) && !string.IsNullOrWhiteSpace(header))
        {
            return header.Trim();
        }

        return req.QueryParameters["token"];
    }

    private string TargetLabel() => Options.Mode switch
    {
        DevHostMode.Standalone => "Runtime: Standalone AstraGraph / Engine: None",
        DevHostMode.Attached => Backend?.Info.TargetName ?? "Target: Attached",
        _ => "Target: Preview"
    };

    private static HttpBridgeResponse Text(int status, string description, string body) => new()
    {
        StatusCode = status,
        StatusDescription = description,
        ContentType = "text/plain; charset=utf-8",
        Content = Encoding.UTF8.GetBytes(body)
    };

    private static IPAddress ParseListen(string address) => address.Trim() switch
    {
        "0.0.0.0" or "*" or "+" => IPAddress.Any,
        "localhost" or "127.0.0.1" => IPAddress.Loopback,
        var other => IPAddress.Parse(other)
    };

    private static string ResolveAssets(DevHostOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.StudioAssetsPath))
        {
            return options.StudioAssetsPath;
        }

        return EmbeddedWebAssetProvider.FindStudioWebRoot()
            ?? throw new InvalidOperationException("AstraGraph.StudioWeb/wwwroot was not found.");
    }
}
