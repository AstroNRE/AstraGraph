namespace AstraGraph.Core;

/// <summary>
/// Static verifier for Astra IR structural and type integrity.
/// </summary>
public static class IrVerifier
{
    public static DiagnosticBag Verify(IrProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);

        var diagnostics = new DiagnosticBag();

        foreach (var func in program.Functions.Concat(program.EntryPoints))
        {
            VerifyFunction(func, diagnostics);
        }

        return diagnostics;
    }

    private static void VerifyFunction(IrFunction function, DiagnosticBag diagnostics)
    {
        if (function.Blocks.Count == 0)
        {
            diagnostics.ReportError("IR001", $"Function '{function.Name}' contains no basic blocks.");
            return;
        }

        if (function.EntryBlock != function.Blocks[0])
        {
            diagnostics.ReportError("IR002", $"Function '{function.Name}' first block must be its entry block.");
        }

        var blockIds = new HashSet<int>();
        var blockNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var block in function.Blocks)
        {
            if (!blockIds.Add(block.Id))
            {
                diagnostics.ReportError("IR003", $"Duplicate block ID {block.Id} in function '{function.Name}'.");
            }

            if (!blockNames.Add(block.Name))
            {
                diagnostics.ReportError("IR004", $"Duplicate block name '{block.Name}' in function '{function.Name}'.");
            }

            // Verify terminator
            if (block.Terminator is null)
            {
                diagnostics.ReportError("IR005", $"Block '{block.Name}' in function '{function.Name}' is missing a terminator.");
            }
            else if (!IsTerminator(block.Terminator.OpCode))
            {
                diagnostics.ReportError("IR006", $"Block '{block.Name}' terminator is not a valid control flow terminator: {block.Terminator.OpCode}.");
            }

            // Verify body instructions do not contain terminators
            for (var i = 0; i < block.Instructions.Count; i++)
            {
                var instr = block.Instructions[i];
                if (IsTerminator(instr.OpCode))
                {
                    diagnostics.ReportError("IR007", $"Instruction {i} in block '{block.Name}' is a terminator '{instr.OpCode}', but terminators must only be in Terminator property.");
                }
            }

            // Verify successor consistency
            foreach (var succ in block.Successors)
            {
                if (!function.Blocks.Contains(succ))
                {
                    diagnostics.ReportError("IR008", $"Block '{block.Name}' references external successor block '{succ.Name}'.");
                }

                if (!succ.Predecessors.Contains(block))
                {
                    diagnostics.ReportError("IR009", $"Block '{block.Name}' is not registered in predecessors of successor '{succ.Name}'.");
                }
            }
        }
    }

    private static bool IsTerminator(IrOpCode op) =>
        op is IrOpCode.Jump or IrOpCode.BranchIf or IrOpCode.Return or IrOpCode.YieldContinuation;
}
