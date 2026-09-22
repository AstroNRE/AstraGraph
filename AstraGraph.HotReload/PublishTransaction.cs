using AstraGraph.Core;
using AstraGraph.Core.Events;

namespace AstraGraph.HotReload;

public enum PublishTransactionStatus
{
    Queued,
    Applying,
    Committed,
    RolledBack,
    Faulted
}

/// <summary>
/// Transactional unit representing a prepared graph publication queued for execution at a simulation tick boundary.
/// </summary>
public sealed class PublishTransaction
{
    public Guid Id { get; } = Guid.NewGuid();
    public GraphId GraphId { get; }
    public GraphDocument Draft { get; }
    public BytecodeProgram PreparedProgram { get; }
    public RevisionId NewRevisionId { get; }
    public RevisionId? ParentRevisionId { get; }
    public string SemanticHash { get; }
    public string Author { get; }
    public string Message { get; }
    public SchemaType? DeclaredSchema { get; }
    public SchemaMigrationPlan? MigrationPlan { get; }
    public IReadOnlyList<GraphEventSubscription> Subscriptions { get; }
    public TaskCompletionSource<PublishResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public PublishTransactionStatus Status { get; set; } = PublishTransactionStatus.Queued;

    public PublishTransaction(
        GraphId graphId,
        GraphDocument draft,
        BytecodeProgram preparedProgram,
        RevisionId newRevisionId,
        RevisionId? parentRevisionId,
        string semanticHash,
        string author,
        string message,
        SchemaType? declaredSchema,
        SchemaMigrationPlan? migrationPlan,
        IReadOnlyList<GraphEventSubscription>? subscriptions = null)
    {
        GraphId = graphId;
        Draft = draft;
        PreparedProgram = preparedProgram;
        NewRevisionId = newRevisionId;
        ParentRevisionId = parentRevisionId;
        SemanticHash = semanticHash;
        Author = author;
        Message = message;
        DeclaredSchema = declaredSchema;
        MigrationPlan = migrationPlan;
        Subscriptions = subscriptions ?? [];
    }
}
