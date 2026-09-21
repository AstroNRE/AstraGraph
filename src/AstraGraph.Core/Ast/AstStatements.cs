namespace AstraGraph.Core;

public enum ContinuationKind
{
    Delay,
    WaitUntil,
    AwaitEvent,
    DoAfter
}

public sealed record AstBlock(
    IReadOnlyList<AstStatement> Statements,
    NodeId? SourceNodeId = null) : AstStatement(SourceNodeId);

public sealed record AstEntryPointStatement(
    string Name,
    IReadOnlyList<AstVariableDeclaration> Parameters,
    AstBlock Body,
    NodeId? SourceNodeId = null) : AstStatement(SourceNodeId);

public sealed record AstVariableDeclaration(
    SymbolId Id,
    string Name,
    AstraType Type);

public sealed record AstVariableAssignStatement(
    SymbolId VariableId,
    string Name,
    AstExpression Value,
    NodeId? SourceNodeId = null) : AstStatement(SourceNodeId);

public sealed record AstBranchStatement(
    AstExpression Condition,
    AstBlock TrueBlock,
    AstBlock? FalseBlock = null,
    NodeId? SourceNodeId = null) : AstStatement(SourceNodeId);

public sealed record AstLoopStatement(
    AstExpression Condition,
    AstBlock Body,
    NodeId? SourceNodeId = null) : AstStatement(SourceNodeId);

public sealed record AstReturnStatement(
    AstExpression? Value = null,
    NodeId? SourceNodeId = null) : AstStatement(SourceNodeId);

public sealed record AstYieldContinuationStatement(
    ContinuationKind Kind,
    IReadOnlyList<AstExpression> Arguments,
    Guid ResumePointId,
    NodeId? SourceNodeId = null) : AstStatement(SourceNodeId);

public sealed record AstExpressionStatement(
    AstExpression Expression,
    NodeId? SourceNodeId = null) : AstStatement(SourceNodeId);

public sealed record AstSetComponentFieldStatement(
    AstExpression Entity,
    SchemaType Schema,
    FieldId FieldId,
    AstExpression Value,
    NodeId? SourceNodeId = null) : AstStatement(SourceNodeId);
