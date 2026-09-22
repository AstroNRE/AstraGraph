using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AstraGraph.Binding;
using AstraGraph.Core;
using AstraGraph.Editor.Protocol;
using AstraGraph.HotReload;
using AstraGraph.Persistence;
using AstraGraph.Persistence.Audit;
using AstraGraph.Runtime;
using AstraGraph.Runtime.Debugging;
using AstraGraph.Runtime.Profiling;
using AstraGraph.Runtime.Security;
using NUnit.Framework;

namespace AstraGraph.Tests;

public class ProtocolCatalogSample
{
    public long Offset { get; set; } = 42L;
    public long Compute(long a, long b) => a * b + Offset;
}

[TestFixture]
public sealed class AuthoringProtocolTests
{
    private string _tempDir = null!;
    private StorageLayout _storage = null!;
    private AuditLogger _auditLogger = null!;
    private AstraGraphHost _host = null!;
    private HotReloadManager _hotReloadManager = null!;
    private GraphDebugger _debugger = null!;
    private GraphProfiler _profiler = null!;
    private BindingCatalog _bindingCatalog = null!;
    private AuthoringServerSession _server = null!;
    private AuthoringClientSession _client = null!;

    private readonly AstraUser _adminUser = new(
        "user_admin",
        "AdminUser",
        AstraPermission.Admin,
        SecurityProfile.Engine);

    private readonly AstraUser _limitedUser = new(
        "user_limited",
        "ReadOnlyUser",
        AstraPermission.ViewGraphs,
        SecurityProfile.Gameplay);

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AstraGraph_Proto_" + Guid.NewGuid().ToString("N"));
        _storage = new StorageLayout(_tempDir);
        _auditLogger = new AuditLogger(_storage);
        _host = new AstraGraphHost();
        _hotReloadManager = new HotReloadManager(_host);
        _debugger = new GraphDebugger();
        _profiler = new GraphProfiler();
        _bindingCatalog = new BindingCatalog();
        _bindingCatalog.IndexType(typeof(ProtocolCatalogSample));

        _server = new AuthoringServerSession(
            token => token == "token_admin" ? _adminUser : token == "token_limited" ? _limitedUser : null,
            _hotReloadManager,
            _auditLogger,
            _debugger,
            _profiler,
            _bindingCatalog);

        _client = new AuthoringClientSession(_server);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private static GraphDocument CreateTestGraphDocument(GraphId graphId, string name = "ProtoTestGraph", GraphSide side = GraphSide.Server)
    {
        var entryId = NodeId.New();
        var execOut = PinId.New();

        return new GraphDocument
        {
            Id = graphId,
            Name = name,
            Kind = GraphKind.System,
            Side = side,
            Metadata = new GraphMetadata
            {
                Author = "AdminUser",
                Description = "Protocol test graph",
                Version = "1.0.0"
            },
            Nodes =
            [
                new NodeDocument
                {
                    Id = entryId,
                    Name = "OnTestEvent",
                    NodeType = "Astra.EntryPoint",
                    Pins =
                    [
                        new PinDocument
                        {
                            Id = execOut,
                            Name = "Out",
                            Direction = PinDirection.Output,
                            Kind = PinKind.Execution
                        }
                    ],
                    Properties = { ["EventName"] = "OnTestEvent" }
                }
            ]
        };
    }

