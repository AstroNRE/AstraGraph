using AstraGraph.Core;

namespace AstraGraph.HotReload;

public sealed record RevisionRecord(
    GraphId GraphId,
    RevisionId RevisionId,
    RevisionId? ParentRevisionId,
    string SemanticHash,
    string Author,
    DateTimeOffset Timestamp,
    string Message,
    BytecodeProgram Program);

public sealed record PublishResult(
    bool Success,
    RevisionId? PublishedRevisionId,
    DiagnosticBag Diagnostics,
    string? ErrorMessage = null);
