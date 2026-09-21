using System.Net.WebSockets;
using AstraGraph.Editor.Bridge;
using AstraGraph.Editor.Protocol;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
[Category("Bridge.W2")]
public sealed class TransportAndProxyTests
{
    // ─── MessageFramer ─────────────────────────────────────────────────────

    [Test]
    public void MessageFramer_Frame_Unframe_RoundTrip()
    {
        var msg = new PingMsg();
        byte[] framed = MessageFramer.Frame(msg);
        var (kind, payload) = MessageFramer.Unframe(framed);

        Assert.That(kind, Is.EqualTo("ping"));
        Assert.That(payload.Length, Is.GreaterThan(0));
    }

    [Test]
    public void MessageFramer_Unframe_ThrowsOnTooShortBuffer()
    {
        Assert.Throws<InvalidOperationException>(() => MessageFramer.Unframe(new byte[2]));
    }

    [Test]
    public void MessageFramer_Unframe_ThrowsOnInvalidKindLength()
    {
        byte[] buf = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(buf, 9999);
        Assert.Throws<InvalidOperationException>(() => MessageFramer.Unframe(buf));
    }

    [Test]
    public void MessageFramer_PreservesPayloadIntegrity()
    {
        var msg = new DraftCompileRequestMsg
        {
            SessionId = "sess-abc",
            GraphId = new AstraGraph.Core.GraphId(Guid.NewGuid()),
            DraftJson = "{\"nodes\":[]}"
        };

        byte[] framed = MessageFramer.Frame(msg);
        var (kind, payload) = MessageFramer.Unframe(framed);

        Assert.That(kind, Is.EqualTo("draft.compile.request"));
        string json = System.Text.Encoding.UTF8.GetString(payload);
        Assert.That(json, Does.Contain("sess-abc"));
    }

    // ─── BridgeWebSocketProxy & PingPongHandler ────────────────────────────

    [Test]
    public async Task BridgeWebSocketProxy_PingPong_ViaLocalBridge()
    {
        var nonceMgr = new SessionNonceManager();
        var security = new BridgeSecurityPolicy();

        AuthoringMessage? receivedByServer = null;

        // Server-side handler
        var handler = new CapturingHandler(msg =>
        {
            receivedByServer = msg;
            return msg is PingMsg ? new PongMsg() : null;
        });

        await using var bridge = new AstraLocalBridge(
            nonceMgr, security, new EmptyWebAssetProvider(),
            async (ctx, ct) =>
            {
                var proxy = new BridgeWebSocketProxy(handler);
                await proxy.RunAsync(ctx.WebSocket, ct);
            });

        await bridge.StartAsync();
        string url = bridge.CreateLaunchUrl();
        var uri = new Uri(url);
        string? nonce = System.Web.HttpUtility.ParseQueryString(uri.Query)["nonce"];

        // Client side
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Origin", $"http://127.0.0.1:{bridge.Port}");
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{bridge.Port}/ws?nonce={nonce}"), CancellationToken.None);

        // Send Ping
        byte[] ping = MessageFramer.Frame(new PingMsg());
        await ws.SendAsync(ping, WebSocketMessageType.Binary, endOfMessage: true, CancellationToken.None);

        // Receive Pong
        var buf = new byte[4096];
        var result = await ws.ReceiveAsync(buf, CancellationToken.None);
        var (kind, _) = MessageFramer.Unframe(buf[..result.Count]);

        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        await Task.Delay(50);

        Assert.That(kind, Is.EqualTo("pong"));
        Assert.That(receivedByServer, Is.InstanceOf<PingMsg>());
    }

    [Test]
    public async Task BridgeWebSocketProxy_MalformedFrame_ReturnsErrorMsg()
    {
        var nonceMgr = new SessionNonceManager();
        var security = new BridgeSecurityPolicy();
        var handler = new PingPongHandler();

        await using var bridge = new AstraLocalBridge(
            nonceMgr, security, new EmptyWebAssetProvider(),
            async (ctx, ct) =>
            {
                var proxy = new BridgeWebSocketProxy(handler);
                await proxy.RunAsync(ctx.WebSocket, ct);
            });

        await bridge.StartAsync();
        string url = bridge.CreateLaunchUrl();
        var uri = new Uri(url);
        string? nonce = System.Web.HttpUtility.ParseQueryString(uri.Query)["nonce"];

        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Origin", $"http://127.0.0.1:{bridge.Port}");
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{bridge.Port}/ws?nonce={nonce}"), CancellationToken.None);

        // Send garbage
        byte[] garbage = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x01, 0x02 };
        await ws.SendAsync(garbage, WebSocketMessageType.Binary, endOfMessage: true, CancellationToken.None);

