namespace AstraGraph.Core;

/// <summary>
/// Compiles an IrProgram with basic block CFG into linear BytecodeProgram for VM execution.
/// </summary>
public static class IrToBytecodeCompiler
{
    public static BytecodeProgram Compile(IrProgram irProgram, RevisionId? revision = null, string? semanticHash = null)
    {
        ArgumentNullException.ThrowIfNull(irProgram);

        var rev = revision ?? RevisionId.New();
        var hash = semanticHash ?? irProgram.Id.ToString();
        var pool = new ConstantPool();

        var bytecodeProg = new BytecodeProgram(irProgram.Id, rev, hash, pool);

        foreach (var entry in irProgram.EntryPoints)
        {
            var compiledFunc = CompileFunction(entry, pool);
            bytecodeProg.EntryPoints.Add(compiledFunc);
        }

        foreach (var func in irProgram.Functions)
        {
            var compiledFunc = CompileFunction(func, pool);
            bytecodeProg.Functions.Add(compiledFunc);
        }

        return bytecodeProg;
    }

    private static BytecodeFunction CompileFunction(IrFunction func, ConstantPool pool)
    {
        var nameIdx = pool.GetOrAddString(func.Name);
        var instructions = new List<BytecodeInstruction>();
        var blockStartOffsets = new Dictionary<int, int>();
        var pendingFixups = new List<(int InstructionIndex, IrBasicBlock Block, IrInstruction IrInstr)>();

        // 1. Emit instructions and record block start offsets
        foreach (var block in func.Blocks)
        {
            blockStartOffsets[block.Id] = instructions.Count;

            // Body instructions
            foreach (var instr in block.Instructions)
            {
                var bcInstr = EmitInstruction(instr, pool);
                instructions.Add(bcInstr);
            }

            // Terminator instruction (placeholder targets to be fixed up)
            if (block.Terminator != null)
            {
                var termIndex = instructions.Count;
                var termBc = EmitInstruction(block.Terminator, pool);
                instructions.Add(termBc);
                pendingFixups.Add((termIndex, block, block.Terminator));
            }
        }

        // 2. Fix up branch and jump targets
        foreach (var (index, block, irInstr) in pendingFixups)
        {
            var current = instructions[index];
            switch (irInstr.OpCode)
            {
                case IrOpCode.Jump:
                {
                    if (block.Successors.Count > 0)
                    {
                        var targetBlock = block.Successors[0];
                        var targetOffset = blockStartOffsets[targetBlock.Id];
                        instructions[index] = current with { Op1 = targetOffset };
                    }
                    break;
                }

                case IrOpCode.BranchIf:
                {
                    if (block.Successors.Count >= 2)
                    {
                        var thenOffset = blockStartOffsets[block.Successors[0].Id];
                        var elseOffset = blockStartOffsets[block.Successors[1].Id];
                        instructions[index] = current with { Op2 = thenOffset, Extra = elseOffset };
                    }
                    break;
                }

                case IrOpCode.YieldContinuation:
                {
                    if (block.Successors.Count > 0)
                    {
                        var resumeOffset = blockStartOffsets[block.Successors[0].Id];
                        instructions[index] = current with { Extra = resumeOffset };
                    }
                    break;
                }
            }
        }

        return new BytecodeFunction(nameIdx, func.RegisterCount, func.Parameters.Count, instructions);
    }

    private static BytecodeInstruction EmitInstruction(IrInstruction instr, ConstantPool pool)
    {
        var destReg = instr.Destination != null ? (ushort)instr.Destination.Index : BytecodeInstruction.NoRegister;
        var opCode = (byte)instr.OpCode;

        switch (instr.OpCode)
        {
            case IrOpCode.LoadConst:
            {
                var constOperand = (IrConstant)instr.Operands![0];
                var constIdx = AddConstantToPool(constOperand, pool);
                return new BytecodeInstruction(opCode, destReg, constIdx, 0, 0);
            }

            case IrOpCode.BranchIf:
            {
                var condReg = ((IrRegister)instr.Operands![0]).Index;
                return new BytecodeInstruction(opCode, BytecodeInstruction.NoRegister, condReg, 0, 0);
            }

            case IrOpCode.Jump:
            {
                return new BytecodeInstruction(opCode, BytecodeInstruction.NoRegister, 0, 0, 0);
            }

            case IrOpCode.Return:
            {
                var retReg = instr.Operands != null && instr.Operands.Count > 0 ? ((IrRegister)instr.Operands[0]).Index : -1;
                return new BytecodeInstruction(opCode, BytecodeInstruction.NoRegister, retReg, 0, 0);
            }

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
            {
                var r1 = ((IrRegister)instr.Operands![0]).Index;
                var r2 = ((IrRegister)instr.Operands[1]).Index;
                return new BytecodeInstruction(opCode, destReg, r1, r2, 0);
            }

            case IrOpCode.Neg:
            case IrOpCode.Not:
            {
                var r1 = ((IrRegister)instr.Operands![0]).Index;
                return new BytecodeInstruction(opCode, destReg, r1, 0, 0);
            }

            case IrOpCode.CallNative:
            {
                var methodIdx = pool.GetOrAddString(instr.StringPayload ?? string.Empty);
                var argCount = instr.Operands?.Count ?? 0;
                var firstArgReg = (instr.Operands != null && instr.Operands.Count > 0) ? ((IrRegister)instr.Operands[0]).Index : 0;
                return new BytecodeInstruction(opCode, destReg, methodIdx, argCount, firstArgReg);
            }

            case IrOpCode.YieldContinuation:
            {
                var kind = (int)(instr.Metadata is ContinuationKind k ? k : ContinuationKind.Delay);
                var resumeGuid = Guid.Parse(instr.StringPayload!);
                var guidIdx = pool.GetOrAddGuid(resumeGuid);
                return new BytecodeInstruction(opCode, BytecodeInstruction.NoRegister, kind, guidIdx, 0);
            }

            default:
                return new BytecodeInstruction(opCode, destReg, 0, 0, 0);
        }
    }

    private static int AddConstantToPool(IrConstant constant, ConstantPool pool)
    {
        if (constant.Value is null) return 0;

        return constant.Value switch
        {
            bool b => pool.GetOrAddInt64(b ? 1 : 0),
            byte u8 => pool.GetOrAddInt64(u8),
            sbyte i8 => pool.GetOrAddInt64(i8),
            short i16 => pool.GetOrAddInt64(i16),
            ushort u16 => pool.GetOrAddInt64(u16),
            int i32 => pool.GetOrAddInt64(i32),
            uint u32 => pool.GetOrAddInt64(u32),
            long i64 => pool.GetOrAddInt64(i64),
            float f32 => pool.GetOrAddDouble(f32),
            double f64 => pool.GetOrAddDouble(f64),
            string s => pool.GetOrAddString(s),
            Guid g => pool.GetOrAddGuid(g),
            _ => pool.GetOrAddString(constant.Value.ToString() ?? string.Empty)
        };
    }
}
