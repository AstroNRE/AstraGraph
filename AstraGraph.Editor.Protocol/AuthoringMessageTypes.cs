using System.Text.Json.Serialization;
using AstraGraph.Core;
using AstraGraph.Runtime.Security;

namespace AstraGraph.Editor.Protocol;

/// <summary>
/// Base class for all strongly-typed authoring messages transported over WebSocket.
/// Each message has a Kind discriminator for routing/deserialization.
/// </summary>
public abstract class AuthoringMessage
{
    public abstract string Kind { get; }
    public string MessageId { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public static class AuthoringProtocol
{
    public const int Version = 1;
}

public sealed class AuthHandshakeRequestMsg : AuthoringMessage
{
    public override string Kind => "auth.handshake.request";
    public string ClientVersion { get; init; } = string.Empty;
    public int ProtocolVersion { get; init; }
    public string AuthorToken { get; init; } = string.Empty;
    public string AuthorName { get; init; } = string.Empty;
}

public sealed class AuthHandshakeResponseMsg : AuthoringMessage
{
    public override string Kind => "auth.handshake.response";
    public AuthoringStatusCode Status { get; init; }
    public string SessionId { get; init; } = string.Empty;
    public AstraPermission Permissions { get; init; }
    public string? ErrorMessage { get; init; }
    public int ProtocolVersion { get; init; }
    public bool CanCompile { get; init; }
    public bool CanPublish { get; init; }
    public bool CanDebug { get; init; }
    public bool CanProfile { get; init; }
    public bool HasBindingCatalog { get; init; }
    public bool HasNativeEngine { get; init; }
    public bool HasPrediction { get; init; }
    public bool HasBui { get; init; }
}

public sealed class GraphListRequestMsg : AuthoringMessage
{
    public override string Kind => "graph.list.request";
    public string SessionId { get; init; } = string.Empty;
}

public sealed class GraphListResponseMsg : AuthoringMessage
{
    public override string Kind => "graph.list.response";
    public AuthoringStatusCode Status { get; init; }
    public IReadOnlyList<GraphSummaryDto> Graphs { get; init; } = Array.Empty<GraphSummaryDto>();
    public string? ErrorMessage { get; init; }
}

public sealed class DraftCompileRequestMsg : AuthoringMessage
{
    public override string Kind => "draft.compile.request";
    public string SessionId { get; init; } = string.Empty;
    public GraphId GraphId { get; init; }
    public string DraftJson { get; init; } = string.Empty;
}

public sealed class DraftCompileResponseMsg : AuthoringMessage
{
    public override string Kind => "draft.compile.response";
    public AuthoringStatusCode Status { get; init; }
    public bool HasErrors { get; init; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = Array.Empty<Diagnostic>();
    public string? BytecodeHash { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class DraftPublishRequestMsg : AuthoringMessage
{
    public override string Kind => "draft.publish.request";
    public string SessionId { get; init; } = string.Empty;
    public GraphId GraphId { get; init; }
    public RevisionId BaseRevisionId { get; init; }
    public string DraftJson { get; init; } = string.Empty;
    public string PublishMessage { get; init; } = string.Empty;
}

public sealed class DraftPublishResponseMsg : AuthoringMessage
{
    public override string Kind => "draft.publish.response";
    public AuthoringStatusCode Status { get; init; }
    public RevisionId? PublishedRevision { get; init; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = Array.Empty<Diagnostic>();
    public string? ErrorMessage { get; init; }
}

public sealed class DraftSaveRequestMsg : AuthoringMessage
{
    public override string Kind => "draft.save.request";
    public string SessionId { get; init; } = string.Empty;
    public GraphId GraphId { get; init; }
    public RevisionId BaseRevisionId { get; init; }
    public string DraftJson { get; init; } = string.Empty;
    public string AuthorMessage { get; init; } = string.Empty;
}

public sealed class DraftSaveResponseMsg : AuthoringMessage
{
    public override string Kind => "draft.save.response";
    public AuthoringStatusCode Status { get; init; }
    public RevisionId? DraftRevisionId { get; init; }
    public bool HasConflict { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class GraphFetchRequestMsg : AuthoringMessage
{
    public override string Kind => "graph.fetch.request";
    public string SessionId { get; init; } = string.Empty;
    public GraphId GraphId { get; init; }
}

public sealed class GraphFetchResponseMsg : AuthoringMessage
{
    public override string Kind => "graph.fetch.response";
    public AuthoringStatusCode Status { get; init; }
    public string DraftJson { get; init; } = string.Empty;
    public RevisionId BaseRevisionId { get; init; }
    public RevisionId ActiveRevisionId { get; init; }
    public bool FromDraft { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class GraphCreateRequestMsg : AuthoringMessage
{
    public override string Kind => "graph.create.request";
    public string SessionId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    [JsonPropertyName("kind")]
    public GraphKind DocumentKind { get; init; } = GraphKind.System;
    public GraphSide Side { get; init; } = GraphSide.Server;
}

public sealed class GraphCreateResponseMsg : AuthoringMessage
{
    public override string Kind => "graph.create.response";
    public AuthoringStatusCode Status { get; init; }
    public GraphSummaryDto? Graph { get; init; }
    public string SourceJson { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
}

public sealed class GraphRenameRequestMsg : AuthoringMessage
{
    public override string Kind => "graph.rename.request";
    public string SessionId { get; init; } = string.Empty;
    public GraphId GraphId { get; init; }
    public string Name { get; init; } = string.Empty;
}

public sealed class GraphRenameResponseMsg : AuthoringMessage
{
    public override string Kind => "graph.rename.response";
    public AuthoringStatusCode Status { get; init; }
    public GraphSummaryDto? Graph { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class GraphDeleteRequestMsg : AuthoringMessage
{
    public override string Kind => "graph.delete.request";
    public string SessionId { get; init; } = string.Empty;
    public GraphId GraphId { get; init; }
}

public sealed class GraphDeleteResponseMsg : AuthoringMessage
{
    public override string Kind => "graph.delete.response";
    public AuthoringStatusCode Status { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class BindingMaterializeRequestMsg : AuthoringMessage
{
    public override string Kind => "binding.materialize.request";
    public string SessionId { get; init; } = string.Empty;
    public string BindingId { get; init; } = string.Empty;
}

public sealed class BindingMaterializeResponseMsg : AuthoringMessage
{
    public override string Kind => "binding.materialize.response";
    public AuthoringStatusCode Status { get; init; }
    public string NodeJson { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
}

public sealed class ConnectionValidateRequestMsg : AuthoringMessage
{
    public override string Kind => "connection.validate.request";
    public string SessionId { get; init; } = string.Empty;
    public PinWire? Source { get; init; }
    public PinWire? Target { get; init; }
    public IReadOnlyList<WireEnds> Connections { get; init; } = [];
}

public sealed class ConnectionValidateResponseMsg : AuthoringMessage
{
    public override string Kind => "connection.validate.response";
    public AuthoringStatusCode Status { get; init; }
    public bool Valid { get; init; }
    public string? Reason { get; init; }
}

public sealed class ConnectionCompatibleRequestMsg : AuthoringMessage
{
    public override string Kind => "connection.compatible.request";
    public string SessionId { get; init; } = string.Empty;
    public PinWire? Source { get; init; }
    public IReadOnlyList<PinWire> Candidates { get; init; } = [];
    public IReadOnlyList<WireEnds> Connections { get; init; } = [];
}

public sealed class ConnectionCompatibleResponseMsg : AuthoringMessage
{
    public override string Kind => "connection.compatible.response";
    public AuthoringStatusCode Status { get; init; }
    public IReadOnlyList<PinCompatibilityDto> Pins { get; init; } = [];
    public string? Reason { get; init; }
}

public sealed class ConnectionSuggestRequestMsg : AuthoringMessage
{
    public override string Kind => "connection.suggest.request";
    public string SessionId { get; init; } = string.Empty;
    public PinWire? Pin { get; init; }
    public string? Query { get; init; }
}

public sealed class ConnectionSuggestResponseMsg : AuthoringMessage
{
    public override string Kind => "connection.suggest.response";
    public AuthoringStatusCode Status { get; init; }
    public IReadOnlyList<ConnectionSuggestionDto> Suggestions { get; init; } = [];
    public string? Reason { get; init; }
}

public sealed class CatalogQueryRequestMsg : AuthoringMessage
{
    public override string Kind => "catalog.query.request";
    public string SessionId { get; init; } = string.Empty;
    public string? SearchFilter { get; init; }
}

public sealed class CatalogQueryResponseMsg : AuthoringMessage
{
    public override string Kind => "catalog.query.response";
    public AuthoringStatusCode Status { get; init; }
    public IReadOnlyList<CatalogEntryDto> Entries { get; init; } = [];
    public string? ErrorMessage { get; init; }
}

public sealed class HistoryDiffRequestMsg : AuthoringMessage
{
    public override string Kind => "history.diff.request";
    public string SessionId { get; init; } = string.Empty;
    public GraphId GraphId { get; init; }
    public RevisionId RevisionId { get; init; }
    public string DraftJson { get; init; } = string.Empty;
}

public sealed class HistoryDiffResponseMsg : AuthoringMessage
{
    public override string Kind => "history.diff.response";
    public AuthoringStatusCode Status { get; init; }
    public IReadOnlyList<SemanticChangeDto> Changes { get; init; } = [];
    public string? ErrorMessage { get; init; }
}

public sealed class HistoryListRequestMsg : AuthoringMessage
{
    public override string Kind => "history.list.request";
    public string SessionId { get; init; } = string.Empty;
    public GraphId GraphId { get; init; }
}

public sealed class HistoryListResponseMsg : AuthoringMessage
{
    public override string Kind => "history.list.response";
    public AuthoringStatusCode Status { get; init; }
    public IReadOnlyList<string> Revisions { get; init; } = [];
    public IReadOnlyList<RevisionSummaryDto> Records { get; init; } = [];
    public string? ErrorMessage { get; init; }
}

public sealed class HistoryRollbackRequestMsg : AuthoringMessage
{
    public override string Kind => "history.rollback.request";
    public string SessionId { get; init; } = string.Empty;
    public GraphId GraphId { get; init; }
    public RevisionId TargetRevisionId { get; init; }
}

public sealed class HistoryRollbackResponseMsg : AuthoringMessage
{
    public override string Kind => "history.rollback.response";
    public AuthoringStatusCode Status { get; init; }
    public GraphId GraphId { get; init; }
    public RevisionId ActiveRevision { get; init; }
    public RevisionId ActivatedRevision { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class SessionUpdatedMsg : AuthoringMessage
{
    public override string Kind => "session.updated";
    public string SessionId { get; init; } = string.Empty;
    public AstraPermission Permissions { get; init; }
}

public sealed class DebuggerCommandRequestMsg : AuthoringMessage
{
    public override string Kind => "debugger.command.request";
    public string SessionId { get; init; } = string.Empty;
    public GraphId GraphId { get; init; }
    public DebuggerAction Action { get; init; }
    public NodeId? TargetNode { get; init; }
}

public sealed class DebuggerCommandResponseMsg : AuthoringMessage
{
    public override string Kind => "debugger.command.response";
    public AuthoringStatusCode Status { get; init; }
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class DebugStreamEventMsg : AuthoringMessage
{
    public override string Kind => "debug.stream.event";
    public GraphId GraphId { get; init; }
    public NodeId CurrentNode { get; init; }
    public string Action { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Locals { get; init; } = new Dictionary<string, string>();
    public long TimestampTicks { get; init; }
}

public sealed class ProfilerStreamEventMsg : AuthoringMessage
{
    public override string Kind => "profiler.stream.event";
    public GraphId GraphId { get; init; }
    public IReadOnlyDictionary<NodeId, double> NodeExecutionTimeMs { get; init; } = new Dictionary<NodeId, double>();
    public long TotalInvocations { get; init; }
}

public sealed class PingMsg : AuthoringMessage
{
    public override string Kind => "ping";
}

public sealed class PongMsg : AuthoringMessage
{
    public override string Kind => "pong";
}

public sealed class ErrorMsg : AuthoringMessage
{
    public override string Kind => "error";
    public string Code { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

/// <summary>
/// Source-generated JSON serialization context for AuthoringMessage hierarchy.
/// </summary>
/// <summary>
/// Shared JSON serialization settings for all AuthoringMessage types.
/// Uses runtime options (not source-generated) because Core enums carry
/// [JsonConverter(typeof(JsonStringEnumConverter))] which is incompatible with AOT source gen.
/// </summary>
public static class AuthoringJsonContext
{
    public static readonly System.Text.Json.JsonSerializerOptions Default = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    // Typed accessors for symmetry with the original source-gen pattern
    public static System.Text.Json.JsonSerializerOptions AuthHandshakeResponseMsg => Default;
    public static System.Text.Json.JsonSerializerOptions AuthHandshakeRequestMsg => Default;
    public static System.Text.Json.JsonSerializerOptions GraphListRequestMsg => Default;
    public static System.Text.Json.JsonSerializerOptions GraphListResponseMsg => Default;
    public static System.Text.Json.JsonSerializerOptions DraftCompileRequestMsg => Default;
    public static System.Text.Json.JsonSerializerOptions DraftCompileResponseMsg => Default;
    public static System.Text.Json.JsonSerializerOptions DraftPublishRequestMsg => Default;
    public static System.Text.Json.JsonSerializerOptions DraftPublishResponseMsg => Default;
    public static System.Text.Json.JsonSerializerOptions DebuggerCommandRequestMsg => Default;
    public static System.Text.Json.JsonSerializerOptions DebuggerCommandResponseMsg => Default;
    public static System.Text.Json.JsonSerializerOptions DebugStreamEventMsg => Default;
    public static System.Text.Json.JsonSerializerOptions ProfilerStreamEventMsg => Default;
    public static System.Text.Json.JsonSerializerOptions PingMsg => Default;
    public static System.Text.Json.JsonSerializerOptions PongMsg => Default;
    public static System.Text.Json.JsonSerializerOptions ErrorMsg => Default;
}
