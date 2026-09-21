using AstraGraph.Core;

namespace AstraGraph.VM;

/// <summary>
/// Fast portable stackless virtual machine executing Astra bytecode.
/// </summary>
public sealed class AstraVm
{
    private readonly IVmHostServices _defaultServices = new DefaultVmHostServices();

    public VmExecutionResult Execute(
        BytecodeProgram program,
        BytecodeFunction function,
        AstraValue[]? initialRegisters = null,
        int startIp = 0,
        IVmHostServices? hostServices = null,
        ExecutionBudget? budget = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(function);

        var services = hostServices ?? _defaultServices;
        var b = budget ?? new ExecutionBudget();

        var registers = new AstraValue[Math.Max(function.RegisterCount, 1)];
        if (initialRegisters != null)
        {
            Array.Copy(initialRegisters, registers, Math.Min(initialRegisters.Length, registers.Length));
        }

        var ip = startIp;
        var instructions = function.Instructions;

        try
        {
            while (ip < instructions.Count)
            {
                b.Tick();

                var instr = instructions[ip++];
                var dest = instr.DestRegister;

                switch (instr.AsOpCode)
                {
                    case IrOpCode.Nop:
                        break;

                    case IrOpCode.LoadConst:
                    {
                        var constant = program.Constants[instr.Op1];
                        registers[dest] = ConvertConstantToAstraValue(constant);
                        break;
                    }

                    case IrOpCode.Add:
                    {
                        var left = registers[instr.Op1];
                        var right = registers[instr.Op2];

                        if (left.Type == AstraValueType.Double || right.Type == AstraValueType.Double)
                        {
                            registers[dest] = AstraValue.FromDouble(left.AsDouble() + right.AsDouble());
                        }
                        else if (left.Type == AstraValueType.Object || right.Type == AstraValueType.Object)
                        {
                            registers[dest] = AstraValue.FromString((left.AsString() ?? string.Empty) + (right.AsString() ?? string.Empty));
                        }
                        else
                        {
                            registers[dest] = AstraValue.FromInt64(left.AsInt64() + right.AsInt64());
                        }
                        break;
                    }

                    case IrOpCode.Sub:
                    {
                        var left = registers[instr.Op1];
                        var right = registers[instr.Op2];

                        if (left.Type == AstraValueType.Double || right.Type == AstraValueType.Double)
                        {
                            registers[dest] = AstraValue.FromDouble(left.AsDouble() - right.AsDouble());
                        }
                        else
                        {
                            registers[dest] = AstraValue.FromInt64(left.AsInt64() - right.AsInt64());
                        }
                        break;
                    }

                    case IrOpCode.Mul:
                    {
                        var left = registers[instr.Op1];
                        var right = registers[instr.Op2];

                        if (left.Type == AstraValueType.Double || right.Type == AstraValueType.Double)
                        {
                            registers[dest] = AstraValue.FromDouble(left.AsDouble() * right.AsDouble());
                        }
                        else
                        {
                            registers[dest] = AstraValue.FromInt64(left.AsInt64() * right.AsInt64());
                        }
                        break;
                    }

                    case IrOpCode.Div:
                    {
                        var left = registers[instr.Op1];
                        var right = registers[instr.Op2];

                        if (left.Type == AstraValueType.Double || right.Type == AstraValueType.Double)
                        {
                            registers[dest] = AstraValue.FromDouble(left.AsDouble() / right.AsDouble());
                        }
                        else
                        {
                            registers[dest] = AstraValue.FromInt64(left.AsInt64() / right.AsInt64());
                        }
                        break;
                    }

                    case IrOpCode.Mod:
                    {
                        registers[dest] = AstraValue.FromInt64(registers[instr.Op1].AsInt64() % registers[instr.Op2].AsInt64());
                        break;
                    }

                    case IrOpCode.Neg:
                    {
                        var val = registers[instr.Op1];
                        registers[dest] = val.Type == AstraValueType.Double
                            ? AstraValue.FromDouble(-val.AsDouble())
                            : AstraValue.FromInt64(-val.AsInt64());
                        break;
                    }

                    case IrOpCode.CmpEq:
                        registers[dest] = AstraValue.FromBool(registers[instr.Op1].Equals(registers[instr.Op2]));
                        break;

                    case IrOpCode.CmpNe:
                        registers[dest] = AstraValue.FromBool(!registers[instr.Op1].Equals(registers[instr.Op2]));
                        break;

                    case IrOpCode.CmpLt:
                    {
                        var left = registers[instr.Op1];
                        var right = registers[instr.Op2];
                        registers[dest] = AstraValue.FromBool(left.Type == AstraValueType.Double || right.Type == AstraValueType.Double
                            ? left.AsDouble() < right.AsDouble()
                            : left.AsInt64() < right.AsInt64());
                        break;
                    }

                    case IrOpCode.CmpLe:
                    {
                        var left = registers[instr.Op1];
                        var right = registers[instr.Op2];
                        registers[dest] = AstraValue.FromBool(left.Type == AstraValueType.Double || right.Type == AstraValueType.Double
                            ? left.AsDouble() <= right.AsDouble()
                            : left.AsInt64() <= right.AsInt64());
                        break;
                    }

                    case IrOpCode.CmpGt:
                    {
                        var left = registers[instr.Op1];
                        var right = registers[instr.Op2];
                        registers[dest] = AstraValue.FromBool(left.Type == AstraValueType.Double || right.Type == AstraValueType.Double
                            ? left.AsDouble() > right.AsDouble()
                            : left.AsInt64() > right.AsInt64());
                        break;
                    }

                    case IrOpCode.CmpGe:
                    {
                        var left = registers[instr.Op1];
                        var right = registers[instr.Op2];
                        registers[dest] = AstraValue.FromBool(left.Type == AstraValueType.Double || right.Type == AstraValueType.Double
                            ? left.AsDouble() >= right.AsDouble()
                            : left.AsInt64() >= right.AsInt64());
                        break;
                    }

                    case IrOpCode.And:
                        registers[dest] = AstraValue.FromBool(registers[instr.Op1].AsBool() && registers[instr.Op2].AsBool());
                        break;

                    case IrOpCode.Or:
                        registers[dest] = AstraValue.FromBool(registers[instr.Op1].AsBool() || registers[instr.Op2].AsBool());
                        break;

                    case IrOpCode.Not:
                        registers[dest] = AstraValue.FromBool(!registers[instr.Op1].AsBool());
                        break;

                    case IrOpCode.BranchIf:
                    {
                        var cond = registers[instr.Op1].AsBool();
                        ip = cond ? instr.Op2 : instr.Extra;
                        break;
                    }

                    case IrOpCode.Jump:
                    {
                        ip = instr.Op1;
                        break;
                    }

                    case IrOpCode.Return:
                    {
                        var retVal = instr.Op1 >= 0 ? registers[instr.Op1] : AstraValue.Null;
                        return new VmExecutionResult(VmExecutionStatus.Completed, retVal, InstructionsExecuted: b.InstructionsExecuted);
                    }

                    case IrOpCode.YieldContinuation:
                    {
                        var kind = (ContinuationKind)instr.Op1;
                        var resumeGuid = (Guid)program.Constants[instr.Op2].Value!;
                        var nextIp = instr.Extra;
                        var yieldState = new ContinuationState(kind, resumeGuid, nextIp, registers);

                        return new VmExecutionResult(
                            VmExecutionStatus.Yielded,
                            AstraValue.Null,
                            yieldState,
                            InstructionsExecuted: b.InstructionsExecuted);
                    }

                    case IrOpCode.StoreVariable:
                    {
                        var varName = program.Constants[instr.Op1].Value?.ToString() ?? string.Empty;
                        var val = registers[instr.Op2];
                        services.SetVariable(SymbolId.Empty, varName, val);
                        break;
                    }

                    case IrOpCode.LoadVariable:
                    {
                        var varName = program.Constants[instr.Op1].Value?.ToString() ?? string.Empty;
                        registers[dest] = services.GetVariable(SymbolId.Empty, varName);
                        break;
                    }

                    case IrOpCode.CallNative:
                    {
                        var method = program.Constants[instr.Op1].Value?.ToString() ?? string.Empty;
                        var argCount = instr.Op2;
                        var firstArgReg = instr.Extra;
                        var args = new AstraValue[argCount];
                        for (var i = 0; i < argCount; i++)
                        {
                            var regIdx = firstArgReg + i;
                            if (regIdx < registers.Length)
                            {
                                args[i] = registers[regIdx];
                            }
                        }
                        var result = services.CallNative(method, args);
                        if (dest != BytecodeInstruction.NoRegister)
                        {
                            registers[dest] = result;
                        }
                        break;
                    }

                    case IrOpCode.GetComponent:
                    {
                        var entityId = registers[instr.Op1].AsEntityUid();
                        var compName = program.Constants[instr.Op2].Value?.ToString() ?? string.Empty;
                        registers[dest] = services.GetComponent(entityId, compName);
                        break;
                    }

                    case IrOpCode.HasComponent:
                    {
                        var entityId = registers[instr.Op1].AsEntityUid();
                        var compName = program.Constants[instr.Op2].Value?.ToString() ?? string.Empty;
                        registers[dest] = AstraValue.FromBool(services.HasComponent(entityId, compName));
                        break;
                    }
                }
            }

            return new VmExecutionResult(VmExecutionStatus.Completed, AstraValue.Null, InstructionsExecuted: b.InstructionsExecuted);
        }
        catch (ExecutionBudgetExceededException ex)
        {
            return new VmExecutionResult(VmExecutionStatus.ExceededBudget, AstraValue.Null, Exception: ex, InstructionsExecuted: b.InstructionsExecuted);
        }
        catch (Exception ex)
        {
            return new VmExecutionResult(VmExecutionStatus.Faulted, AstraValue.Null, Exception: ex, InstructionsExecuted: b.InstructionsExecuted);
        }
    }

    private static AstraValue ConvertConstantToAstraValue(ConstantEntry entry) => entry.Kind switch
    {
        ConstantKind.Null => AstraValue.Null,
        ConstantKind.Bool => AstraValue.FromBool((bool)entry.Value!),
        ConstantKind.Int64 => AstraValue.FromInt64((long)entry.Value!),
        ConstantKind.Double => AstraValue.FromDouble((double)entry.Value!),
        ConstantKind.String => AstraValue.FromString((string)entry.Value!),
        ConstantKind.Guid => AstraValue.FromObject(entry.Value),
        _ => AstraValue.Null
    };
}
