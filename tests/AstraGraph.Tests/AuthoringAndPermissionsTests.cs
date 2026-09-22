using System.Net.Http;
using System.Net.WebSockets;
using System.Text.Json;
using AstraGraph.Core;
using AstraGraph.Editor.Bridge;
using AstraGraph.Editor.Protocol;
using AstraGraph.HotReload;
using AstraGraph.Runtime;
using AstraGraph.Runtime.Debugging;
using AstraGraph.Runtime.Security;
using NUnit.Framework;

#if NET10_0_OR_GREATER
using AstraGraph.Robust.Server;
#endif

namespace AstraGraph.Tests;

[TestFixture]
public sealed class AuthoringAndPermissionsTests
{
    private sealed class MockAdminSession : IAstraAdminFacts
    {
        public Guid UserId { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "AdminUser";
        public uint AdminFlags { get; set; }
        public string? Rank { get; set; }
        public bool IsPlayerSandbox { get; set; }

        string IAstraAdminFacts.UserId => UserId.ToString();
    }

#if NET10_0_OR_GREATER
    [Test]
    public void PermissionProvider_RejectsSession_WithoutAstraFlag()
    {
        var provider = new RobustAdminPermissionProvider();
        var normalSession = new MockAdminSession
        {
            AdminFlags = 0u, // No flags
            Rank = "Player"
        };

        Assert.That(provider.CanEnterAstra(normalSession), Is.False);
        Assert.That(provider.ResolveUser(normalSession), Is.Null);
        Assert.That(provider.GetEffectivePermissions(normalSession), Is.EqualTo(AstraPermission.None));
    }

    [Test]
    public void PermissionProvider_AcceptsAdminSession_AndResolvesEffectivePermissions()
    {
        var provider = new RobustAdminPermissionProvider();
        var headAdmin = new MockAdminSession
        {
            AdminFlags = SS14AdminFlagsConstants.AdminFlagAstraGraph,
            Rank = "HeadAdmin"
        };

        Assert.That(provider.CanEnterAstra(headAdmin), Is.True);
        var user = provider.ResolveUser(headAdmin);
        Assert.That(user, Is.Not.Null);
        Assert.That(provider.HasPermission(headAdmin, AstraPermission.Admin), Is.True);
        Assert.That(provider.GetSecurityProfile(headAdmin), Is.EqualTo(Binding.SecurityProfile.Engine));

        var devSession = new MockAdminSession
        {
            AdminFlags = SS14AdminFlagsConstants.AdminFlagAstraGraph,
            Rank = "ContentDeveloper"
        };

        Assert.That(provider.CanEnterAstra(devSession), Is.True);
        Assert.That(provider.HasPermission(devSession, AstraPermission.EditDrafts), Is.True);
        Assert.That(provider.HasPermission(devSession, AstraPermission.Compile), Is.True);
        Assert.That(provider.HasPermission(devSession, AstraPermission.PublishServer), Is.False);
        Assert.That(provider.GetSecurityProfile(devSession), Is.EqualTo(Binding.SecurityProfile.Gameplay));
    }

    [Test]
    public void PermissionProvider_UserOverridesAndDenies_TakePrecedence()
    {
        var provider = new RobustAdminPermissionProvider();
        var userId = Guid.NewGuid();
        var userSession = new MockAdminSession
        {
            UserId = userId,
            AdminFlags = SS14AdminFlagsConstants.AdminFlagAstraGraph,
            Rank = "ContentDeveloper"
        };

        // Explicit override: Grant PublishServer, but Deny PublishShared
        provider.RegisterUserOverride(userId.ToString(), AstraPermission.ViewGraphs | AstraPermission.PublishServer | AstraPermission.PublishShared);
        provider.RegisterUserDeny(userId.ToString(), AstraPermission.PublishShared);

        Assert.That(provider.CanEnterAstra(userSession), Is.True);
        Assert.That(provider.HasPermission(userSession, AstraPermission.PublishServer), Is.True);
        Assert.That(provider.HasPermission(userSession, AstraPermission.PublishShared), Is.False, "Explicit deny must override granted permission!");
    }

    [Test]
    public void AuthoringService_IssuesLaunchToken_AndAuthenticates()
    {
        var host = new AstraGraphHost();
        var hotReload = new HotReloadManager(host);
        var provider = new RobustAdminPermissionProvider();
        var authoringService = new AstraAuthoringService(provider, hotReload);

        var adminSession = new MockAdminSession
        {
            AdminFlags = SS14AdminFlagsConstants.AdminFlagAstraGraph,
            Rank = "Developer"
        };

        var launchToken = authoringService.IssueLaunchToken(adminSession);
        Assert.That(launchToken, Is.Not.Null);
        Assert.That(launchToken!.Token, Does.StartWith("astra_"));

        var valid = authoringService.TryValidateToken(launchToken.Token, out var authenticatedUser);
        Assert.That(valid, Is.True);
        Assert.That(authenticatedUser, Is.Not.Null);
        Assert.That(authenticatedUser!.Id, Is.EqualTo(launchToken.UserId));

        // Unauthorized user cannot obtain token
        var unauthorizedSession = new MockAdminSession { AdminFlags = 0 };
        Assert.That(authoringService.IssueLaunchToken(unauthorizedSession), Is.Null);
    }
#endif

