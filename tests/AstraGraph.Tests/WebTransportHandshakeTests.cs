using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AstraGraph.Editor.Bridge;
using AstraGraph.Editor.Protocol;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class WebTransportHandshakeTests
{
    [Test]
    public async Task WebClient_CanConnect_AndExchangeHelloHandshake()
    {
        var nonceMgr = new SessionNonceManager();
        var security = new BridgeSecurityPolicy();

        string? receivedNonce = null;
        string? receivedSession = null;

        await using var bridge = AstraLocalBridge.CreateDefault(
            nonceMgr,
            security,
            async (wsCtx, ct) =>
            {
                receivedNonce = wsCtx.Nonce;
                receivedSession = wsCtx.SessionId;

                // Simple echo / acknowledge handshake
                var buffer = new byte[4096];
                var result = await wsCtx.WebSocket.ReceiveAsync(buffer, ct);
                var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                
                var ack = JsonSerializer.Serialize(new
                {
                    type = "HelloAck",
                    status = "Authenticated",
                    sessionId = wsCtx.SessionId
                });

                var ackBytes = Encoding.UTF8.GetBytes(ack);
                await wsCtx.WebSocket.SendAsync(ackBytes, WebSocketMessageType.Text, true, ct);
            });

        await bridge.StartAsync();

        // Create launch nonce
        var launchUrl = bridge.CreateLaunchUrl();
        var uri = new Uri(launchUrl);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var nonce = query["nonce"];

        Assert.That(nonce, Is.Not.Null.And.Not.Empty);

        // Connect client
        using var clientWs = new ClientWebSocket();
        var wsUri = new Uri($"ws://127.0.0.1:{bridge.Port}/ws?nonce={nonce}");
        await clientWs.ConnectAsync(wsUri, CancellationToken.None);

        Assert.That(clientWs.State, Is.EqualTo(WebSocketState.Open));

        // Send Hello
        var helloMsg = JsonSerializer.Serialize(new
        {
            type = "Hello",
            clientVersion = "1.0.0",
            nonce = nonce
        });
        await clientWs.SendAsync(Encoding.UTF8.GetBytes(helloMsg), WebSocketMessageType.Text, true, CancellationToken.None);

        // Receive HelloAck
        var recvBuf = new byte[4096];
        var recvResult = await clientWs.ReceiveAsync(recvBuf, CancellationToken.None);
        var ackJson = Encoding.UTF8.GetString(recvBuf, 0, recvResult.Count);

        using var doc = JsonDocument.Parse(ackJson);
        Assert.That(doc.RootElement.GetProperty("type").GetString(), Is.EqualTo("HelloAck"));
        Assert.That(doc.RootElement.GetProperty("status").GetString(), Is.EqualTo("Authenticated"));

        await clientWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);
        await bridge.StopAsync();
    }

    [Test]
    public async Task WebClient_InvalidNonce_IsRejected()
    {
        var nonceMgr = new SessionNonceManager();
        var security = new BridgeSecurityPolicy();

        await using var bridge = AstraLocalBridge.CreateDefault(
            nonceMgr,
            security,
            (wsCtx, ct) => Task.CompletedTask);

        await bridge.StartAsync();

        // Connect with completely fake nonce
        using var clientWs = new ClientWebSocket();
        var wsUri = new Uri($"ws://127.0.0.1:{bridge.Port}/ws?nonce=fake-invalid-nonce-12345");

        try
        {
            await clientWs.ConnectAsync(wsUri, CancellationToken.None);
            
            // If connection was accepted by TCP before WebSocket handshake, next read will show close
            var buf = new byte[1024];
            var res = await clientWs.ReceiveAsync(buf, CancellationToken.None);
            Assert.That(clientWs.State, Is.EqualTo(WebSocketState.CloseReceived).Or.EqualTo(WebSocketState.Closed));
        }
        catch (WebSocketException)
        {
            // Successfully rejected at HTTP/WebSocket upgrade handshake
            Assert.Pass("Rejected at handshake as expected");
        }
        finally
        {
            await bridge.StopAsync();
        }
    }
}
