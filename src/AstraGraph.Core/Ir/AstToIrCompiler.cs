namespace AstraGraph.Core;

/// <summary>
/// Compiles a typed AstProgram into flat, basic-block-based Astra IR.
/// </summary>
public static class AstToIrCompiler
{
    public static IrProgram Compile(AstProgram astProgram)
    {
        ArgumentNullException.ThrowIfNull(astProgram);

        var irProgram = new IrProgram(astProgram.Id, astProgram.Name, astProgram.Kind, astProgram.Side);

        foreach (var v in astProgram.Variables)
        {
            irProgram.Variables.Add(new IrVariable(v.Id, v.Type, v.Name));
        }

        foreach (var entry in astProgram.EntryPoints)
        {
            var irFunc = CompileEntryPoint(entry);
            irProgram.EntryPoints.Add(irFunc);
        }

        foreach (var func in astProgram.Functions)
        {
            var irFunc = CompileFunction(func);
            irProgram.Functions.Add(irFunc);
        }

        // Verify generated IR
        var diags = IrVerifier.Verify(irProgram);
        if (diags.HasErrors)
        {
            throw new InvalidOperationException($"IR generation failed verification:{Environment.NewLine}{diags}");
        }

        return irProgram;
    }

    private static IrFunction CompileEntryPoint(AstEntryPointStatement entryPoint)
    {
        var entryBlock = new IrBasicBlock(0, "entry");
        var irFunc = new IrFunction(entryPoint.Name, [], PrimitiveType.Void, entryBlock);
        var emitter = new FunctionEmitter(irFunc);

        emitter.EmitBlock(entryPoint.Body, entryBlock);
        return irFunc;
    }

    private static IrFunction CompileFunction(AstFunction function)
    {
        var entryBlock = new IrBasicBlock(0, "entry");
        var paramVars = function.Parameters.Select(p => new IrVariable(p.Id, p.Type, p.Name)).ToList();
        var irFunc = new IrFunction(function.Name, paramVars, function.ReturnType, entryBlock);
        var emitter = new FunctionEmitter(irFunc);

        emitter.EmitBlock(function.Body, entryBlock);
        return irFunc;
    }

    private sealed class FunctionEmitter
    {
        private readonly IrFunction _function;
        private int _registerCounter;
        private int _blockCounter;

        public FunctionEmitter(IrFunction function)
        {
            _function = function;
        }

        private IrRegister AllocateRegister(AstraType type)
        {
            var reg = new IrRegister(_registerCounter++, type);
            _function.RegisterCount = Math.Max(_function.RegisterCount, _registerCounter);
            return reg;
        }

        private IrBasicBlock CreateBlock(string prefix)
        {
            return _function.CreateBlock($"{prefix}_{++_blockCounter}");
        }

        public void EmitBlock(AstBlock block, IrBasicBlock currentBlock)
        {
            var activeBlock = currentBlock;

            foreach (var stmt in block.Statements)
            {
                if (activeBlock.Terminator != null)
                {
                    // Block was terminated (e.g. by a return or jump), create an unreachable block for remaining
                    activeBlock = CreateBlock("unreachable");
                }

                activeBlock = EmitStatement(stmt, activeBlock);
            }

            // Ensure last block has a terminator
            if (activeBlock.Terminator is null)
            {
                var ret = new IrInstruction(IrOpCode.Return);
                activeBlock.SetTerminator(ret);
            }
        }