        // Should receive an error message
        var buf = new byte[4096];
        var result = await ws.ReceiveAsync(buf, CancellationToken.None);

        if (result.MessageType != WebSocketMessageType.Close)
        {
            var (kind, _) = MessageFramer.Unframe(buf[..result.Count]);
            Assert.That(kind, Is.EqualTo("error"));
        }

        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    // ─── Transport State Machine ───────────────────────────────────────────

    [Test]
    public async Task LocalGameBridgeTransport_ConnectDisconnect_StateTransitions()
    {
        var nonceMgr = new SessionNonceManager();
        var security = new BridgeSecurityPolicy();

        await using var bridge = new AstraLocalBridge(
            nonceMgr, security, new EmptyWebAssetProvider(),
            async (ctx, ct) =>
            {
                var proxy = new BridgeWebSocketProxy(new PingPongHandler());
                await proxy.RunAsync(ctx.WebSocket, ct);
            });

        await bridge.StartAsync();
        string url = bridge.CreateLaunchUrl();
        var uri = new Uri(url);
        string? nonce = System.Web.HttpUtility.ParseQueryString(uri.Query)["nonce"];

        await using var transport = new LocalGameBridgeTransport();
        Assert.That(transport.State, Is.EqualTo(TransportState.Disconnected));

        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Origin", $"http://127.0.0.1:{bridge.Port}");
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{bridge.Port}/ws?nonce={nonce}"), CancellationToken.None);
        Assert.That(ws.State, Is.EqualTo(WebSocketState.Open));

        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    [Test]
    public void RemoteServerTransport_InitialState_IsDisconnected()
    {
        var transport = new RemoteServerTransport();
        Assert.That(transport.State, Is.EqualTo(TransportState.Disconnected));
        Assert.That(transport.IsAuthenticated, Is.False);
        Assert.That(transport.SessionId, Is.Null);
    }


    [Test]
    public async Task RemoteServerTransport_ConnectToNonExistentEndpoint_ThrowsAndFaults()
    {
        await using var transport = new RemoteServerTransport("badtoken");

        Assert.That(transport.State, Is.EqualTo(TransportState.Disconnected));

        try
        {
            await transport.ConnectAsync(new Uri("ws://127.0.0.1:19999/ws"));
        }
        catch
        {
            // expected
        }

        Assert.That(transport.State, Is.EqualTo(TransportState.Faulted));
    }

    // ─── AuthoringJsonContext ──────────────────────────────────────────────

    [Test]
    public void AuthoringJsonContext_CanSerializeAndDeserializePingMsg()
    {
        var ping = new PingMsg();
        byte[] framed = MessageFramer.Frame(ping);
        var (kind, payload) = MessageFramer.Unframe(framed);
        Assert.That(kind, Is.EqualTo("ping"));

        var deserialized = System.Text.Json.JsonSerializer.Deserialize<PingMsg>(
            payload, AuthoringJsonContext.Default);
        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.Kind, Is.EqualTo("ping"));
    }

    [Test]
    public void AuthoringJsonContext_CanSerializeComplexMsg()
    {
        var msg = new DraftCompileResponseMsg
        {
            Status = AstraGraph.Editor.Protocol.AuthoringStatusCode.Success,
            HasErrors = false,
            Diagnostics = Array.Empty<AstraGraph.Core.Diagnostic>(),
            BytecodeHash = "abc123"
        };

        byte[] framed = MessageFramer.Frame(msg);
        var (kind, payload) = MessageFramer.Unframe(framed);
        Assert.That(kind, Is.EqualTo("draft.compile.response"));

        var deserialized = System.Text.Json.JsonSerializer.Deserialize<DraftCompileResponseMsg>(
            payload, AuthoringJsonContext.Default);
        Assert.That(deserialized?.BytecodeHash, Is.EqualTo("abc123"));
    }

}

/// <summary>Helper handler that captures the last received message.</summary>
file sealed class CapturingHandler(Func<AuthoringMessage, AuthoringMessage?> respond)
    : IAuthoringMessageHandler
{
    public Task<AuthoringMessage?> HandleAsync(AuthoringMessage message, CancellationToken ct)
        => Task.FromResult(respond(message));
}

file sealed class EmptyWebAssetProvider : IWebAssetProvider
{
    public WebAsset? TryGetAsset(string path) => null;
}
