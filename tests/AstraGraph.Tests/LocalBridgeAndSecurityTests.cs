using System.Net;
using System.Net.WebSockets;
using System.Text;
using AstraGraph.Editor.Bridge;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
[Category("Bridge.W1")]
public sealed class LocalBridgeAndSecurityTests
{
    // ─── SessionNonceManager ───────────────────────────────────────────────

    [Test]
    public void SessionNonceManager_CreateNonce_IsHexString_32Bytes()
    {
        var mgr = new SessionNonceManager();
        var info = mgr.CreateNonce();

        Assert.That(info.Nonce, Has.Length.EqualTo(64)); // 32 bytes * 2 hex chars
        Assert.That(info.Nonce, Does.Match("^[0-9a-f]+$"));
    }

    [Test]
    public void SessionNonceManager_CreateNonce_IsUnique()
    {
        var mgr = new SessionNonceManager();
        var nonces = Enumerable.Range(0, 100).Select(_ => mgr.CreateNonce().Nonce).ToHashSet();
        Assert.That(nonces, Has.Count.EqualTo(100));
    }

    [Test]
    public void SessionNonceManager_TryRedeem_SucceedsOnce()
    {
        var mgr = new SessionNonceManager();
        var info = mgr.CreateNonce();

        bool first = mgr.TryRedeemNonce(info.Nonce, out var redeemed);
        bool second = mgr.TryRedeemNonce(info.Nonce, out _);

        Assert.That(first, Is.True);
        Assert.That(redeemed?.Nonce, Is.EqualTo(info.Nonce));
        Assert.That(second, Is.False, "Second redeem must fail (one-time use).");
    }

    [Test]
    public void SessionNonceManager_TryRedeem_FailsForUnknownNonce()
    {
        var mgr = new SessionNonceManager();
        bool result = mgr.TryRedeemNonce("deadbeefdeadbeef", out _);
        Assert.That(result, Is.False);
    }

    [Test]
    public void SessionNonceManager_TryRedeem_FailsForNullOrEmpty()
    {
        var mgr = new SessionNonceManager();
        Assert.That(mgr.TryRedeemNonce(null, out _), Is.False);
        Assert.That(mgr.TryRedeemNonce("", out _), Is.False);
        Assert.That(mgr.TryRedeemNonce("   ", out _), Is.False);
    }

    [Test]
    public async Task SessionNonceManager_TryRedeem_FailsAfterExpiry()
    {
        var mgr = new SessionNonceManager(defaultTtl: TimeSpan.FromMilliseconds(50));
        var info = mgr.CreateNonce();

        await Task.Delay(100); // Let it expire

        bool result = mgr.TryRedeemNonce(info.Nonce, out _);
        Assert.That(result, Is.False, "Expired nonce must not be redeemable.");
    }

    [Test]
    public void SessionNonceManager_CleanupExpired_RemovesExpired()
    {
        var mgr = new SessionNonceManager(defaultTtl: TimeSpan.FromMilliseconds(1));
        for (int i = 0; i < 5; i++)
            mgr.CreateNonce(TimeSpan.FromMilliseconds(1));

        // Keep one fresh
        mgr.CreateNonce(TimeSpan.FromSeconds(60));

        Thread.Sleep(20);
        int removed = mgr.CleanupExpired();

        Assert.That(removed, Is.EqualTo(5));
        Assert.That(mgr.ActiveNonceCount, Is.EqualTo(1));
    }

    [Test]
    public void SessionNonceManager_Clear_RemovesAll()
    {
        var mgr = new SessionNonceManager();
        mgr.CreateNonce();
        mgr.CreateNonce();
        mgr.Clear();
        Assert.That(mgr.ActiveNonceCount, Is.EqualTo(0));
    }

    // ─── BridgeSecurityPolicy – Origin ────────────────────────────────────

    [Test]
    public void BridgeSecurityPolicy_ValidOrigin_LoopbackPort_IsAllowed()
    {
        var policy = new BridgeSecurityPolicy();
        var result = policy.ValidateOrigin("http://127.0.0.1:54321", 54321);
        Assert.That(result.IsAllowed, Is.True);
    }

