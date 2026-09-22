using System.Runtime.InteropServices;

namespace AstraGraph.Core;

/// <summary>
/// Packed 16-byte bytecode instruction executed by Astra VM.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct BytecodeInstruction(
    byte OpCode,
    ushort DestRegister,
    int Op1,
    int Op2,
    int Extra)
{
    public const ushort NoRegister = 0xFFFF;

    public bool HasDestination => DestRegister != NoRegister;

    public IrOpCode AsOpCode => (IrOpCode)OpCode;

    public override string ToString() =>
        $"{(HasDestination ? $"%r{DestRegister} = " : "")}{AsOpCode} (op1:{Op1}, op2:{Op2}, extra:{Extra})";
}

/// <summary>
/// A compiled executable function in bytecode form.
/// </summary>
public sealed class BytecodeFunction
{
    public int NameConstantIndex { get; }
    public int RegisterCount { get; }
    public int ParameterCount { get; }
    public IReadOnlyList<BytecodeInstruction> Instructions { get; }
    public IReadOnlyList<NodeId?>? SourceMap { get; }

    public BytecodeFunction(
        int nameConstantIndex,
        int registerCount,
        int parameterCount,
        IReadOnlyList<BytecodeInstruction> instructions,
        IReadOnlyList<NodeId?>? sourceMap = null)
    {
        NameConstantIndex = nameConstantIndex;
        RegisterCount = registerCount;
        ParameterCount = parameterCount;
        Instructions = instructions;
        SourceMap = sourceMap;
    }

    public NodeId? GetSourceNodeId(int ip) =>
        SourceMap != null && ip >= 0 && ip < SourceMap.Count ? SourceMap[ip] : null;
}

/// <summary>
/// A complete compiled program in portable binary bytecode form.
/// </summary>
public sealed class BytecodeProgram
{
    public const uint MagicHeader = 0x43424741; // "AGBC" in little endian
    public const byte FormatVersion = 1;

    public GraphId Id { get; }
    public RevisionId Revision { get; }
    public string SemanticHash { get; }
    public ConstantPool Constants { get; }
    public List<BytecodeFunction> Functions { get; } = [];
    public List<BytecodeFunction> EntryPoints { get; } = [];

    public BytecodeProgram(GraphId id, RevisionId revision, string semanticHash, ConstantPool constants)
    {
        Id = id;
        Revision = revision;
        SemanticHash = semanticHash;
        Constants = constants;
    }

    public BytecodeFunction? FindEntryPoint(string name)
    {
        foreach (var ep in EntryPoints)
        {
            if (Constants[ep.NameConstantIndex].Value is string str && string.Equals(str, name, StringComparison.Ordinal))
            {
                return ep;
            }
        }
        return null;
    }

    public BytecodeFunction? FindFunction(string name)
    {
        foreach (var fn in Functions)
        {
            if (Constants[fn.NameConstantIndex].Value is string str && string.Equals(str, name, StringComparison.Ordinal))
            {
                return fn;
            }
        }
        return null;
    }
}
