using System;
using System.IO;
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

    private static GraphDocument CreateTestGraphDocument(GraphId graphId, string name = "ProtoTestGraph")
    {
        var entryId = NodeId.New();
        var execOut = PinId.New();

        return new GraphDocument
        {
            Id = graphId,
            Name = name,
            Kind = GraphKind.System,
            Side = GraphSide.Server,
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
}