    [Test]
    public async Task StudioWeb_LoopbackBridge_ServesBundledAssetsAndStatus()
    {
        await using var bridge = AstraLocalBridge.CreateDefault();
        await bridge.StartAsync();

        Assert.That(bridge.IsRunning, Is.True);
        Assert.That(bridge.Port, Is.GreaterThan(0));

        using var client = new HttpClient();
        var baseUrl = $"http://127.0.0.1:{bridge.Port}";

        // 1. GET /index.html
        var htmlResp = await client.GetAsync($"{baseUrl}/index.html");
        Assert.That((int)htmlResp.StatusCode, Is.EqualTo(200));
        var htmlBody = await htmlResp.Content.ReadAsStringAsync();
        Assert.That(htmlBody, Does.Contain("Astra Studio"));

        // 2. GET /api/status
        var statusResp = await client.GetAsync($"{baseUrl}/api/status");
        Assert.That((int)statusResp.StatusCode, Is.EqualTo(200));
        var statusJson = await statusResp.Content.ReadAsStringAsync();
        Assert.That(statusJson, Does.Contain("isRunning"));
    }

    [Test]
    public async Task StudioWeb_WebSocketNonce_EnforcesOneTimeUse()
    {
        var nonceManager = new SessionNonceManager();
        var security = new BridgeSecurityPolicy();
        await using var bridge = new AstraLocalBridge(
            nonceManager,
            security,
            EmbeddedWebAssetProvider.CreateWithDefaultStudio(),
            async (ctx, ct) =>
            {
                // Echo handler
                var buffer = new byte[1024];
                while (ctx.WebSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var res = await ctx.WebSocket.ReceiveAsync(buffer, ct);
                    if (res.MessageType == WebSocketMessageType.Close) break;
                    await ctx.WebSocket.SendAsync(buffer[..res.Count], res.MessageType, res.EndOfMessage, ct);
                }
            });

        await bridge.StartAsync();

        var nonce = nonceManager.CreateNonce().Nonce;
        var wsUri = new Uri($"ws://127.0.0.1:{bridge.Port}/ws?nonce={nonce}");

        // 1. First connection succeeds
        using var ws1 = new ClientWebSocket();
        await ws1.ConnectAsync(wsUri, CancellationToken.None);
        Assert.That(ws1.State, Is.EqualTo(WebSocketState.Open));

        // 2. Second connection with the same redeemed nonce must fail
        using var ws2 = new ClientWebSocket();
        try
        {
            await ws2.ConnectAsync(wsUri, CancellationToken.None);
            var buffer = new byte[256];
            var receiveRes = await ws2.ReceiveAsync(buffer, CancellationToken.None);
            Assert.That(receiveRes.MessageType, Is.EqualTo(WebSocketMessageType.Close));
            Assert.That(ws2.CloseStatus, Is.EqualTo(WebSocketCloseStatus.PolicyViolation));
        }
        catch (WebSocketException)
        {
            // Socket closed immediately due to policy violation
            Assert.Pass("Handshake rejected due to redeemed nonce as expected.");
        }

        await ws1.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);
    }