    [Test]
    public void BridgeSecurityPolicy_ValidOrigin_Localhost_IsAllowed()
    {
        var policy = new BridgeSecurityPolicy();
        var result = policy.ValidateOrigin("http://localhost:54321", 54321);
        Assert.That(result.IsAllowed, Is.True);
    }

    [Test]
    public void BridgeSecurityPolicy_InvalidOrigin_WrongPort_IsDenied()
    {
        var policy = new BridgeSecurityPolicy();
        var result = policy.ValidateOrigin("http://127.0.0.1:9999", 54321);
        Assert.That(result.IsAllowed, Is.False);
    }

    [Test]
    public void BridgeSecurityPolicy_InvalidOrigin_ExternalDomain_IsDenied()
    {
        var policy = new BridgeSecurityPolicy();
        var result = policy.ValidateOrigin("http://evil.example.com", 54321);
        Assert.That(result.IsAllowed, Is.False);
    }

    [Test]
    public void BridgeSecurityPolicy_NullOrigin_IsAllowed()
    {
        // Null origin is allowed (non-browser tools, e.g. curl)
        var policy = new BridgeSecurityPolicy();
        var result = policy.ValidateOrigin(null, 54321);
        Assert.That(result.IsAllowed, Is.True);
    }

    [Test]
    public void BridgeSecurityPolicy_CustomAllowedOrigin_IsAllowed()
    {
        var policy = new BridgeSecurityPolicy(new[] { "http://custom.local:8080" });
        Assert.That(policy.ValidateOrigin("http://custom.local:8080", 9000).IsAllowed, Is.True);
    }

    // ─── BridgeSecurityPolicy – RemoteEndPoint ────────────────────────────

    [Test]
    public void BridgeSecurityPolicy_LoopbackEndPoint_IsAllowed()
    {
        var ep = new IPEndPoint(IPAddress.Loopback, 12345);
        Assert.That(BridgeSecurityPolicy.ValidateRemoteEndPoint(ep).IsAllowed, Is.True);
    }

    [Test]
    public void BridgeSecurityPolicy_IPv6Loopback_IsAllowed()
    {
        var ep = new IPEndPoint(IPAddress.IPv6Loopback, 12345);
        Assert.That(BridgeSecurityPolicy.ValidateRemoteEndPoint(ep).IsAllowed, Is.True);
    }