        private IrBasicBlock EmitStatement(AstStatement stmt, IrBasicBlock currentBlock)
        {
            switch (stmt)
            {
                case AstVariableAssignStatement assign:
                {
                    var valReg = EmitExpression(assign.Value, currentBlock);
                    var varOperand = new IrVariable(assign.VariableId, assign.Value.Type, assign.Name);
                    var instr = new IrInstruction(IrOpCode.StoreVariable, null, [varOperand, valReg], SourceNodeId: assign.SourceNodeId);
                    currentBlock.AddInstruction(instr);
                    return currentBlock;
                }

                case AstBranchStatement branch:
                {
                    var condReg = EmitExpression(branch.Condition, currentBlock);

                    var thenBlock = CreateBlock("then");
                    var elseBlock = CreateBlock("else");
                    var mergeBlock = CreateBlock("merge");

                    var branchTerminator = new IrInstruction(IrOpCode.BranchIf, null, [condReg], SourceNodeId: branch.SourceNodeId);
                    currentBlock.SetTerminator(branchTerminator, thenBlock, elseBlock);

                    // Emit Then block
                    EmitBlock(branch.TrueBlock, thenBlock);
                    if (thenBlock.Terminator is null)
                    {
                        thenBlock.SetTerminator(new IrInstruction(IrOpCode.Jump), mergeBlock);
                    }

                    // Emit Else block
                    if (branch.FalseBlock != null)
                    {
                        EmitBlock(branch.FalseBlock, elseBlock);
                    }
                    if (elseBlock.Terminator is null)
                    {
                        elseBlock.SetTerminator(new IrInstruction(IrOpCode.Jump), mergeBlock);
                    }

                    return mergeBlock;
                }

                case AstYieldContinuationStatement yieldStmt:
                {
                    var argRegs = new List<IrOperand>();
                    foreach (var arg in yieldStmt.Arguments)
                    {
                        argRegs.Add(EmitExpression(arg, currentBlock));
                    }

                    var resumeBlock = CreateBlock("resume");
                    var yieldTerminator = new IrInstruction(
                        IrOpCode.YieldContinuation,
                        null,
                        argRegs,
                        StringPayload: yieldStmt.ResumePointId.ToString("D"),
                        Metadata: yieldStmt.Kind,
                        SourceNodeId: yieldStmt.SourceNodeId);

                    currentBlock.SetTerminator(yieldTerminator, resumeBlock);
                    return resumeBlock;
                }

                case AstSetComponentFieldStatement setField:
                {
                    var entityReg = EmitExpression(setField.Entity, currentBlock);
                    var valReg = EmitExpression(setField.Value, currentBlock);
                    var fieldMeta = $"{setField.Schema.Id}:{setField.FieldId}";
                    var instr = new IrInstruction(
                        IrOpCode.SetComponentField,
                        null,
                        [entityReg, valReg],
                        StringPayload: fieldMeta,
                        Metadata: setField.Schema,
                        SourceNodeId: setField.SourceNodeId);
                    currentBlock.AddInstruction(instr);
                    return currentBlock;
                }

                case AstReturnStatement ret:
                {
                    IrOperand? retOperand = null;
                    if (ret.Value != null)
                    {
                        retOperand = EmitExpression(ret.Value, currentBlock);
                    }

                    var retInstr = new IrInstruction(
                        IrOpCode.Return,
                        null,
                        retOperand != null ? [retOperand] : null,
                        SourceNodeId: ret.SourceNodeId);
                    currentBlock.SetTerminator(retInstr);
                    return currentBlock;
                }

                case AstExpressionStatement exprStmt:
                {
                    EmitExpression(exprStmt.Expression, currentBlock);
                    return currentBlock;
                }

                case AstBlock nestedBlock:
                {
                    EmitBlock(nestedBlock, currentBlock);
                    return currentBlock;
                }

                default:
                    return currentBlock;
            }
        }