    [Test]
    public async Task Handshake_WithValidToken_ReturnsSessionAndPermissions()
    {
        var response = await _client.ConnectAsync("token_admin", "AdminUser");
        Assert.That(response.Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That(_client.IsAuthenticated, Is.True);
        Assert.That(_client.Permissions.HasFlag(AstraPermission.PublishServer), Is.True);
    }

    [Test]
    public async Task Handshake_WithInvalidToken_ReturnsUnauthorized()
    {
        var response = await _client.ConnectAsync("invalid_token", "Attacker");
        Assert.That(response.Status, Is.EqualTo(AuthoringStatusCode.Unauthorized));
        Assert.That(_client.IsAuthenticated, Is.False);
    }

    [Test]
    public async Task DraftSave_WithHeadRevision_Succeeds()
    {
        await _client.ConnectAsync("token_admin", "AdminUser");

        var graphId = GraphId.New();
        var revId = RevisionId.New();
        _server.RegisterGraph(new GraphSummaryDto(graphId, "TestGraph", GraphKind.System, GraphSide.Server, revId, 1));

        var graphDoc = CreateTestGraphDocument(graphId);
        var json = GraphSerializer.Serialize(graphDoc);

        var saveResult = await _client.SaveDraftAsync(graphId, revId, json, "Work in progress");
        Assert.That(saveResult.Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That(saveResult.HasConflict, Is.False);
        Assert.That(saveResult.DraftRevisionId, Is.Not.Null);
    }

    [Test]
    public async Task DraftSave_WithStaleRevision_DetectsConflict()
    {
        await _client.ConnectAsync("token_admin", "AdminUser");

        var graphId = GraphId.New();
        var headRev = RevisionId.New();
        var staleRev = RevisionId.New();
        _server.RegisterGraph(new GraphSummaryDto(graphId, "TestGraph", GraphKind.System, GraphSide.Server, headRev, 1));

        var graphDoc = CreateTestGraphDocument(graphId);
        var json = GraphSerializer.Serialize(graphDoc);

        var saveResult = await _client.SaveDraftAsync(graphId, staleRev, json, "Stale branch commit");
        Assert.That(saveResult.Status, Is.EqualTo(AuthoringStatusCode.Conflict));
        Assert.That(saveResult.HasConflict, Is.True);
        Assert.That(saveResult.ConflictDetails, Does.Contain(headRev.ToString()));
    }

    [Test]
    public async Task DraftCompile_WithValidDraft_ReturnsBytecodeHashAndDiagnostics()
    {
        await _client.ConnectAsync("token_admin", "AdminUser");

        var graphId = GraphId.New();
        var graphDoc = CreateTestGraphDocument(graphId);
        var json = GraphSerializer.Serialize(graphDoc);

        var compileResult = await _client.CompileDraftAsync(graphId, json);
        Assert.That(compileResult.Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That(compileResult.HasErrors, Is.False);
        Assert.That(compileResult.BytecodeHash, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public async Task DraftPublish_WithPermissions_PublishesRevisionAndWritesAuditLog()
    {
        await _client.ConnectAsync("token_admin", "AdminUser");

        var graphId = GraphId.New();
        var baseRev = RevisionId.New();
        _server.RegisterGraph(new GraphSummaryDto(graphId, "PublishGraph", GraphKind.System, GraphSide.Server, baseRev, 1));

        var graphDoc = CreateTestGraphDocument(graphId, "PublishGraph");
        var json = GraphSerializer.Serialize(graphDoc);

        var publishResult = await _client.PublishDraftAsync(graphId, baseRev, json, "Published v2 via protocol");
        Assert.That(publishResult.Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That(publishResult.PublishedRevision, Is.Not.Null);

        // Verify audit log has been written
        var auditRecords = _auditLogger.ReadRecords(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(5));
        Assert.That(auditRecords.Count, Is.GreaterThan(0));
        var record = auditRecords[^1];
        Assert.That(record.Action, Is.EqualTo("Publish"));
        Assert.That(record.Author, Is.EqualTo("AdminUser"));
    }

    [Test]
    public async Task DraftPublish_WithoutPermissions_RejectsUnauthorized()
    {
        await _client.ConnectAsync("token_limited", "ReadOnlyUser");

        var graphId = GraphId.New();
        var baseRev = RevisionId.New();
        _server.RegisterGraph(new GraphSummaryDto(graphId, "ProtectedGraph", GraphKind.System, GraphSide.Server, baseRev, 1));

        var graphDoc = CreateTestGraphDocument(graphId, "ProtectedGraph");
        var json = GraphSerializer.Serialize(graphDoc);

        var publishResult = await _client.PublishDraftAsync(graphId, baseRev, json, "Attempt unauthorized publish");
        Assert.That(publishResult.Status, Is.EqualTo(AuthoringStatusCode.Unauthorized));
    }

    [Test]
    public async Task CatalogQuery_ReturnsFilteredDescriptors()
    {
        await _client.ConnectAsync("token_admin", "AdminUser");

        var catalogResponse = await _client.QueryCatalogAsync("Compute");
        Assert.That(catalogResponse.Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That(catalogResponse.Entries.Count, Is.GreaterThan(0));
        Assert.That(catalogResponse.Entries[0].Signature, Does.Contain("Compute"));
    }

    [Test]
    public async Task DebuggerCommand_SetsBreakpointOnServer()
    {
        await _client.ConnectAsync("token_admin", "AdminUser");

        var graphId = GraphId.New();
        var targetNode = NodeId.New();

        var setBpResult = await _client.SendDebuggerCommandAsync(graphId, DebuggerAction.SetBreakpoint, targetNode);
        Assert.That(setBpResult.Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That(_debugger.HasBreakpoint(targetNode), Is.True);

        var removeBpResult = await _client.SendDebuggerCommandAsync(graphId, DebuggerAction.RemoveBreakpoint, targetNode);
        Assert.That(removeBpResult.Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That(_debugger.HasBreakpoint(targetNode), Is.False);
    }

    [Test]
    public async Task LiveGraph_OpensPublishedSource_PublishesNextRevision_AndRollsBackToTheChosenOne()
    {
        await _client.ConnectAsync("token_admin", "AdminUser");
        var created = _server.HandleGraphCreate(new GraphCreateRequest(_client.SessionId!, "Versioned", GraphKind.System, GraphSide.Server));
        Assert.That(created.Status, Is.EqualTo(AuthoringStatusCode.Success));
        var graphId = created.Graph!.Id;

        var v1Json = GraphSerializer.Serialize(CreateTestGraphDocument(graphId, "Versioned"));
        var published1 = await _client.PublishDraftAsync(graphId, RevisionId.Empty, v1Json, "v1");
        Assert.That(published1.Status, Is.EqualTo(AuthoringStatusCode.Success));
        var rev1 = published1.PublishedRevision!.Value;
        var program1 = _host.GetProgram(graphId)!.Revision;

        var opened = _server.HandleGraphFetch(new GraphFetchRequest(_client.SessionId!, graphId));
        Assert.That(opened.Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That(opened.FromDraft, Is.False);
        Assert.That(opened.DraftJson, Does.Contain("Versioned"));
        Assert.That(opened.BaseRevisionId, Is.EqualTo(rev1));

        var v2Json = GraphSerializer.Serialize(CreateTestGraphDocument(graphId, "Versioned v2"));
        var published2 = await _client.PublishDraftAsync(graphId, rev1, v2Json, "v2");
        Assert.That(published2.Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That(_host.GetProgram(graphId)!.Revision, Is.Not.EqualTo(program1));

        var rolled = await _client.RollbackAsync(graphId, rev1);
        Assert.That(rolled.Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That(rolled.ActivatedRevision, Is.EqualTo(rev1));
        Assert.That(_host.GetProgram(graphId)!.Revision, Is.EqualTo(program1));

        var restored = _server.HandleGraphFetch(new GraphFetchRequest(_client.SessionId!, graphId));
        Assert.That(restored.FromDraft, Is.False);
        Assert.That(GraphSerializer.Deserialize(restored.DraftJson).Name, Is.EqualTo("Versioned"));
        Assert.That(restored.ActiveRevisionId, Is.EqualTo(rolled.CurrentRevision));
    }

    [Test]
    public async Task Handshake_RejectsAnUnknownProtocolVersion()
    {
        var response = await _server.HandleAsync(new AuthHandshakeRequestMsg
        {
            ProtocolVersion = 99,
            ClientVersion = "1.0.0",
            AuthorToken = "token_admin",
            AuthorName = "AdminUser"
        }, CancellationToken.None);

        var handshake = response as AuthHandshakeResponseMsg;
        Assert.That(handshake, Is.Not.Null);
        Assert.That(handshake!.Status, Is.EqualTo(AuthoringStatusCode.IncompatibleVersion));
        Assert.That(handshake.ErrorMessage, Does.Contain("protocol 99"));
    }

    [Test]
    public async Task GraphDelete_RemovesTheGraphFromTheProject()
    {
        await _client.ConnectAsync("token_admin", "AdminUser");
        var created = _server.HandleGraphCreate(new GraphCreateRequest(_client.SessionId!, "Disposable", GraphKind.System, GraphSide.Server));
        var deleted = _server.HandleGraphDelete(new GraphDeleteRequest(_client.SessionId!, created.Graph!.Id));
        Assert.That(deleted.Status, Is.EqualTo(AuthoringStatusCode.Success));
        var listed = await _client.ListGraphsAsync();
        Assert.That(listed.Graphs.Any(graph => graph.Id == created.Graph.Id), Is.False);
    }

    [Test]
    public async Task BindingMaterialize_CreatesTypedPins_AndRejectsMismatchedWires()
    {
        await _client.ConnectAsync("token_admin", "AdminUser");
        var catalog = await _client.QueryCatalogAsync("Compute");
        var entry = catalog.Entries.First(item => item.MethodName == "Compute");
        Assert.That(entry.BindingId, Does.Contain("Compute"));
        Assert.That(entry.Parameters.Any(pin => pin.Name is "a" or "b"), Is.True);

        var json = _server.MaterializeBinding(entry.BindingId);
        Assert.That(json, Is.Not.Null);
        var node = GraphSerializer.Deserialize(json!).Nodes[0];
        Assert.That(node.Pins.Any(pin => pin.Kind == PinKind.Data && pin.Name == "a"), Is.True);
        Assert.That(node.Pins.Any(pin => pin.Kind == PinKind.Data && pin.Name == "Result"), Is.True);
        Assert.That(node.Properties["bindingId"], Is.EqualTo(entry.BindingId));

        var resultPin = node.Pins.First(pin => pin.Name == "Result");
        var mismatch = AuthoringServerSession.ValidateWire(
            new PinWire(resultPin.Id.ToString(), node.Id.ToString(), resultPin.Name, "output", "data", resultPin.DataType),
            new PinWire(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "in", "input", "data", "System.String"),
            []);
        Assert.That(mismatch.IsValid, Is.False);
        Assert.That(mismatch.ErrorReason, Does.Contain("Type mismatch"));

        var same = AuthoringServerSession.ValidateWire(
            new PinWire(resultPin.Id.ToString(), node.Id.ToString(), resultPin.Name, "output", "data", resultPin.DataType),
            new PinWire(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "in", "input", "data", resultPin.DataType),
            []);
        Assert.That(same.IsValid, Is.True);

        var suggestions = _server.SuggestConnections(
            new PinWire(resultPin.Id.ToString(), node.Id.ToString(), resultPin.Name, "output", "data", resultPin.DataType),
            "Compute");
        Assert.That(suggestions.Any(item => item.PinName is "a" or "b" && item.BindingId == entry.BindingId), Is.True);
    }

    [Test]
    public async Task HistoryDiff_ShowsANodeAddedSinceThePublishedRevision()
    {
        await _client.ConnectAsync("token_admin", "AdminUser");
        var graphId = GraphId.New();
        var baseRev = RevisionId.New();
        _server.RegisterGraph(new GraphSummaryDto(graphId, "DiffGraph", GraphKind.System, GraphSide.Server, baseRev, 1));
        var publishedJson = GraphSerializer.Serialize(CreateTestGraphDocument(graphId, "DiffGraph"));
        var published = await _client.PublishDraftAsync(graphId, baseRev, publishedJson, "v1");
        Assert.That(published.PublishedRevision, Is.Not.Null);

        var draft = GraphSerializer.Deserialize(publishedJson);
        draft.Nodes.Add(new NodeDocument { Id = NodeId.New(), Name = "Added", NodeType = "Flow.Branch" });
        var changes = _server.DiffDraft(graphId, published.PublishedRevision!.Value, GraphSerializer.Serialize(draft));
        Assert.That(changes.Any(change => change.Kind == "node.added" && change.Detail == "Added"), Is.True);
        Assert.That(changes.Any(change => change.Kind == "node.removed"), Is.False);
    }

    [Test]
    public async Task StudioSurfaces_InspectWatchesProfileAuditAndKeepSchemaFieldIdentity()
    {
        await _client.ConnectAsync("token_admin", "AdminUser");
        var node = NodeId.New();
        _debugger.Pause();
        Assert.That(_debugger.ShouldSuspend(node, 4, new[] { AstraValue.FromInt64(7) }), Is.True);

        _server.RememberWatch(_client.SessionId!, "Health", "r0", remove: false);
        var inspect = _server.InspectDebugger(_client.SessionId!);
        Assert.That(inspect.SuspendedNodeId, Is.EqualTo(node.ToString()));
        Assert.That(inspect.Locals[0].Value, Does.Contain("7"));
        Assert.That(inspect.Watches[0].Value, Does.Contain("7"));

        var graphId = GraphId.New();
        _profiler.RecordInstruction(graphId, node);
        var snapshot = _server.CaptureProfiler(graphId, reset: false);
        Assert.That(snapshot.Instructions, Is.EqualTo(1));
        Assert.That(snapshot.Hottest.Any(item => item.NodeId == node.ToString() && item.Hits == 1), Is.True);

        var fieldId = FieldId.New();
        var saved = _server.SaveSchema(_client.SessionId!, new SchemaDto(SchemaId.New().ToString(), "Door", true, [new SchemaFieldDto(fieldId.ToString(), "Open", "bool", "false", true, true)]));
        Assert.That(saved.Status, Is.EqualTo(AuthoringStatusCode.Success));
        var renamed = _server.SaveSchema(_client.SessionId!, saved.Schema! with { Fields = [saved.Schema.Fields[0] with { Name = "IsOpen" }] });
        Assert.That(renamed.Schema!.Fields[0].Id, Is.EqualTo(fieldId.ToString()));
        Assert.That(renamed.Schema.Fields[0].Name, Is.EqualTo("IsOpen"));
        Assert.That(_server.ListSchemas().Single().Name, Is.EqualTo("Door"));

        var audit = _server.QueryAudit();
        Assert.That(audit, Is.Not.Null);
    }

    [Test]
    public async Task DraftDiscard_DropsTheDraftAndReturnsLiveSource()
    {
        await _client.ConnectAsync("token_admin", "AdminUser");
        var created = _server.HandleGraphCreate(new GraphCreateRequest(_client.SessionId!, "Live", GraphKind.System, GraphSide.Server));
        var graphId = created.Graph!.Id;
        var live = GraphSerializer.Deserialize(created.SourceJson);
        var draft = new GraphDocument
        {
            Id = live.Id,
            Name = "Dirty",
            Kind = live.Kind,
            Side = live.Side,
            Nodes = live.Nodes
        };
        var saved = await _client.SaveDraftAsync(graphId, RevisionId.Empty, GraphSerializer.Serialize(draft), "draft");
        Assert.That(saved.Status, Is.EqualTo(AuthoringStatusCode.Success));

        var discarded = _server.DiscardDraft(_client.SessionId!, graphId);
        Assert.That(discarded.FromDraft, Is.False);
        Assert.That(GraphSerializer.Deserialize(discarded.DraftJson).Name, Is.EqualTo("Live"));
    }

    [Test]
    public async Task UiSave_KeepsElementIdentityWhenTextChanges()
    {
        await _client.ConnectAsync("token_admin", "AdminUser");
        var buttonId = Guid.NewGuid().ToString("D");
        var saved = _server.SaveUi(_client.SessionId!, new UiDocumentDto(
            GraphId.New().ToString(),
            "Airlock",
            480,
            320,
            new UiNodeDto(Guid.NewGuid().ToString("D"), "Window", "Airlock", "Airlock",
            [
                new UiNodeDto(buttonId, "Button", "Open", "Open", [])
            ])));
        Assert.That(saved.Status, Is.EqualTo(AuthoringStatusCode.Success));
        var edited = saved.Document! with
        {
            Root = saved.Document.Root with
            {
                Children = [saved.Document.Root.Children[0] with { Text = "Cycle" }]
            }
        };
        var again = _server.SaveUi(_client.SessionId!, edited);
        Assert.That(again.Document!.Root.Children[0].Id, Is.EqualTo(buttonId));
        Assert.That(again.Document.Root.Children[0].Text, Is.EqualTo("Cycle"));
        Assert.That(_server.ListUi().Single().Name, Is.EqualTo("Airlock"));
    }

    [Test]
    public async Task UiSave_KeepsStyleBindingAndEvent()
    {
        await _client.ConnectAsync("token_admin", "AdminUser");
        var buttonId = Guid.NewGuid().ToString("D");
        var saved = _server.SaveUi(_client.SessionId!, new UiDocumentDto(
            GraphId.New().ToString(),
            "Airlock",
            480,
            320,
            new UiNodeDto(buttonId, "Button", "Open", "Open", [], StyleClasses: ["danger"], MinWidth: 96),
            [new UiBindingDto(Guid.NewGuid().ToString("D"), buttonId, "Text", "DoorState", "OneWay")],
            [new UiEventDto(Guid.NewGuid().ToString("D"), buttonId, "OnPressed", "CycleDoor")],
            new Dictionary<string, string> { ["DoorState"] = "closed" }));
        Assert.That(saved.Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That(saved.Document!.Root.StyleClasses, Does.Contain("danger"));
        Assert.That(saved.Document.Root.MinWidth, Is.EqualTo(96));
        Assert.That(saved.Document.Bindings!.Single().StateVariable, Is.EqualTo("DoorState"));
        Assert.That(saved.Document.Events!.Single().TargetAction, Is.EqualTo("CycleDoor"));
        Assert.That(saved.Document.LocalState!["DoorState"], Is.EqualTo("closed"));
    }

    [Test]
    public void Profiler_P95UsesTheSlowSamples()
    {
        var graphId = GraphId.New();
        for (var i = 1; i <= 20; i++) _profiler.RecordElapsed(graphId, i);
        var metric = _profiler.GetMetrics(graphId);
        Assert.That(metric.P95Microseconds, Is.GreaterThanOrEqualTo(19));
        var snapshot = _server.CaptureProfiler(graphId, reset: true);
        Assert.That(snapshot.P95Microseconds, Is.GreaterThanOrEqualTo(19));
        Assert.That(_profiler.GetMetrics(graphId).Invocations, Is.EqualTo(0));
    }

    [Test]
    public async Task BreakpointCondition_PausesOnlyWhenTheRegisterMatches()
    {
        await _client.ConnectAsync("token_admin", "AdminUser");
        var node = NodeId.New();
        var rejected = _server.HandleDebuggerCommand(new DebuggerCommandRequest(_client.SessionId!, GraphId.New(), DebuggerAction.SetBreakpoint, node, "nope"));
        Assert.That(rejected.Status, Is.EqualTo(AuthoringStatusCode.ValidationError));

        var set = _server.HandleDebuggerCommand(new DebuggerCommandRequest(_client.SessionId!, GraphId.New(), DebuggerAction.SetBreakpoint, node, "r0==7"));
        Assert.That(set.Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That(_debugger.ShouldSuspend(node, 1, new[] { AstraValue.FromInt64(3) }), Is.False);
        Assert.That(_debugger.ShouldSuspend(node, 2, new[] { AstraValue.FromInt64(7) }), Is.True);
    }

    [Test]
    public async Task SandboxRun_ReportsTheCompileStage()
    {
        await _client.ConnectAsync("token_admin", "AdminUser");
        var graphId = GraphId.New();
        var json = GraphSerializer.Serialize(CreateTestGraphDocument(graphId, "Sandbox"));
        var run = _server.RunSandbox(_client.SessionId!, json);
        Assert.That(run.Stage, Is.EqualTo("Verify"));
        Assert.That(run.SemanticHash, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public async Task RuntimeStatus_ListsTheOpenProject()
    {
        await _client.ConnectAsync("token_admin", "AdminUser");
        _server.HandleGraphCreate(new GraphCreateRequest(_client.SessionId!, "Visible", GraphKind.System, GraphSide.Server));
        var listed = _server.RuntimeGraphs();
        Assert.That(listed.Any(graph => graph.Name == "Visible"), Is.True);
    }

    [Test]
    public void HandshakeFixture_MatchesTheSharedContract()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "tests", "fixtures", "handshake.json")))
        {
            dir = dir.Parent;
        }

        Assert.That(dir, Is.Not.Null);
        var json = File.ReadAllText(Path.Combine(dir!.FullName, "tests", "fixtures", "handshake.json"));
        var message = JsonSerializer.Deserialize<AuthHandshakeResponseMsg>(json, AuthoringJsonContext.Default);
        Assert.That(message, Is.Not.Null);
        Assert.That(message!.Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That(message.SessionId, Is.EqualTo("abc"));
        Assert.That(message.ProtocolVersion, Is.EqualTo(1));
        Assert.That(message.Permissions, Is.EqualTo(AstraPermission.ViewGraphs));
    }

    [Test]
    public async Task GraphDisable_KeepsTheSourceAndMarksTheSummary()
    {
        await _client.ConnectAsync("token_admin", "AdminUser");
        var created = _server.HandleGraphCreate(new GraphCreateRequest(_client.SessionId!, "Door", GraphKind.System, GraphSide.Server));
        var disabled = _server.HandleGraphDisable(created.Graph!.Id, _client.SessionId!, disabled: true);
        Assert.That(disabled.Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That(disabled.Graph!.Status, Is.EqualTo("Disabled"));

        var listed = _server.HandleGraphList(new GraphListRequest(_client.SessionId!));
        Assert.That(listed.Graphs.Single(graph => graph.Id == created.Graph.Id).Status, Is.EqualTo("Disabled"));

        var fetched = _server.HandleGraphFetch(new GraphFetchRequest(_client.SessionId!, created.Graph.Id));
        Assert.That(fetched.Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That(fetched.DraftJson, Does.Contain("Door"));

        var enabled = _server.HandleGraphDisable(created.Graph.Id, _client.SessionId!, disabled: false);
        Assert.That(enabled.Graph!.Status, Is.EqualTo("Live"));
    }

    [Test]
    public async Task SchemaSave_StoresStructAndEnum()
    {
        await _client.ConnectAsync("token_admin", "AdminUser");
        var structure = _server.SaveSchema(_client.SessionId!, new SchemaDto(
            SchemaId.New().ToString(),
            "DoorSpan",
            false,
            [new SchemaFieldDto(FieldId.New().ToString(), "Ticks", "int32", "0", false, false)],
            "Struct"));
        Assert.That(structure.Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That(structure.Schema!.Kind, Is.EqualTo("Struct"));
        Assert.That(structure.Schema.IsComponent, Is.False);

        var enumerated = _server.SaveSchema(_client.SessionId!, new SchemaDto(
            SymbolId.New().ToString(),
            "DoorState",
            false,
            [],
            "Enum",
            ["Closed", "Open"]));
        Assert.That(enumerated.Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That(enumerated.Schema!.Members, Is.EqualTo(new[] { "Closed", "Open" }));
        Assert.That(_server.ListSchemas().Select(schema => schema.Kind), Does.Contain("Struct").And.Contain("Enum"));
    }

    [Test]
    public async Task SchemaSave_ReportsPreserveWhenAFieldIsRenamed()
    {
        await _client.ConnectAsync("token_admin", "AdminUser");
        var schemaId = SchemaId.New();
        var fieldId = FieldId.New();
        var first = _server.SaveSchema(_client.SessionId!, new SchemaDto(
            schemaId.ToString(),
            "Door",
            true,
            [new SchemaFieldDto(fieldId.ToString(), "State", "int32", "0", true, false)]));
        Assert.That(first.Status, Is.EqualTo(AuthoringStatusCode.Success));

        var renamed = _server.SaveSchema(_client.SessionId!, new SchemaDto(
            schemaId.ToString(),
            "Door",
            true,
            [new SchemaFieldDto(fieldId.ToString(), "DoorState", "int32", "0", true, false)]));
        Assert.That(renamed.Changes.Single().Kind, Is.EqualTo("Preserve"));
        Assert.That(renamed.Changes.Single().Detail, Does.Contain("DoorState"));
    }

    [Test]
    public async Task LastKnownGood_RollsBackToThePreviousPublish()
    {
        await _client.ConnectAsync("token_admin", "AdminUser");
        var graphId = GraphId.New();
        var baseRev = RevisionId.New();
        _server.RegisterGraph(new GraphSummaryDto(graphId, "Door", GraphKind.System, GraphSide.Server, baseRev, 1));
        var firstJson = GraphSerializer.Serialize(CreateTestGraphDocument(graphId, "Door"));
        var first = await _client.PublishDraftAsync(graphId, baseRev, firstJson, "v1");
        Assert.That(first.Status, Is.EqualTo(AuthoringStatusCode.Success));

        var secondJson = GraphSerializer.Serialize(CreateTestGraphDocument(graphId, "Door v2"));
        var second = await _client.PublishDraftAsync(graphId, first.PublishedRevision!.Value, secondJson, "v2");
        Assert.That(second.Status, Is.EqualTo(AuthoringStatusCode.Success));

        var restored = await _server.RestoreLastKnownGoodAsync(_client.SessionId!, graphId);
        Assert.That(restored.Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That(restored.ActivatedRevision, Is.EqualTo(first.PublishedRevision));
    }

    [Test]
    public async Task SharedPublish_AssignsAnActivationTick()
    {
        await _client.ConnectAsync("token_admin", "AdminUser");
        var graphId = GraphId.New();
        var baseRev = RevisionId.New();
        _server.RegisterGraph(new GraphSummaryDto(graphId, "Predicted", GraphKind.System, GraphSide.Shared, baseRev, 1));
        var published = await _client.PublishDraftAsync(graphId, baseRev, GraphSerializer.Serialize(CreateTestGraphDocument(graphId, "Predicted", GraphSide.Shared)), "shared");
        Assert.That(published.Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That(published.ActivationTick, Is.GreaterThan(0));
    }

    [Test]
    public async Task UiCompile_ReportsADuplicateElement()
    {
        await _client.ConnectAsync("token_admin", "AdminUser");
        var sharedId = Guid.NewGuid().ToString("D");
        var compiled = _server.CompileUi(_client.SessionId!, new UiDocumentDto(
            GraphId.New().ToString(),
            "Airlock",
            320,
            240,
            new UiNodeDto(sharedId, "Window", "Airlock", "Airlock",
            [
                new UiNodeDto(sharedId, "Button", "Open", "Open", [])
            ])));
        Assert.That(compiled.Status, Is.EqualTo(AuthoringStatusCode.ValidationError));
        Assert.That(compiled.Diagnostics.Any(item => item.Code == "UI0005"), Is.True);
    }

    [Test]
    public void RuntimeInspect_ListsEntityFieldsAndPendingBytes()
    {
        var field = new SchemaField(FieldId.New(), "State", PrimitiveType.Int32, "0", SchemaFieldOptions.Replicated);
        var schema = new SchemaType(SchemaId.New(), "Door", true, [field]);
        var storage = _host.Components.AddComponent(7, schema);
        storage.SetField(0, AstraValue.FromInt64(9));

        var listed = _server.RuntimeEntities();
        Assert.That(listed.Single().EntityId, Is.EqualTo(7));
        Assert.That(listed.Single().Schema, Is.EqualTo("Door"));
        Assert.That(listed.Single().Fields.Single().Value, Is.EqualTo("9"));
        Assert.That(_host.PendingReplicationBytes(), Is.GreaterThan(0));

        var graphId = GraphId.New();
        _profiler.RecordAllocation(graphId, 128);
        var snapshot = _server.CaptureProfiler(graphId, reset: false);
        Assert.That(snapshot.AllocatedBytes, Is.EqualTo(128));
        Assert.That(snapshot.NetworkBytes, Is.GreaterThan(0));
        Assert.That(snapshot.QueryIterations, Is.EqualTo(MixedQueryEngine.Iterations));
    }

    [Test]
    public async Task Rebase_MergesADraftOntoANewerHead()
    {
        await _client.ConnectAsync("token_admin", "AdminUser");
        var graphId = GraphId.New();
        var baseline = CreateTestGraphDocument(graphId, "Door");
        var first = await _client.PublishDraftAsync(graphId, RevisionId.Empty, GraphSerializer.Serialize(baseline), "base");
        Assert.That(first.Status, Is.EqualTo(AuthoringStatusCode.Success));
        var ours = new GraphDocument
        {
            Id = baseline.Id,
            Name = baseline.Name,
            Kind = baseline.Kind,
            Side = baseline.Side,
            Metadata = baseline.Metadata,
            Nodes = baseline.Nodes,
            Variables = [new GraphVariableDocument { Name = "Value", TypeName = "int32" }]
        };
        var saved = _server.HandleDraftSave(new DraftSaveRequest(_client.SessionId!, graphId, first.PublishedRevision!.Value, GraphSerializer.Serialize(ours), "ours"));
        Assert.That(saved.Status, Is.EqualTo(AuthoringStatusCode.Success));
        var renamed = baseline.Nodes[0];
        var theirs = new GraphDocument
        {
            Id = baseline.Id,
            Name = baseline.Name,
            Kind = baseline.Kind,
            Side = baseline.Side,
            Metadata = baseline.Metadata,
            Nodes = [new NodeDocument { Id = renamed.Id, Name = "Theirs", NodeType = renamed.NodeType, Pins = renamed.Pins, Properties = renamed.Properties }]
        };
        var second = await _client.PublishDraftAsync(graphId, first.PublishedRevision.Value, GraphSerializer.Serialize(theirs), "theirs");
        Assert.That(second.Status, Is.EqualTo(AuthoringStatusCode.Success));
        var rebasing = _server.RebaseDraft(_client.SessionId!, graphId);
        Assert.That(rebasing.Status, Is.EqualTo(AuthoringStatusCode.Success), rebasing.ErrorMessage);
        Assert.That(rebasing.DraftJson, Does.Contain("Theirs"));
        Assert.That(rebasing.DraftJson, Does.Contain("Value"));
    }

    [Test]
    public void SemanticMerge_ReportsAConflictWhenBothAuthorsEditTheSameNode()
    {
        var graphId = GraphId.New();
        var baseline = CreateTestGraphDocument(graphId);
        var node = baseline.Nodes[0];
        GraphDocument Copy(string name) => new()
        {
            Id = baseline.Id,
            Name = baseline.Name,
            Kind = baseline.Kind,
            Side = baseline.Side,
            Nodes = [new NodeDocument { Id = node.Id, Name = name, NodeType = node.NodeType, Pins = node.Pins, Properties = node.Properties }]
        };
        var merged = SemanticMerge.Merge(baseline, Copy("Ours"), Copy("Theirs"));
        Assert.That(merged.Document, Is.Null);
        Assert.That(merged.Conflicts, Is.Not.Empty);
    }

    [Test]
    public async Task SchemaSave_AcceptsInterfaceAndListFields()
    {
        await _client.ConnectAsync("token_admin", "AdminUser");
        var saved = _server.SaveSchema(_client.SessionId!, new SchemaDto(
            SchemaId.New().ToString(),
            "DoorContract",
            false,
            [new SchemaFieldDto(FieldId.New().ToString(), "States", "List<int32>", null, false, false)],
            "Interface"));
        Assert.That(saved.Status, Is.EqualTo(AuthoringStatusCode.Success), saved.ErrorMessage);
        Assert.That(saved.Schema!.Kind, Is.EqualTo("Interface"));
        Assert.That(saved.Schema.Fields[0].TypeName, Is.EqualTo("List<int32>"));
    }

    [Test]
    public async Task EntityWatch_ReadsADynamicField()
    {
        await _client.ConnectAsync("token_admin", "AdminUser");
        var field = new SchemaField(FieldId.New(), "State", PrimitiveType.Int32, "0", SchemaFieldOptions.None);
        var schema = new SchemaType(SchemaId.New(), "Door", true, [field]);
        _host.Components.AddComponent(7, schema).SetField(0, AstraValue.FromInt64(9));
        _server.RememberWatch(_client.SessionId!, "State", "entity:7.Door.State", false);
        var inspect = _server.InspectDebugger(_client.SessionId!);
        Assert.That(inspect.Watches.Single().Value, Is.EqualTo("9"));
    }

    [Test]
    public async Task SharedPublish_UsesTheHostTickPlusLead()
    {
        await _client.ConnectAsync("token_admin", "AdminUser");
        _host.Update(1, 10);
        var graphId = GraphId.New();
        var published = await _client.PublishDraftAsync(graphId, RevisionId.Empty, GraphSerializer.Serialize(CreateTestGraphDocument(graphId, "SharedDoor", GraphSide.Shared)), "shared");
        Assert.That(published.Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That(published.ActivationTick, Is.EqualTo(14));
        Assert.That(published.ClientCount, Is.EqualTo(1));
    }

    [Test]
    public async Task UiCompile_MountsControlsAndReportsLayout()
    {
        await _client.ConnectAsync("token_admin", "AdminUser");
        var compiled = _server.CompileUi(_client.SessionId!, new UiDocumentDto(
            GraphId.New().ToString(),
            "Panel",
            200,
            120,
            new UiNodeDto(Guid.NewGuid().ToString("D"), "Button", "Open", "Open", [], ValueSource: "Binding")));
        Assert.That(compiled.Status, Is.EqualTo(AuthoringStatusCode.Success), compiled.ErrorMessage);
        Assert.That(compiled.ElementCount, Is.EqualTo(1));
        Assert.That(compiled.Mounted, Does.Contain("Button:Binding"));
    }

    [Test]
    public async Task ProfilerSubscribe_ReturnsNodeTime()
    {
        await _client.ConnectAsync("token_admin", "AdminUser");
        var node = NodeId.New();
        _profiler.RecordNodeTime(node, 15);
        var graphId = GraphId.New();
        var subscribed = _server.SubscribeProfiler(_client.SessionId!, graphId, false);
        Assert.That(subscribed.Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That(subscribed.Snapshot!.Hottest.Any(item => item.NodeId == node.ToString() && item.Microseconds >= 15), Is.True);
    }

    [Test]
    public void CompileFixture_MatchesTheSharedContract()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "tests", "fixtures", "compile-response.json")))
        {
            dir = dir.Parent;
        }

        Assert.That(dir, Is.Not.Null);
        var json = File.ReadAllText(Path.Combine(dir!.FullName, "tests", "fixtures", "compile-response.json"));
        var message = JsonSerializer.Deserialize<DraftCompileResponseMsg>(json, AuthoringJsonContext.Default);
        Assert.That(message, Is.Not.Null);
        Assert.That(message!.Status, Is.EqualTo(AuthoringStatusCode.Success));
        Assert.That(message.HasErrors, Is.False);
        Assert.That(message.Stage, Is.EqualTo("Verify"));
    }
}
