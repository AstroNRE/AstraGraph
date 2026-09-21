using System.Text.Json.Serialization;

namespace AstraGraph.Core;

[JsonConverter(typeof(JsonStringEnumConverter<IrOpCode>))]
public enum IrOpCode
{
    Nop,
    LoadConst,
    LoadLocal,
    StoreLocal,
    LoadVariable,
    StoreVariable,

    // Arithmetic
    Add,
    Sub,
    Mul,
    Div,
    Mod,
    Neg,

    // Comparisons
    CmpEq,
    CmpNe,
    CmpLt,
    CmpLe,
    CmpGt,
    CmpGe,

    // Logic
    And,
    Or,
    Not,

    // Calls
    CallLocal,
    CallNative,

    // ECS Operations
    GetComponent,
    HasComponent,
    SetComponentField,

    // Control Flow Terminators
    Jump,
    BranchIf,
    Return,
    YieldContinuation
}

/// <summary>
/// Abstract operand in Astra IR.
/// </summary>
public abstract record IrOperand(AstraType Type);

public sealed record IrConstant(object? Value, AstraType Type) : IrOperand(Type)
{
    public override string ToString() => Value?.ToString() ?? "null";
}

public sealed record IrRegister(int Index, AstraType Type) : IrOperand(Type)
{
    public override string ToString() => $"%r{Index}:{Type.TypeName}";
}

public sealed record IrLocal(int Slot, AstraType Type, string Name) : IrOperand(Type)
{
    public override string ToString() => $"local({Name}@{Slot}):{Type.TypeName}";
}

public sealed record IrVariable(SymbolId Id, AstraType Type, string Name) : IrOperand(Type)
{
    public override string ToString() => $"var({Name}):{Type.TypeName}";
}
