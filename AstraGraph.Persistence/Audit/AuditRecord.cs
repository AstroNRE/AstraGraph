using System;
using System.Collections.Generic;

namespace AstraGraph.Persistence.Audit;

/// <summary>
/// Immutable audit entry representing an operation performed on graphs, revisions, or storage.
/// </summary>
public sealed record AuditRecord(
    DateTimeOffset Timestamp,
    string Action,
    Guid? GraphId,
    Guid? RevisionId,
    string Author,
    string Message,
    string? SourceHash,
    bool Success,
    IReadOnlyDictionary<string, string>? Details = null);
