namespace AstraGraph.Core;

/// <summary>
/// Result of semantic analysis and AST lowering of a GraphDocument.
/// </summary>
public sealed record SemanticResult(
    AstProgram? Program,
    DiagnosticBag Diagnostics)
{
    public bool Success => !Diagnostics.HasErrors && Program is not null;
}
