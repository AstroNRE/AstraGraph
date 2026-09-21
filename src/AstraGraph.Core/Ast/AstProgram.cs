namespace AstraGraph.Core;

public sealed record AstFunction(
    SymbolId Id,
    string Name,
    IReadOnlyList<AstVariableDeclaration> Parameters,
    AstraType ReturnType,
    AstBlock Body,
    NodeId? SourceNodeId = null);

/// <summary>
/// Root Typed AST representation for a verified and type-checked graph.
/// </summary>
public sealed record AstProgram(
    GraphId Id,
    string Name,
    GraphKind Kind,
    GraphSide Side,
    IReadOnlyList<AstVariableDeclaration> Variables,
    IReadOnlyList<AstEntryPointStatement> EntryPoints,
    IReadOnlyList<AstFunction> Functions);
