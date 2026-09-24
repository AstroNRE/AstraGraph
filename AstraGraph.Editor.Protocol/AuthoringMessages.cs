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
    StepInto,
    StepOut
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
    int RevisionCount,
    string Status = "Live",
    bool HasDraft = false,
    string Owner = "",
    string Tags = "",
    string OverrideOf = "");

public sealed record GraphListRequest(string SessionId);

public sealed record GraphListResponse(
    AuthoringStatusCode Status,
    IReadOnlyList<GraphSummaryDto> Graphs,
    string? ErrorMessage = null,
    int ClientCount = 0,
    string MigrationSummary = "No schema migration");

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
    string? ErrorMessage = null,
    string Stage = "Verify");

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
    string? ErrorMessage = null,
    int ActivationTick = 0,
    string? MigrationSummary = null,
    int ClientCount = 0);

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
    string SemanticHash,
    string ParentRevisionId = "",
    int ActivationTick = 0);

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
    NodeId? TargetNode = null,
    string? Condition = null);

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

public sealed record DebugValueDto(string Name, string Expression, string Value);

public sealed record DebugTraceDto(string NodeId, int InstructionPointer);

public sealed record ProfilerNodeDto(string NodeId, long Hits, double Microseconds = 0);

public sealed record ProfilerSnapshotDto(
    long Invocations,
    double AverageMicroseconds,
    long Instructions,
    long NativeCalls,
    long Yields,
    IReadOnlyList<ProfilerNodeDto> Hottest,
    double P95Microseconds = 0,
    long BudgetViolations = 0,
    long AllocatedBytes = 0,
    int NetworkBytes = 0,
    long QueryIterations = 0,
    IReadOnlyList<double>? Samples = null);

public sealed record RuntimeFieldDto(string Name, string Value);

public sealed record RuntimeEntityDto(int EntityId, string Schema, IReadOnlyList<RuntimeFieldDto> Fields);

public sealed record SandboxRunDto(string Stage, string? SemanticHash, string? Result, bool HasErrors);

public sealed record RuntimeGraphDto(string Id, string Name, string ActiveRevision, int RevisionCount);

public sealed record AuditEntryDto(
    string Timestamp,
    string Action,
    string Author,
    string Message,
    bool Success,
    string? GraphId);

public sealed record SchemaFieldDto(
    string Id,
    string Name,
    string TypeName,
    string? DefaultValue,
    bool Persistent,
    bool Replicated);

public sealed record UiNodeDto(
    string Id,
    string ElementType,
    string? Name,
    string? Text,
    IReadOnlyList<UiNodeDto> Children,
    bool Visible = true,
    bool Enabled = true,
    string Orientation = "Vertical",
    int? MinWidth = null,
    int? MinHeight = null,
    IReadOnlyList<string>? StyleClasses = null,
    string ValueSource = "Constant",
    string? ControlTypeId = null,
    IReadOnlyDictionary<string, string>? Properties = null);

public sealed record UiBindingDto(
    string BindingId,
    string ElementId,
    string TargetProperty,
    string StateVariable,
    string Direction);

public sealed record UiEventDto(
    string SubscriptionId,
    string ElementId,
    string EventName,
    string TargetAction);

public sealed record UiDocumentDto(
    string Id,
    string Name,
    int Width,
    int Height,
    UiNodeDto Root,
    IReadOnlyList<UiBindingDto>? Bindings = null,
    IReadOnlyList<UiEventDto>? Events = null,
    IReadOnlyDictionary<string, string>? LocalState = null,
    IReadOnlyList<UiStateVariableDto>? StateVariables = null,
    string DocumentKind = "UI",
    string? Css = null);

public sealed record UiStateVariableDto(
    string Id,
    string Name,
    string TypeName,
    string? DefaultValue = null,
    string Scope = "Local");

public sealed record UiControlDescriptorDto(
    string TypeId,
    string DisplayName,
    string Category,
    bool CanHaveChildren,
    IReadOnlyList<UiPropertyDescriptorDto> Properties,
    IReadOnlyList<UiEventDescriptorDto> Events);

public sealed record UiPropertyDescriptorDto(
    string Name,
    string TypeName,
    string EditorKind,
    bool CanWrite,
    string? Category = null,
    string? DefaultValue = null,
    IReadOnlyList<string>? EnumValues = null);

public sealed record UiEventDescriptorDto(
    string Name,
    string EventType,
    IReadOnlyList<string> Payload);

public sealed record SchemaDto(
    string Id,
    string Name,
    bool IsComponent,
    IReadOnlyList<SchemaFieldDto> Fields,
    string Kind = "Component",
    IReadOnlyList<string>? Members = null);

public sealed record ProfilerStreamEvent(
    GraphId GraphId,
    IReadOnlyDictionary<NodeId, double> NodeExecutionTimeMs,
    long TotalInvocations);
