#if NET10_0_OR_GREATER
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using AstraGraph.Core;
using AstraGraph.Editor.Protocol;
using AstraGraph.Studio.DevHost;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public class StudioDevHostTests
{
    [Test]
    public async Task Preview_GetRoot_ReturnsStudioHtml()
    {
        var logs = new List<string>();
        await using var server = await Start(DevHostMode.Preview, token: "preview-secret", logs: logs);
        using var client = new HttpClient();
        var html = await client.GetStringAsync(Base(server));
        Assert.That(html, Does.Contain("Astra Studio"));
        Assert.That(string.Join('\n', logs), Does.Contain("Token: ********"));
        Assert.That(string.Join('\n', logs), Does.Not.Contain("preview-secret"));

        var health = await client.GetAsync(Base(server) + "/health");
        Assert.That((int)health.StatusCode, Is.EqualTo(200));
        var denied = await client.PostAsync(Base(server) + "/api/publish", new StringContent("{}"));
        Assert.That((int)denied.StatusCode, Is.EqualTo(405));
    }

    [Test]
    public async Task Preview_AssetsAndTraversal()
    {
        await using var server = await Start(DevHostMode.Preview);
        using var client = new HttpClient();
        var script = await client.GetAsync(Base(server) + "/studio.js");
        var css = await client.GetAsync(Base(server) + "/studio.css");
        Assert.That((int)script.StatusCode, Is.EqualTo(200));
        Assert.That((int)css.StatusCode, Is.EqualTo(200));
        var scriptText = await script.Content.ReadAsStringAsync();
        Assert.That(scriptText, Does.Contain("Preview Mode"));
        Assert.That(scriptText, Does.Contain("No authoring backend connected"));

        var traversal = await client.GetAsync(Base(server) + "/%2e%2e/%2e%2e/Compatibility.json");
        Assert.That((int)traversal.StatusCode, Is.AnyOf(400, 403, 404));
    }

    [Test]
    public async Task Status_MatchesMode()
    {
        await using var preview = await Start(DevHostMode.Preview);
        await using var standalone = await Start(DevHostMode.Standalone, permissions: "developer");
        using var client = new HttpClient();

        var previewStatus = await client.GetFromJsonAsync<DevHostStatus>(Base(preview) + "/api/status");
        Assert.That(previewStatus!.Mode, Is.EqualTo("preview"));
        Assert.That(previewStatus.RuntimeConnected, Is.False);
        Assert.That(previewStatus.AuthoringAvailable, Is.False);
        Assert.That(previewStatus.Engine, Is.EqualTo("None"));

        var config = await client.GetFromJsonAsync<DevHostClientConfig>(Base(preview) + "/api/config");
        Assert.That(config!.Mode, Is.EqualTo("preview"));
        Assert.That(config.AuthoringAvailable, Is.False);

        var standaloneStatus = await client.GetFromJsonAsync<DevHostStatus>(Base(standalone) + "/api/status");
        Assert.That(standaloneStatus!.Mode, Is.EqualTo("standalone"));
        Assert.That(standaloneStatus.RuntimeConnected, Is.True);
        Assert.That(standaloneStatus.AuthoringAvailable, Is.True);
        Assert.That(standaloneStatus.Engine, Is.EqualTo("Standalone"));
    }

    [Test]
    public void ExternalBind_WithoutToken_IsRejected()
    {
        var options = new DevHostOptions
        {
            ListenAddress = "0.0.0.0",
            Port = 0,
            Token = null,
            OpenBrowser = false,
            Mode = DevHostMode.Preview
        };
        var error = Assert.ThrowsAsync<InvalidOperationException>(() => AstraStudioDevServer.StartAsync(options, log: static _ => { }));
        Assert.That(error!.Message, Does.Contain("Startup aborted"));
        Assert.That(error.Message, Does.Contain("External Astra Studio binding requires authentication."));
    }

    [Test]
    public async Task Nonce_IsSingleUse()
    {
        await using var server = await Start(DevHostMode.Standalone, permissions: "publisher");
        var nonce = server.IssueNonce();
        var uri = new Uri($"ws://127.0.0.1:{server.Port}/ws?nonce={nonce}");

        using var first = new ClientWebSocket();
        await first.ConnectAsync(uri, CancellationToken.None);
        Assert.That(first.State, Is.EqualTo(WebSocketState.Open));

        using var second = new ClientWebSocket();
        try
        {
            await second.ConnectAsync(uri, CancellationToken.None);
            var buffer = new byte[64];
            var result = await second.ReceiveAsync(buffer, CancellationToken.None);
            Assert.That(result.MessageType, Is.EqualTo(WebSocketMessageType.Close));
        }
        catch (WebSocketException)
        {
            Assert.That(second.State, Is.Not.EqualTo(WebSocketState.Open));
        }

        await first.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    [Test]
    public async Task Standalone_WebSocket_SaveCompileAndPublish()
    {
        await using var server = await Start(DevHostMode.Standalone, permissions: "publisher");
        Assert.That(server.Runtime, Is.Not.Null);
        using var socket = await Connect(server);
        var sessionId = await Handshake(socket, authorToken: "");
        var graphId = GraphId.New();
        var draft = GraphSerializer.Serialize(SampleGraph(graphId, "PublishedOverSocket"));

        await Send(socket, new GraphListRequestMsg { SessionId = sessionId });
        var listed = await Receive<GraphListResponseMsg>(socket);
        Assert.That(listed!.Status, Is.EqualTo(AuthoringStatusCode.Success));

        await Send(socket, new DraftSaveRequestMsg { SessionId = sessionId, GraphId = graphId, DraftJson = draft, AuthorMessage = "save" });
        var saved = await Receive<DraftSaveResponseMsg>(socket);
        Assert.That(saved!.Status, Is.EqualTo(AuthoringStatusCode.Success));

        await Send(socket, new DraftCompileRequestMsg { SessionId = sessionId, GraphId = graphId, DraftJson = draft });
        var compiled = await Receive<DraftCompileResponseMsg>(socket);
        Assert.That(compiled!.Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That(compiled.HasErrors, Is.False);

        await Send(socket, new DraftPublishRequestMsg { SessionId = sessionId, GraphId = graphId, DraftJson = draft, PublishMessage = "publish" });
        var published = await Receive<DraftPublishResponseMsg>(socket);
        Assert.That(published!.Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That(server.Runtime!.Host.GetProgram(graphId), Is.Not.Null);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    [Test]
    public async Task Standalone_Restart_KeepsPublishedGraph()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-devhost-" + Guid.NewGuid().ToString("N"));
        var project = Path.Combine(root, "Resources", "AstraGraph");
        var data = Path.Combine(root, "data", "AstraGraph");
        var graphId = GraphId.New();
        var draft = GraphSerializer.Serialize(SampleGraph(graphId, "SurvivesRestart"));

        await using (var first = await Start(DevHostMode.Standalone, permissions: "publisher", project: project, data: data))
        {
            using var socket = await Connect(first);
            var sessionId = await Handshake(socket, "");
            await Send(socket, new DraftPublishRequestMsg { SessionId = sessionId, GraphId = graphId, DraftJson = draft, PublishMessage = "persist" });
            var published = await Receive<DraftPublishResponseMsg>(socket);
            Assert.That(published!.Status, Is.EqualTo(AuthoringStatusCode.Success));
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        }

        await using var second = await Start(DevHostMode.Standalone, permissions: "publisher", project: project, data: data);
        using var again = await Connect(second);
        var nextSession = await Handshake(again, "");
        await Send(again, new GraphListRequestMsg { SessionId = nextSession });
        var listed = await Receive<GraphListResponseMsg>(again);
        Assert.That(listed!.Graphs.Any(graph => graph.Name == "SurvivesRestart"), Is.True);
        await Send(again, new HistoryListRequestMsg { SessionId = nextSession, GraphId = graphId });
        var history = await Receive<HistoryListResponseMsg>(again);
        Assert.That(history!.Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That(history.Revisions, Is.Not.Empty);
        await again.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    [Test]
    public async Task Standalone_Debugger_EmitsStreamEvent()
    {
        await using var server = await Start(DevHostMode.Standalone, permissions: "publisher");
        using var socket = await Connect(server);
        var sessionId = await Handshake(socket, "");
        var graphId = GraphId.New();
        var nodeId = NodeId.New();
        var draft = GraphSerializer.Serialize(SampleGraph(graphId, "Debugged", nodeId));
        await Send(socket, new DraftPublishRequestMsg { SessionId = sessionId, GraphId = graphId, DraftJson = draft, PublishMessage = "debug" });
        var published = await Receive<DraftPublishResponseMsg>(socket);
        Assert.That(published!.Status, Is.EqualTo(AuthoringStatusCode.Success));

        await Send(socket, new DebuggerCommandRequestMsg
        {
            SessionId = sessionId,
            GraphId = graphId,
            Action = DebuggerAction.SetBreakpoint,
            TargetNode = nodeId
        });
        var armed = await Receive<DebuggerCommandResponseMsg>(socket);
        Assert.That(armed!.IsSuccess, Is.True);

        await Send(socket, new DebuggerCommandRequestMsg { SessionId = sessionId, GraphId = graphId, Action = DebuggerAction.Pause });
        var paused = await Receive<DebuggerCommandResponseMsg>(socket);
        Assert.That(paused!.IsSuccess, Is.True);

        var executed = server.RunPublishedGraph(graphId);
        Assert.That(executed, Is.Not.Null);
        var streamed = await Receive<DebugStreamEventMsg>(socket);
        Assert.That(streamed!.Action, Is.EqualTo("paused"));
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    [Test]
    public async Task Permissions_DeveloperCompiles_PublisherPublishes()
    {
        var graphId = GraphId.New();
        var draft = GraphSerializer.Serialize(SampleGraph(graphId, "Permissioned"));

        await using (var developer = await Start(DevHostMode.Standalone, permissions: "developer"))
        {
            using var socket = await Connect(developer);
            var sessionId = await Handshake(socket, "");
            await Send(socket, new DraftCompileRequestMsg { SessionId = sessionId, GraphId = graphId, DraftJson = draft });
            var compiled = await Receive<DraftCompileResponseMsg>(socket);
            Assert.That(compiled!.Status, Is.EqualTo(AuthoringStatusCode.Success));
            await Send(socket, new DraftPublishRequestMsg { SessionId = sessionId, GraphId = graphId, DraftJson = draft, PublishMessage = "no" });
            var denied = await Receive<DraftPublishResponseMsg>(socket);
            Assert.That(denied!.Status, Is.EqualTo(AuthoringStatusCode.Unauthorized));
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        }

        await using var publisher = await Start(DevHostMode.Standalone, permissions: "publisher");
        using var publishing = await Connect(publisher);
        var publisherSession = await Handshake(publishing, "");
        await Send(publishing, new DraftPublishRequestMsg { SessionId = publisherSession, GraphId = graphId, DraftJson = draft, PublishMessage = "yes" });
        var published = await Receive<DraftPublishResponseMsg>(publishing);
        Assert.That(published!.Status, Is.EqualTo(AuthoringStatusCode.Success));
        await publishing.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    [Test]
    public async Task TwoSessions_KeepIndependentDrafts()
    {
        await using var server = await Start(DevHostMode.Standalone, permissions: "developer");
        using var first = await Connect(server);
        using var second = await Connect(server);
        var sessionA = await Handshake(first, "");
        var sessionB = await Handshake(second, "");
        Assert.That(sessionA, Is.Not.EqualTo(sessionB));

        var graphA = GraphId.New();
        var graphB = GraphId.New();
        var draftA = GraphSerializer.Serialize(SampleGraph(graphA, "AlphaDraft"));
        var draftB = GraphSerializer.Serialize(SampleGraph(graphB, "BetaDraft"));
        await Send(first, new DraftSaveRequestMsg { SessionId = sessionA, GraphId = graphA, DraftJson = draftA, AuthorMessage = "a" });
        Assert.That((await Receive<DraftSaveResponseMsg>(first))!.Status, Is.EqualTo(AuthoringStatusCode.Success));
        await Send(second, new DraftSaveRequestMsg { SessionId = sessionB, GraphId = graphB, DraftJson = draftB, AuthorMessage = "b" });
        Assert.That((await Receive<DraftSaveResponseMsg>(second))!.Status, Is.EqualTo(AuthoringStatusCode.Success));

        await Send(second, new GraphFetchRequestMsg { SessionId = sessionB, GraphId = graphA });
        var fetchedA = await Receive<GraphFetchResponseMsg>(second);
        await Send(first, new GraphFetchRequestMsg { SessionId = sessionA, GraphId = graphB });
        var fetchedB = await Receive<GraphFetchResponseMsg>(first);
        Assert.That(fetchedA!.DraftJson, Does.Contain("AlphaDraft"));
        Assert.That(fetchedB!.DraftJson, Does.Contain("BetaDraft"));
        Assert.That(fetchedA.DraftJson, Does.Not.Contain("BetaDraft"));

        await Send(first, new DraftCompileRequestMsg { SessionId = "missing-session", GraphId = graphA, DraftJson = draftA });
        var rejected = await Receive<DraftCompileResponseMsg>(first);
        Assert.That(rejected!.Status, Is.EqualTo(AuthoringStatusCode.Unauthorized));
        await first.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        await second.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    [Test]
    public async Task AttachedMode_UsesProvidedHandlerWithoutSecondRuntime()
    {
        var handler = new PingHandler();
        var backend = new TestBackend(handler);
        var options = new DevHostOptions
        {
            Mode = DevHostMode.Attached,
            ListenAddress = "127.0.0.1",
            Port = 0,
            OpenBrowser = false,
            Token = "attached-token"
        };
        await using var server = await AstraStudioDevServer.StartAsync(options, backend, static _ => { });
        Assert.That(server.Runtime, Is.Null);
        using var client = new HttpClient();
        var status = await client.GetFromJsonAsync<DevHostStatus>(Base(server) + "/api/status");
        Assert.That(status!.Mode, Is.EqualTo("attached"));
        Assert.That(status.Engine, Is.EqualTo("Attached Development Server"));
        Assert.That(status.RuntimeConnected, Is.True);
        Assert.That(status.CompatibilityProfile, Is.EqualTo("robust-api-v1"));

        using var socket = await Connect(server);
        await Send(socket, new PingMsg());
        var pong = await Receive<PongMsg>(socket);
        Assert.That(pong, Is.Not.Null);
        Assert.That(handler.Seen, Is.EqualTo(1));
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    [Test]
    public async Task OpenBrowser_MissingLauncher_KeepsTheHostRunning()
    {
        var logs = new List<string>();
        var root = Path.Combine(Path.GetTempPath(), "astra-devhost-" + Guid.NewGuid().ToString("N"));
        var options = new DevHostOptions
        {
            Mode = DevHostMode.Preview,
            ListenAddress = "127.0.0.1",
            Port = 0,
            OpenBrowser = true,
            ProjectPath = Path.Combine(root, "Resources", "AstraGraph"),
            DataPath = Path.Combine(root, "data", "AstraGraph")
        };

        await using var server = await AstraStudioDevServer.StartAsync(options, log: logs.Add);
        Assert.That(server.IsRunning, Is.True);
        using var client = new HttpClient();
        var health = await client.GetAsync(Base(server) + "/health");
        Assert.That((int)health.StatusCode, Is.EqualTo(200));
    }

    [Test]
    public void Parse_CliOverridesEnvironment()
    {
        var environment = new Dictionary<string, string?>
        {
            ["ASTRA_STUDIO_MODE"] = "preview",
            ["ASTRA_STUDIO_PORT"] = "1",
            ["ASTRA_STUDIO_LISTEN"] = "0.0.0.0"
        };
        var options = DevHostOptions.Parse(["--port", "9", "--listen", "127.0.0.1"], environment);
        Assert.That(options.Mode, Is.EqualTo(DevHostMode.Preview));
        Assert.That(options.Port, Is.EqualTo(9));
        Assert.That(options.ListenAddress, Is.EqualTo("127.0.0.1"));
    }

    private static async Task<AstraStudioDevServer> Start(
        DevHostMode mode,
        string permissions = "developer",
        string? token = null,
        string? project = null,
        string? data = null,
        List<string>? logs = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-devhost-" + Guid.NewGuid().ToString("N"));
        var options = new DevHostOptions
        {
            Mode = mode,
            ListenAddress = "127.0.0.1",
            Port = 0,
            Token = token,
            OpenBrowser = false,
            Permissions = permissions,
            ProjectPath = project ?? Path.Combine(root, "Resources", "AstraGraph"),
            DataPath = data ?? Path.Combine(root, "data", "AstraGraph")
        };
        return await AstraStudioDevServer.StartAsync(options, log: line => logs?.Add(line));
    }

    private static string Base(AstraStudioDevServer server) => $"http://127.0.0.1:{server.Port}";

    private static async Task<ClientWebSocket> Connect(AstraStudioDevServer server)
    {
        var nonce = server.IssueNonce();
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{server.Port}/ws?nonce={nonce}"), CancellationToken.None);
        return socket;
    }

    private static async Task<string> Handshake(ClientWebSocket socket, string authorToken)
    {
        await Send(socket, new AuthHandshakeRequestMsg { ClientVersion = "1.0.0", AuthorToken = authorToken, AuthorName = "DevHost" });
        var response = await Receive<AuthHandshakeResponseMsg>(socket);
        Assert.That(response!.Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That(response.CanCompile, Is.True);
        return response.SessionId;
    }

    private static GraphDocument SampleGraph(GraphId graphId, string name, NodeId? nodeId = null)
    {
        var entry = nodeId ?? NodeId.New();
        return new GraphDocument
        {
            Id = graphId,
            Name = name,
            Kind = GraphKind.System,
            Side = GraphSide.Server,
            Nodes =
            [
                new NodeDocument
                {
                    Id = entry,
                    Name = "OnStart",
                    NodeType = "Event.Start",
                    Pins = [new PinDocument { Id = PinId.New(), Name = "Out", Direction = PinDirection.Output, Kind = PinKind.Execution }]
                }
            ]
        };
    }

    private static async Task Send(WebSocket socket, AuthoringMessage message)
    {
        var bytes = MessageFramer.Frame(message);
        await socket.SendAsync(bytes, WebSocketMessageType.Binary, true, CancellationToken.None);
    }

    private static async Task<T?> Receive<T>(WebSocket socket) where T : AuthoringMessage
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var buffer = new byte[64 * 1024];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, timeout.Token);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            stream.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        var (_, payload) = MessageFramer.Unframe(stream.ToArray());
        return JsonSerializer.Deserialize<T>(payload, AuthoringJsonContext.Default);
    }

    private sealed class PingHandler : IAuthoringMessageHandler
    {
        public int Seen { get; private set; }

        public Task<AuthoringMessage?> HandleAsync(AuthoringMessage message, CancellationToken ct)
        {
            Seen++;
            return Task.FromResult<AuthoringMessage?>(new PongMsg());
        }
    }

    private sealed class TestBackend(PingHandler handler) : IAstraDevHostBackend
    {
        public IAuthoringMessageHandler AuthoringHandler { get; } = handler;

        public DevHostBackendInfo Info { get; } = new(
            "Target: Attached Development Server",
            "Attached Development Server",
            true,
            "robust-api-v1");
    }
}
#endif