    [Test]
    public async Task StudioWeb_EndToEnd_AuthoringProtocol_HandshakeListCompilePublishAndDebugger()
    {
        var host = new AstraGraphHost();
        var hotReload = new HotReloadManager(host);
        var debugger = new GraphDebugger();

        // Register a test user for the server session
        var testUser = new AstraUser("user-e2e", "Tester", AstraPermission.Admin, Binding.SecurityProfile.Engine);
        var authoringServer = new AuthoringServerSession(
            userAuthenticator: token => token == "secret-e2e-token" ? testUser : null,
            hotReloadManager: hotReload,
            debugger: debugger);

        // Pre-register an initial graph in authoring session
        var graphId = GraphId.New();
        var initialRevision = RevisionId.New();
        authoringServer.RegisterGraph(new GraphSummaryDto(
            graphId,
            "GameplaySystem",
            GraphKind.System,
            GraphSide.Server,
            initialRevision,
            1));

        // Start bridge with AuthoringServerSession as the message handler
        await using var bridge = AstraLocalBridge.CreateWithHandler(authoringServer);
        await bridge.StartAsync();

        var nonce = bridge.CreateLaunchUrl().Split("nonce=")[1].Split("&")[0];
        var wsUri = new Uri($"ws://127.0.0.1:{bridge.Port}/ws?nonce={nonce}");

        using var clientWs = new ClientWebSocket();
        await clientWs.ConnectAsync(wsUri, CancellationToken.None);
        Assert.That(clientWs.State, Is.EqualTo(WebSocketState.Open));

        // Step 1: Handshake
        var handshakeReq = new AuthHandshakeRequestMsg
        {
            ClientVersion = "1.0.0",
            AuthorToken = "secret-e2e-token",
            AuthorName = "Tester"
        };
        await SendMessageAsync(clientWs, handshakeReq);
        var handshakeResp = await ReceiveMessageAsync<AuthHandshakeResponseMsg>(clientWs);
        Assert.That(handshakeResp, Is.Not.Null);
        Assert.That(handshakeResp!.Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That(string.IsNullOrEmpty(handshakeResp.SessionId), Is.False);
        var sessionId = handshakeResp.SessionId;

        // Step 2: List graphs
        var listReq = new GraphListRequestMsg { SessionId = sessionId };
        await SendMessageAsync(clientWs, listReq);
        var listResp = await ReceiveMessageAsync<GraphListResponseMsg>(clientWs);
        Assert.That(listResp, Is.Not.Null);
        Assert.That(listResp!.Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That(listResp.Graphs.Count, Is.EqualTo(1));
        Assert.That(listResp.Graphs[0].Id, Is.EqualTo(graphId));

        // Step 3: Compile draft
        var doc = new GraphDocument
        {
            Id = graphId,
            Name = "GameplaySystem",
            Kind = GraphKind.System,
            Side = GraphSide.Server,
            Nodes = [],
            Connections = []
        };
        var draftJson = GraphSerializer.Serialize(doc);

        var compileReq = new DraftCompileRequestMsg
        {
            SessionId = sessionId,
            GraphId = graphId,
            DraftJson = draftJson
        };
        await SendMessageAsync(clientWs, compileReq);
        var compileResp = await ReceiveMessageAsync<DraftCompileResponseMsg>(clientWs);
        Assert.That(compileResp, Is.Not.Null);
        Assert.That(compileResp!.Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That(compileResp.HasErrors, Is.False);

        // Step 4: Publish draft over WebSocket
        var publishReq = new DraftPublishRequestMsg
        {
            SessionId = sessionId,
            GraphId = graphId,
            BaseRevisionId = initialRevision,
            DraftJson = draftJson,
            PublishMessage = "Published via WebSocket E2E"
        };
        await SendMessageAsync(clientWs, publishReq);
        var publishResp = await ReceiveMessageAsync<DraftPublishResponseMsg>(clientWs);
        Assert.That(publishResp, Is.Not.Null);
        Assert.That(publishResp!.Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That(publishResp.PublishedRevision, Is.Not.Null);

        // Verify program is now published on host!
        var publishedProgram = host.GetProgram(graphId);
        Assert.That(publishedProgram, Is.Not.Null);
        Assert.That(publishedProgram!.Revision, Is.EqualTo(publishResp.PublishedRevision!.Value));

        // Step 5: Debugger Command over WebSocket
        var debugNode = NodeId.New();
        var debugReq = new DebuggerCommandRequestMsg
        {
            SessionId = sessionId,
            GraphId = graphId,
            Action = DebuggerAction.SetBreakpoint,
            TargetNode = debugNode
        };
        await SendMessageAsync(clientWs, debugReq);
        var debugResp = await ReceiveMessageAsync<DebuggerCommandResponseMsg>(clientWs);
        Assert.That(debugResp, Is.Not.Null);
        Assert.That(debugResp!.Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That(debugResp.IsSuccess, Is.True);

        // Verify breakpoint was set on host debugger
        Assert.That(debugger.HasBreakpoint(debugNode), Is.True);

        // Clean close
        await clientWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test Complete", CancellationToken.None);
    }

    private static async Task SendMessageAsync(WebSocket ws, AuthoringMessage msg)
    {
        var bytes = MessageFramer.Frame(msg);
        await ws.SendAsync(bytes, WebSocketMessageType.Binary, true, CancellationToken.None);
    }

    private static async Task<T?> ReceiveMessageAsync<T>(WebSocket ws) where T : AuthoringMessage
    {
        var buffer = new byte[64 * 1024];
        using var ms = new MemoryStream();
        WebSocketReceiveResult res;
        do
        {
            res = await ws.ReceiveAsync(buffer, CancellationToken.None);
            if (res.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buffer, 0, res.Count);
        } while (!res.EndOfMessage);

        var (kind, payload) = MessageFramer.Unframe(ms.ToArray());
        return JsonSerializer.Deserialize<T>(payload, AuthoringJsonContext.Default);
    }
}
