using System.Globalization;
using System.Text;

namespace AstraGraph.Core;

/// <summary>
/// Generates human-readable disassembly listings from Astra bytecode programs.
/// </summary>
public static class BytecodeDisassembler
{
    public static string Disassemble(BytecodeProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"; === Astra Bytecode: {program.Id} (Revision: {program.Revision}) ===");
        sb.AppendLine(CultureInfo.InvariantCulture, $"; SemanticHash: {program.SemanticHash}");
        sb.AppendLine();

        // Disassemble Constants
        sb.AppendLine(".constants");
        for (var i = 0; i < program.Constants.Count; i++)
        {
            var c = program.Constants[i];
            sb.AppendLine(CultureInfo.InvariantCulture, $"  #{i:D3}: {c.Kind,-7} = {FormatConstant(c)}");
        }
        sb.AppendLine();

        // Disassemble EntryPoints
        foreach (var ep in program.EntryPoints)
        {
            DisassembleFunction(sb, program, ep, isEntryPoint: true);
        }

        // Disassemble Functions
        foreach (var fn in program.Functions)
        {
            DisassembleFunction(sb, program, fn, isEntryPoint: false);
        }

        return sb.ToString();
    }

    private static void DisassembleFunction(StringBuilder sb, BytecodeProgram program, BytecodeFunction func, bool isEntryPoint)
    {
        var funcName = program.Constants[func.NameConstantIndex].Value?.ToString() ?? "unnamed";
        var kind = isEntryPoint ? ".entrypoint" : ".function";

        sb.AppendLine(CultureInfo.InvariantCulture, $"{kind} {funcName} (registers: {func.RegisterCount}, params: {func.ParameterCount})");

        for (var i = 0; i < func.Instructions.Count; i++)
        {
            var instr = func.Instructions[i];
            var dest = instr.HasDestination ? $"%r{instr.DestRegister} = " : "        ";
            var opName = instr.AsOpCode.ToString();

            sb.AppendLine(CultureInfo.InvariantCulture, $"  L{i:D4}: {dest}{opName,-18} {FormatOperands(program, instr)}");
        }
        sb.AppendLine();
    }

    private static string FormatConstant(ConstantEntry entry) =>
        entry.Kind == ConstantKind.String ? $"\"{entry.Value}\"" : entry.Value?.ToString() ?? "null";

    private static string FormatOperands(BytecodeProgram program, BytecodeInstruction instr)
    {
        switch (instr.AsOpCode)
        {
            case IrOpCode.LoadConst:
                var c = program.Constants[instr.Op1];
                return $"#{instr.Op1} ({FormatConstant(c)})";

            case IrOpCode.BranchIf:
                return $"%r{instr.Op1} -> L{instr.Op2:D4}, else L{instr.Extra:D4}";

            case IrOpCode.Jump:
                return $"-> L{instr.Op1:D4}";

            case IrOpCode.Add:
            case IrOpCode.Sub:
            case IrOpCode.Mul:
            case IrOpCode.Div:
            case IrOpCode.Mod:
            case IrOpCode.CmpEq:
            case IrOpCode.CmpNe:
            case IrOpCode.CmpLt:
            case IrOpCode.CmpLe:
            case IrOpCode.CmpGt:
            case IrOpCode.CmpGe:
            case IrOpCode.And:
            case IrOpCode.Or:
                return $"%r{instr.Op1}, %r{instr.Op2}";

            case IrOpCode.Neg:
            case IrOpCode.Not:
                return $"%r{instr.Op1}";

            case IrOpCode.CallNative:
                var method = program.Constants[instr.Op1].Value?.ToString();
                return $"\"{method}\" (args: {instr.Op2})";

            case IrOpCode.YieldContinuation:
                return $"kind:{instr.Op1}, resumePoint: #{instr.Op2} -> L{instr.Extra:D4}";

            default:
                return $"op1:{instr.Op1}, op2:{instr.Op2}, extra:{instr.Extra}";
        }
    }
}