        private IrRegister EmitExpression(AstExpression expr, IrBasicBlock currentBlock)
        {
            switch (expr)
            {
                case AstLiteralExpression literal:
                {
                    var reg = AllocateRegister(literal.Type);
                    var constOperand = new IrConstant(literal.Value, literal.Type);
                    currentBlock.AddInstruction(new IrInstruction(IrOpCode.LoadConst, reg, [constOperand], SourceNodeId: literal.SourceNodeId));
                    return reg;
                }

                case AstVariableReadExpression varRead:
                {
                    var reg = AllocateRegister(varRead.Type);
                    var varOperand = new IrVariable(varRead.VariableId, varRead.Type, varRead.Name);
                    currentBlock.AddInstruction(new IrInstruction(IrOpCode.LoadVariable, reg, [varOperand], SourceNodeId: varRead.SourceNodeId));
                    return reg;
                }

                case AstBinaryExpression bin:
                {
                    var leftReg = EmitExpression(bin.Left, currentBlock);
                    var rightReg = EmitExpression(bin.Right, currentBlock);
                    var reg = AllocateRegister(bin.Type);

                    var opcode = bin.Operator switch
                    {
                        AstBinaryOperator.Add => IrOpCode.Add,
                        AstBinaryOperator.Subtract => IrOpCode.Sub,
                        AstBinaryOperator.Multiply => IrOpCode.Mul,
                        AstBinaryOperator.Divide => IrOpCode.Div,
                        AstBinaryOperator.Modulo => IrOpCode.Mod,
                        AstBinaryOperator.Equal => IrOpCode.CmpEq,
                        AstBinaryOperator.NotEqual => IrOpCode.CmpNe,
                        AstBinaryOperator.LessThan => IrOpCode.CmpLt,
                        AstBinaryOperator.LessThanOrEqual => IrOpCode.CmpLe,
                        AstBinaryOperator.GreaterThan => IrOpCode.CmpGt,
                        AstBinaryOperator.GreaterThanOrEqual => IrOpCode.CmpGe,
                        AstBinaryOperator.And => IrOpCode.And,
                        AstBinaryOperator.Or => IrOpCode.Or,
                        _ => throw new InvalidOperationException($"Unsupported binary operator {bin.Operator}")
                    };

                    currentBlock.AddInstruction(new IrInstruction(opcode, reg, [leftReg, rightReg], SourceNodeId: bin.SourceNodeId));
                    return reg;
                }

                case AstUnaryExpression unary:
                {
                    var operandReg = EmitExpression(unary.Operand, currentBlock);
                    var reg = AllocateRegister(unary.Type);

                    var opcode = unary.Operator switch
                    {
                        AstUnaryOperator.Negate => IrOpCode.Neg,
                        AstUnaryOperator.Not => IrOpCode.Not,
                        _ => throw new InvalidOperationException($"Unsupported unary operator {unary.Operator}")
                    };

                    currentBlock.AddInstruction(new IrInstruction(opcode, reg, [operandReg], SourceNodeId: unary.SourceNodeId));
                    return reg;
                }

                case AstNativeCallExpression nativeCall:
                {
                    var argRegs = new List<IrOperand>();
                    foreach (var arg in nativeCall.Arguments)
                    {
                        argRegs.Add(EmitExpression(arg, currentBlock));
                    }

                    var reg = AllocateRegister(nativeCall.ReturnType);
                    currentBlock.AddInstruction(new IrInstruction(
                        IrOpCode.CallNative,
                        reg,
                        argRegs,
                        StringPayload: nativeCall.MethodDescriptor,
                        SourceNodeId: nativeCall.SourceNodeId));
                    return reg;
                }

                case AstGetComponentExpression getComp:
                {
                    var entityReg = EmitExpression(getComp.Entity, currentBlock);
                    var reg = AllocateRegister(getComp.ComponentType);
                    currentBlock.AddInstruction(new IrInstruction(
                        IrOpCode.GetComponent,
                        reg,
                        [entityReg],
                        StringPayload: getComp.ComponentType.TypeName,
                        SourceNodeId: getComp.SourceNodeId));
                    return reg;
                }

                case AstHasComponentExpression hasComp:
                {
                    var entityReg = EmitExpression(hasComp.Entity, currentBlock);
                    var reg = AllocateRegister(PrimitiveType.Bool);
                    currentBlock.AddInstruction(new IrInstruction(
                        IrOpCode.HasComponent,
                        reg,
                        [entityReg],
                        StringPayload: hasComp.ComponentType.TypeName,
                        SourceNodeId: hasComp.SourceNodeId));
                    return reg;
                }

                default:
                    return AllocateRegister(expr.Type);
            }
        }
    }
}
