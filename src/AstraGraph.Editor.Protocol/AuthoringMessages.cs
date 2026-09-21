using AstraGraph.Core;
using AstraGraph.Runtime.Security;

namespace AstraGraph.Editor.Protocol;

public enum AuthoringStatusCode
{
    Success,
    Unauthorized,
    Conflict,
    ValidationError,
    NotFound,
    InternalError
}

public enum DebuggerAction
{
    SetBreakpoint,
    RemoveBreakpoint,
    Pause,
    Resume,
    StepOver,
    StepInto
}

public sealed record AuthHandshakeRequest(
    string ClientVersion,
    string AuthorToken,
    string AuthorName);

public sealed record AuthHandshakeResponse(
    AuthoringStatusCode Status,
    string SessionId,
    AstraPermission Permissions,
    string? ErrorMessage = null);

public sealed record GraphSummaryDto(
    GraphId Id,
    string Name,
    GraphKind Kind,
    GraphSide Side,
    RevisionId ActiveRevision,
    int RevisionCount);

public sealed record GraphListRequest(string SessionId);

public sealed record GraphListResponse(
    AuthoringStatusCode Status,
    IReadOnlyList<GraphSummaryDto> Graphs,
    string? ErrorMessage = null);

public sealed record DraftSaveRequest(
    string SessionId,
    GraphId GraphId,
    RevisionId BaseRevisionId,
    string DraftJson,
    string AuthorMessage);

public sealed record DraftSaveResponse(
    AuthoringStatusCode Status,
    RevisionId? DraftRevisionId,
    bool HasConflict,
    string? ConflictDetails = null,
    string? ErrorMessage = null);

public sealed record DraftCompileRequest(
    string SessionId,
    GraphId GraphId,
    string DraftJson);

public sealed record DraftCompileResponse(
    AuthoringStatusCode Status,
    bool HasErrors,
    IReadOnlyList<Diagnostic> Diagnostics,
    string? BytecodeHash = null,
    string? ErrorMessage = null);

public sealed record DraftPublishRequest(
    string SessionId,
    GraphId GraphId,
    RevisionId BaseRevisionId,
    string DraftJson,
    string PublishMessage);

public sealed record DraftPublishResponse(
    AuthoringStatusCode Status,
    RevisionId? PublishedRevision,
    IReadOnlyList<Diagnostic> Diagnostics,
    string? ErrorMessage = null);

public sealed record RollbackRequest(
    string SessionId,
    GraphId GraphId,
    RevisionId TargetRevision);

public sealed record RollbackResponse(
    AuthoringStatusCode Status,
    RevisionId CurrentRevision,
    string? ErrorMessage = null);

public sealed record CatalogEntryDto(
    string Signature,
    string Category,
    bool IsPure,
    bool IsPredictionSafe,
    GraphSide Side,
    string Documentation);

public sealed record CatalogQueryRequest(
    string SessionId,
    string? SearchFilter = null,
    GraphSide? TargetSide = null);

public sealed record CatalogQueryResponse(
    AuthoringStatusCode Status,
    IReadOnlyList<CatalogEntryDto> Entries,
    string? ErrorMessage = null);

public sealed record DebuggerCommandRequest(
    string SessionId,
    GraphId GraphId,
    DebuggerAction Action,
    NodeId? TargetNode = null);

public sealed record DebuggerCommandResponse(
    AuthoringStatusCode Status,
    bool IsSuccess,
    string? ErrorMessage = null);

public sealed record DebugStreamEvent(
    GraphId GraphId,
    NodeId CurrentNode,
    string Action,
    IReadOnlyDictionary<string, string> Locals,
    long TimestampTicks);

public sealed record ProfilerStreamEvent(
    GraphId GraphId,
    IReadOnlyDictionary<NodeId, double> NodeExecutionTimeMs,
    long TotalInvocations);