    [Test]
    public void BridgeSecurityPolicy_ExternalAddress_IsDenied()
    {
        var ep = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 12345);
        Assert.That(BridgeSecurityPolicy.ValidateRemoteEndPoint(ep).IsAllowed, Is.False);
    }

    [Test]
    public void BridgeSecurityPolicy_NullEndPoint_IsDenied()
    {
        Assert.That(BridgeSecurityPolicy.ValidateRemoteEndPoint(null).IsAllowed, Is.False);
    }


    // ─── BridgeSecurityPolicy – Token Comparison ──────────────────────────

    [Test]
    public void BridgeSecurityPolicy_ValidateToken_ConstantTime_MatchReturnsTrue()
    {
        Assert.That(BridgeSecurityPolicy.ValidateTokenConstantTime("abc123", "abc123"), Is.True);
    }

    [Test]
    public void BridgeSecurityPolicy_ValidateToken_Mismatch_ReturnsFalse()
    {
        Assert.That(BridgeSecurityPolicy.ValidateTokenConstantTime("abc123", "abc124"), Is.False);
    }

    [Test]
    public void BridgeSecurityPolicy_ValidateToken_NullOrEmpty_ReturnsFalse()
    {
        Assert.That(BridgeSecurityPolicy.ValidateTokenConstantTime(null, "abc"), Is.False);
        Assert.That(BridgeSecurityPolicy.ValidateTokenConstantTime("abc", null), Is.False);
        Assert.That(BridgeSecurityPolicy.ValidateTokenConstantTime("", ""), Is.False);
    }

    // ─── BrowserLauncher – URL Building ───────────────────────────────────

    [Test]
    public void BrowserLauncher_BuildStudioUrl_ContainsNonce()
    {
        string url = BrowserLauncher.BuildStudioUrl(54321, "abc123nonce");
        Assert.That(url, Does.StartWith("http://127.0.0.1:54321/?nonce=abc123nonce"));
    }

    [Test]
    public void BrowserLauncher_BuildStudioUrl_WithContext_ContainsDeepLinkParams()
    {
        var ctx = new StudioLaunchContext(EntityUid: "ent-42", GraphId: "graph-weapon", Action: "inspect");
        string url = BrowserLauncher.BuildStudioUrl(54321, "mynonce", ctx);

        Assert.That(url, Does.Contain("entity=ent-42"));
        Assert.That(url, Does.Contain("graph=graph-weapon"));
        Assert.That(url, Does.Contain("action=inspect"));
    }

    [Test]
    public void BrowserLauncher_BuildStudioUrl_EscapesSpecialChars()
    {
        var ctx = new StudioLaunchContext(EntityUid: "uid with spaces&special=chars");
        string url = BrowserLauncher.BuildStudioUrl(1234, "nonce", ctx);
        Assert.That(url, Does.Not.Contain(" "));
        Assert.That(url, Does.Not.Contain("&special=chars"));  // must be escaped
    }

    [Test]
    public void BrowserLauncher_BuildStudioUrl_InvalidPort_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BrowserLauncher.BuildStudioUrl(0, "nonce"));
        Assert.Throws<ArgumentOutOfRangeException>(() => BrowserLauncher.BuildStudioUrl(-1, "nonce"));
    }

    [Test]
    public void BrowserLauncher_BuildStudioUrl_EmptyNonce_Throws()
    {
        Assert.Throws<ArgumentException>(() => BrowserLauncher.BuildStudioUrl(1234, ""));
        Assert.Throws<ArgumentException>(() => BrowserLauncher.BuildStudioUrl(1234, "   "));
    }

    // ─── LoopbackHttpServer – Integration ─────────────────────────────────

    [Test]
    public async Task LoopbackHttpServer_Start_BindsToLoopback()
    {
        var security = new BridgeSecurityPolicy();
        static Task<HttpBridgeResponse> Handler(HttpBridgeRequest r, CancellationToken _)
            => Task.FromResult(HttpBridgeResponse.Ok("hello"));
        static Task WsHandler(WebSocketBridgeContext c, CancellationToken _) => Task.CompletedTask;

        await using var server = new LoopbackHttpServer(security, Handler, WsHandler);
        await server.StartAsync();

        Assert.That(server.Port, Is.GreaterThan(0));
        Assert.That(server.Port, Is.LessThanOrEqualTo(65535));
        Assert.That(server.State, Is.EqualTo(LoopbackServerState.Running));
    }

    [Test]
    public async Task LoopbackHttpServer_Responds_ToHttpGet()
    {
        var security = new BridgeSecurityPolicy();
        static Task<HttpBridgeResponse> Handler(HttpBridgeRequest r, CancellationToken _)
            => Task.FromResult(HttpBridgeResponse.Ok("pong"));
        static Task WsHandler(WebSocketBridgeContext c, CancellationToken _) => Task.CompletedTask;

        await using var server = new LoopbackHttpServer(security, Handler, WsHandler);
        await server.StartAsync();

        using var http = new HttpClient();
        var response = await http.GetAsync($"http://127.0.0.1:{server.Port}/test");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That((int)response.StatusCode, Is.EqualTo(200));
        Assert.That(body, Is.EqualTo("pong"));
    }

    [Test]
    public async Task LoopbackHttpServer_Returns404_ForMissingPath()
    {
        var security = new BridgeSecurityPolicy();
        static Task<HttpBridgeResponse> Handler(HttpBridgeRequest r, CancellationToken _)
            => Task.FromResult(HttpBridgeResponse.NotFound());
        static Task WsHandler(WebSocketBridgeContext c, CancellationToken _) => Task.CompletedTask;

        await using var server = new LoopbackHttpServer(security, Handler, WsHandler);
        await server.StartAsync();

        using var http = new HttpClient();
        var response = await http.GetAsync($"http://127.0.0.1:{server.Port}/missing");
        Assert.That((int)response.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task LoopbackHttpServer_Stop_TransitionsToStopped()
    {
        var security = new BridgeSecurityPolicy();
        static Task<HttpBridgeResponse> Handler(HttpBridgeRequest r, CancellationToken _)
            => Task.FromResult(HttpBridgeResponse.Ok("ok"));
        static Task WsHandler(WebSocketBridgeContext c, CancellationToken _) => Task.CompletedTask;

        await using var server = new LoopbackHttpServer(security, Handler, WsHandler);
        await server.StartAsync();
        await server.StopAsync();

        Assert.That(server.State, Is.EqualTo(LoopbackServerState.Stopped));
    }

    [Test]
    public async Task LoopbackHttpServer_TwoDifferentInstances_GetDifferentPorts()
    {
        var security = new BridgeSecurityPolicy();
        static Task<HttpBridgeResponse> Handler(HttpBridgeRequest r, CancellationToken _)
            => Task.FromResult(HttpBridgeResponse.Ok("ok"));
        static Task WsHandler(WebSocketBridgeContext c, CancellationToken _) => Task.CompletedTask;

        await using var s1 = new LoopbackHttpServer(security, Handler, WsHandler);
        await using var s2 = new LoopbackHttpServer(security, Handler, WsHandler);

        await s1.StartAsync();
        await s2.StartAsync();

        Assert.That(s1.Port, Is.Not.EqualTo(s2.Port));
    }

    // ─── AstraLocalBridge – Integration ───────────────────────────────────

#pragma warning disable CA1859
    private static IWebAssetProvider EmptyAssets() => new EmptyAssetProvider();
#pragma warning restore CA1859

    [Test]
    public async Task AstraLocalBridge_CreateLaunchUrl_ContainsPortAndNonce()
    {
        var nonceMgr = new SessionNonceManager();
        var security = new BridgeSecurityPolicy();

        await using var bridge = new AstraLocalBridge(
            nonceMgr, security, EmptyAssets(),
            (ctx, ct) => Task.CompletedTask);

        await bridge.StartAsync();
        string url = bridge.CreateLaunchUrl();

        Assert.That(url, Does.StartWith($"http://127.0.0.1:{bridge.Port}/?nonce="));
        Assert.That(bridge.IsRunning, Is.True);
    }

    [Test]
    public async Task AstraLocalBridge_WebSocket_RedeemsNonceOnConnect()
    {
        var nonceMgr = new SessionNonceManager();
        var security = new BridgeSecurityPolicy();
        WebSocketBridgeContext? capturedCtx = null;

        await using var bridge = new AstraLocalBridge(
            nonceMgr, security, EmptyAssets(),
            (ctx, ct) =>
            {
                capturedCtx = ctx;
                return Task.CompletedTask;
            });

        await bridge.StartAsync();
        string url = bridge.CreateLaunchUrl();

        // Extract nonce from URL
        var uri = new Uri(url);
        string? nonce = System.Web.HttpUtility.ParseQueryString(uri.Query)["nonce"];

        using var wsClient = new ClientWebSocket();
        wsClient.Options.SetRequestHeader("Origin", $"http://127.0.0.1:{bridge.Port}");
        await wsClient.ConnectAsync(new Uri($"ws://127.0.0.1:{bridge.Port}/ws?nonce={nonce}"), CancellationToken.None);
        await wsClient.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);

        // Give handler a moment
        await Task.Delay(50);

        Assert.That(capturedCtx, Is.Not.Null);
        Assert.That(capturedCtx!.RedeemedNonce?.Nonce, Is.EqualTo(nonce));
    }

    [Test]
    public async Task AstraLocalBridge_WebSocket_RejectsInvalidNonce()
    {
        var nonceMgr = new SessionNonceManager();
        var security = new BridgeSecurityPolicy();

        await using var bridge = new AstraLocalBridge(
            nonceMgr, security, EmptyAssets(),
            (ctx, ct) => Task.CompletedTask);

        await bridge.StartAsync();

        using var wsClient = new ClientWebSocket();
        wsClient.Options.SetRequestHeader("Origin", $"http://127.0.0.1:{bridge.Port}");

        await wsClient.ConnectAsync(
            new Uri($"ws://127.0.0.1:{bridge.Port}/ws?nonce=badnonce12345"),
            CancellationToken.None);

        // Server should close with PolicyViolation
        var buffer = new byte[1024];
        var result = await wsClient.ReceiveAsync(buffer, CancellationToken.None);

        Assert.That(result.MessageType, Is.EqualTo(WebSocketMessageType.Close));
        Assert.That(wsClient.CloseStatus, Is.EqualTo(WebSocketCloseStatus.PolicyViolation));
    }

    [Test]
    public async Task AstraLocalBridge_WebSocket_RejectsNonceReuse()
    {
        var nonceMgr = new SessionNonceManager();
        var security = new BridgeSecurityPolicy();

        await using var bridge = new AstraLocalBridge(
            nonceMgr, security, EmptyAssets(),
            (ctx, ct) => Task.CompletedTask);

        await bridge.StartAsync();
        string url = bridge.CreateLaunchUrl();
        var uri = new Uri(url);
        string? nonce = System.Web.HttpUtility.ParseQueryString(uri.Query)["nonce"];

        // First connection – success
        using var ws1 = new ClientWebSocket();
        ws1.Options.SetRequestHeader("Origin", $"http://127.0.0.1:{bridge.Port}");
        await ws1.ConnectAsync(new Uri($"ws://127.0.0.1:{bridge.Port}/ws?nonce={nonce}"), CancellationToken.None);
        await ws1.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        await Task.Delay(50);

        // Second connection – nonce already consumed
        using var ws2 = new ClientWebSocket();
        ws2.Options.SetRequestHeader("Origin", $"http://127.0.0.1:{bridge.Port}");
        await ws2.ConnectAsync(new Uri($"ws://127.0.0.1:{bridge.Port}/ws?nonce={nonce}"), CancellationToken.None);
        var buffer = new byte[1024];
        var result = await ws2.ReceiveAsync(buffer, CancellationToken.None);

        Assert.That(result.MessageType, Is.EqualTo(WebSocketMessageType.Close));
        Assert.That(ws2.CloseStatus, Is.EqualTo(WebSocketCloseStatus.PolicyViolation));
    }

    [Test]
    public async Task AstraLocalBridge_StatusApi_ReturnsJson()
    {
        var nonceMgr = new SessionNonceManager();
        var security = new BridgeSecurityPolicy();

        await using var bridge = new AstraLocalBridge(
            nonceMgr, security, EmptyAssets(),
            (ctx, ct) => Task.CompletedTask);

        await bridge.StartAsync();

        using var http = new HttpClient();
        var response = await http.GetAsync($"http://127.0.0.1:{bridge.Port}/api/status");
        string json = await response.Content.ReadAsStringAsync();

        Assert.That((int)response.StatusCode, Is.EqualTo(200));
        Assert.That(json, Does.Contain("isRunning"));
    }

    [Test]
    public async Task AstraLocalBridge_Stop_DisablesLaunchUrlCreation()
    {
        var nonceMgr = new SessionNonceManager();
        var security = new BridgeSecurityPolicy();

        await using var bridge = new AstraLocalBridge(
            nonceMgr, security, EmptyAssets(),
            (ctx, ct) => Task.CompletedTask);

        await bridge.StartAsync();
        await bridge.StopAsync();

        Assert.Throws<InvalidOperationException>(() => bridge.CreateLaunchUrl());
    }
}

/// <summary>
/// Empty asset provider for testing — returns no assets.
/// </summary>
file sealed class EmptyAssetProvider : IWebAssetProvider
{
    public WebAsset? TryGetAsset(string path) => null;
}
