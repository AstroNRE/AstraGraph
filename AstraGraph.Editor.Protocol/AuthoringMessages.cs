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
    InternalError,
    IncompatibleVersion
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
    string? ErrorMessage = null,
    RevisionId? ActivatedRevision = null);

public sealed record GraphCreateRequest(
    string SessionId,
    string Name,
    GraphKind Kind,
    GraphSide Side);

public sealed record GraphCreateResponse(
    AuthoringStatusCode Status,
    GraphSummaryDto? Graph,
    string SourceJson,
    string? ErrorMessage = null);

public sealed record GraphRenameRequest(
    string SessionId,
    GraphId GraphId,
    string Name);

public sealed record GraphDeleteRequest(
    string SessionId,
    GraphId GraphId);

public sealed record GraphMutationResponse(
    AuthoringStatusCode Status,
    GraphSummaryDto? Graph,
    string? ErrorMessage = null);

public sealed record GraphFetchRequest(
    string SessionId,
    GraphId GraphId);

public sealed record GraphFetchResponse(
    AuthoringStatusCode Status,
    string DraftJson,
    RevisionId BaseRevisionId,
    RevisionId ActiveRevisionId,
    bool FromDraft,
    string? ErrorMessage = null);

public sealed record PinWire(
    string Id,
    string NodeId,
    string Name,
    string Direction,
    string Kind,
    string DataType);

public sealed record WireEnds(string SourcePinId, string TargetPinId);

public sealed record PinCompatibilityDto(string PinId, bool Valid, string? Reason);

public sealed record ConnectionSuggestionDto(
    string BindingId,
    string NodeType,
    string DisplayName,
    string PinName,
    int Score);

public sealed record SemanticChangeDto(string Kind, string Detail);

public sealed record RevisionSummaryDto(
    string RevisionId,
    string Author,
    string Timestamp,
    string Message,
    string SemanticHash);

public sealed record CatalogParameterDto(
    string Name,
    string TypeName,
    string Direction);

public sealed record CatalogEntryDto(
    string Signature,
    string Category,
    bool IsPure,
    bool IsPredictionSafe,
    GraphSide Side,
    string Documentation,
    string BindingId,
    string DeclaringType,
    string MethodName,
    IReadOnlyList<CatalogParameterDto> Parameters,
    string ReturnType,
    string SecurityProfile,
    int Cost,
    bool IsObsolete);

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
