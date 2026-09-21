namespace AstraGraph.Core;

/// <summary>
/// Abstract base class for all nodes in the typed Abstract Syntax Tree (AST).
/// </summary>
public abstract record AstNode(NodeId? SourceNodeId = null);

/// <summary>
/// Abstract base class for AST expressions that produce a typed value.
/// </summary>
public abstract record AstExpression(AstraType Type, NodeId? SourceNodeId = null) : AstNode(SourceNodeId);

/// <summary>
/// Abstract base class for AST statements that control execution flow or mutate state.
/// </summary>
public abstract record AstStatement(NodeId? SourceNodeId = null) : AstNode(SourceNodeId);
