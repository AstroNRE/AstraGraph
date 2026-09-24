namespace AstraGraph.Core;

public enum AstBinaryOperator
{
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    And,
    Or
}

public enum AstUnaryOperator
{
    Negate,
    Not
}

public sealed record AstLiteralExpression(
    object? Value,
    AstraType Type,
    NodeId? SourceNodeId = null) : AstExpression(Type, SourceNodeId);

public sealed record AstVariableReadExpression(
    SymbolId VariableId,
    string Name,
    AstraType Type,
    NodeId? SourceNodeId = null) : AstExpression(Type, SourceNodeId);

public sealed record AstBinaryExpression(
    AstBinaryOperator Operator,
    AstExpression Left,
    AstExpression Right,
    AstraType Type,
    NodeId? SourceNodeId = null) : AstExpression(Type, SourceNodeId);

public sealed record AstUnaryExpression(
    AstUnaryOperator Operator,
    AstExpression Operand,
    AstraType Type,
    NodeId? SourceNodeId = null) : AstExpression(Type, SourceNodeId);

public sealed record AstMemberAccessExpression(
    AstExpression Target,
    FieldId FieldId,
    string MemberName,
    AstraType Type,
    NodeId? SourceNodeId = null) : AstExpression(Type, SourceNodeId);

public sealed record AstFunctionCallExpression(
    SymbolId FunctionId,
    string FunctionName,
    IReadOnlyList<AstExpression> Arguments,
    AstraType ReturnType,
    NodeId? SourceNodeId = null) : AstExpression(ReturnType, SourceNodeId);

public sealed record AstNativeCallExpression(
    string MethodDescriptor,
    IReadOnlyList<AstExpression> Arguments,
    AstraType ReturnType,
    NodeId? SourceNodeId = null) : AstExpression(ReturnType, SourceNodeId);

public sealed record AstGetComponentExpression(
    AstExpression Entity,
    AstraType ComponentType,
    NodeId? SourceNodeId = null) : AstExpression(ComponentType, SourceNodeId);

public sealed record AstHasComponentExpression(
    AstExpression Entity,
    AstraType ComponentType,
    NodeId? SourceNodeId = null) : AstExpression(PrimitiveType.Bool, SourceNodeId);

public sealed record AstEventContextExpression(
    int Slot,
    AstraType Type,
    NodeId? SourceNodeId = null) : AstExpression(Type, SourceNodeId);

public sealed record AstMemberReadExpression(
    AstExpression Target,
    string MemberName,
    AstraType Type,
    NodeId? SourceNodeId = null) : AstExpression(Type, SourceNodeId);

public sealed record AstFieldReadExpression(
    AstExpression Component,
    string FieldName,
    AstraType Type,
    NodeId? SourceNodeId = null) : AstExpression(Type, SourceNodeId);

public sealed record AstCollectionLengthExpression(
    AstExpression Collection,
    NodeId? SourceNodeId = null) : AstExpression(PrimitiveType.Int32, SourceNodeId);

public sealed record AstCollectionGetExpression(
    AstExpression Collection,
    AstExpression Index,
    AstraType Type,
    NodeId? SourceNodeId = null) : AstExpression(Type, SourceNodeId);

public sealed record AstHasValueExpression(
    AstExpression Value,
    NodeId? SourceNodeId = null) : AstExpression(PrimitiveType.Bool, SourceNodeId);

public sealed record AstStructMakeExpression(
    string SchemaKey,
    AstraType Type,
    NodeId? SourceNodeId = null) : AstExpression(Type, SourceNodeId);

public sealed record AstStructCopyExpression(
    AstExpression Source,
    AstraType Type,
    NodeId? SourceNodeId = null) : AstExpression(Type, SourceNodeId);

public sealed record AstSetFieldExpression(
    AstExpression Target,
    string FieldKey,
    AstExpression Value,
    AstraType Type,
    NodeId? SourceNodeId = null) : AstExpression(Type, SourceNodeId);

public sealed record AstListMakeExpression(
    AstraType Type,
    NodeId? SourceNodeId = null) : AstExpression(Type, SourceNodeId);

public sealed record AstCollectionAddExpression(
    AstExpression Collection,
    AstExpression Item,
    AstraType Type,
    NodeId? SourceNodeId = null) : AstExpression(Type, SourceNodeId);

public sealed record AstCollectionSetExpression(
    AstExpression Collection,
    AstExpression Index,
    AstExpression Item,
    AstraType Type,
    NodeId? SourceNodeId = null) : AstExpression(Type, SourceNodeId);

public sealed record AstCollectionRemoveExpression(
    AstExpression Collection,
    AstExpression Index,
    AstraType Type,
    NodeId? SourceNodeId = null) : AstExpression(Type, SourceNodeId);
