using AstraGraph.Core;
using AstraGraph.Core.Events;

namespace AstraGraph.HotReload;

/// <summary>
/// Immutable snapshot record capturing a published graph revision across Code, Schema, and Subscriptions.
/// Enables comprehensive, deterministic rollback without orphaned states or broken schemas.
/// </summary>
public sealed record RevisionRecord(
    GraphId GraphId,
    RevisionId RevisionId,
    RevisionId? ParentRevisionId,
    string SemanticHash,
    string Author,
    DateTimeOffset Timestamp,
    string Message,
    BytecodeProgram Program,
    SchemaType? SchemaSnapshot = null,
    IReadOnlyList<GraphEventSubscription>? Subscriptions = null);

public sealed record PublishResult(
    bool Success,
    RevisionId? PublishedRevisionId,
    DiagnosticBag Diagnostics,
    string? ErrorMessage = null)
{
    public static implicit operator bool(PublishResult result) => result.Success;
}
