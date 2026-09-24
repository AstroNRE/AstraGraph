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
        var irFunc = new IrFunction(entryPoint.Name, [], PrimitiveType.Void, entryBlock)
        {
            Trigger = entryPoint.Trigger
        };
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

        public IrBasicBlock EmitBlock(AstBlock block, IrBasicBlock currentBlock, bool implicitReturn = true)
        {
            var activeBlock = currentBlock;

            foreach (var stmt in block.Statements)
            {
                if (activeBlock.Terminator != null)
                {
                    activeBlock = CreateBlock("unreachable");
                }

                activeBlock = EmitStatement(stmt, activeBlock);
            }

            if (implicitReturn && activeBlock.Terminator is null)
            {
                activeBlock.SetTerminator(new IrInstruction(IrOpCode.Return));
            }

            return activeBlock;
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

                    var thenEnd = EmitBlock(branch.TrueBlock, thenBlock, implicitReturn: false);
                    if (thenEnd.Terminator is null)
                    {
                        thenEnd.SetTerminator(new IrInstruction(IrOpCode.Jump), mergeBlock);
                    }

                    var elseEnd = branch.FalseBlock != null
                        ? EmitBlock(branch.FalseBlock, elseBlock, implicitReturn: false)
                        : elseBlock;
                    if (elseEnd.Terminator is null)
                    {
                        elseEnd.SetTerminator(new IrInstruction(IrOpCode.Jump), mergeBlock);
                    }

                    return mergeBlock;
                }

                case AstForStatement loop:
                    return EmitCountedLoop(loop.IndexName, loop.Start, loop.End, loop.Body, currentBlock, loop.SourceNodeId);

                case AstForEachStatement each:
                    return EmitForEach(each, currentBlock);

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
                    return EmitBlock(nestedBlock, currentBlock, implicitReturn: false);

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
                    var sources = new List<IrRegister>();
                    foreach (var arg in nativeCall.Arguments)
                    {
                        sources.Add(EmitExpression(arg, currentBlock));
                    }

                    var packed = new List<IrOperand>(sources.Count);
                    foreach (var source in sources)
                    {
                        var slot = AllocateRegister(source.Type);
                        currentBlock.AddInstruction(new IrInstruction(IrOpCode.Move, slot, [source], SourceNodeId: nativeCall.SourceNodeId));
                        packed.Add(slot);
                    }

                    var reg = AllocateRegister(nativeCall.ReturnType);
                    currentBlock.AddInstruction(new IrInstruction(
                        IrOpCode.CallNative,
                        reg,
                        packed,
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

                case AstEventContextExpression context:
                {
                    var reg = AllocateRegister(context.Type);
                    currentBlock.AddInstruction(new IrInstruction(
                        IrOpCode.LoadEvent,
                        reg,
                        Metadata: context.Slot,
                        SourceNodeId: context.SourceNodeId));
                    return reg;
                }

                case AstMemberReadExpression member:
                {
                    var target = EmitExpression(member.Target, currentBlock);
                    var reg = AllocateRegister(member.Type);
                    currentBlock.AddInstruction(new IrInstruction(
                        IrOpCode.GetMember,
                        reg,
                        [target],
                        StringPayload: member.MemberName,
                        SourceNodeId: member.SourceNodeId));
                    return reg;
                }

                case AstFieldReadExpression field:
                {
                    var component = EmitExpression(field.Component, currentBlock);
                    var reg = AllocateRegister(field.Type);
                    currentBlock.AddInstruction(new IrInstruction(
                        IrOpCode.GetField,
                        reg,
                        [component],
                        StringPayload: field.FieldName,
                        SourceNodeId: field.SourceNodeId));
                    return reg;
                }

                case AstCollectionLengthExpression length:
                {
                    var collection = EmitExpression(length.Collection, currentBlock);
                    var reg = AllocateRegister(PrimitiveType.Int32);
                    currentBlock.AddInstruction(new IrInstruction(
                        IrOpCode.CollectionLength,
                        reg,
                        [collection],
                        SourceNodeId: length.SourceNodeId));
                    return reg;
                }

                case AstCollectionGetExpression element:
                {
                    var collection = EmitExpression(element.Collection, currentBlock);
                    var index = EmitExpression(element.Index, currentBlock);
                    var reg = AllocateRegister(element.Type);
                    currentBlock.AddInstruction(new IrInstruction(
                        IrOpCode.CollectionGet,
                        reg,
                        [collection, index],
                        SourceNodeId: element.SourceNodeId));
                    return reg;
                }

                case AstHasValueExpression hasValue:
                {
                    var operand = EmitExpression(hasValue.Value, currentBlock);
                    var reg = AllocateRegister(PrimitiveType.Bool);
                    currentBlock.AddInstruction(new IrInstruction(
                        IrOpCode.HasValue,
                        reg,
                        [operand],
                        SourceNodeId: hasValue.SourceNodeId));
                    return reg;
                }

                case AstStructMakeExpression make:
                {
                    var reg = AllocateRegister(make.Type);
                    currentBlock.AddInstruction(new IrInstruction(
                        IrOpCode.StructMake,
                        reg,
                        StringPayload: make.SchemaKey,
                        SourceNodeId: make.SourceNodeId));
                    return reg;
                }

                case AstStructCopyExpression copy:
                {
                    var source = EmitExpression(copy.Source, currentBlock);
                    var reg = AllocateRegister(copy.Type);
                    currentBlock.AddInstruction(new IrInstruction(
                        IrOpCode.StructCopy,
                        reg,
                        [source],
                        SourceNodeId: copy.SourceNodeId));
                    return reg;
                }

                case AstSetFieldExpression setField:
                {
                    var target = EmitExpression(setField.Target, currentBlock);
                    var value = EmitExpression(setField.Value, currentBlock);
                    var reg = AllocateRegister(setField.Type);
                    currentBlock.AddInstruction(new IrInstruction(
                        IrOpCode.SetField,
                        reg,
                        [target, value],
                        StringPayload: setField.FieldKey,
                        SourceNodeId: setField.SourceNodeId));
                    return reg;
                }

                case AstListMakeExpression list:
                {
                    var reg = AllocateRegister(list.Type);
                    currentBlock.AddInstruction(new IrInstruction(
                        IrOpCode.ListMake,
                        reg,
                        SourceNodeId: list.SourceNodeId));
                    return reg;
                }

                case AstCollectionAddExpression add:
                {
                    var collection = EmitExpression(add.Collection, currentBlock);
                    var item = EmitExpression(add.Item, currentBlock);
                    var reg = AllocateRegister(add.Type);
                    currentBlock.AddInstruction(new IrInstruction(
                        IrOpCode.CollectionAdd,
                        reg,
                        [collection, item],
                        SourceNodeId: add.SourceNodeId));
                    return reg;
                }

                case AstCollectionSetExpression setItem:
                {
                    var collection = EmitExpression(setItem.Collection, currentBlock);
                    var index = EmitExpression(setItem.Index, currentBlock);
                    var item = EmitExpression(setItem.Item, currentBlock);
                    var reg = AllocateRegister(setItem.Type);
                    currentBlock.AddInstruction(new IrInstruction(
                        IrOpCode.CollectionSet,
                        reg,
                        [collection, index, item],
                        SourceNodeId: setItem.SourceNodeId));
                    return reg;
                }

                case AstCollectionRemoveExpression remove:
                {
                    var collection = EmitExpression(remove.Collection, currentBlock);
                    var index = EmitExpression(remove.Index, currentBlock);
                    var reg = AllocateRegister(remove.Type);
                    currentBlock.AddInstruction(new IrInstruction(
                        IrOpCode.CollectionRemove,
                        reg,
                        [collection, index],
                        SourceNodeId: remove.SourceNodeId));
                    return reg;
                }

                default:
                    return AllocateRegister(expr.Type);
            }
        }

        private IrBasicBlock EmitCountedLoop(
            string indexName,
            AstExpression start,
            AstExpression end,
            AstBlock body,
            IrBasicBlock currentBlock,
            NodeId? source)
        {
            var startReg = EmitExpression(start, currentBlock);
            StoreNamed(indexName, startReg, PrimitiveType.Int32, currentBlock, source);

            var header = CreateBlock("for_header");
            var loopBody = CreateBlock("for_body");
            var step = CreateBlock("for_step");
            var exit = CreateBlock("for_exit");
            currentBlock.SetTerminator(new IrInstruction(IrOpCode.Jump, SourceNodeId: source), header);

            var indexReg = LoadNamed(indexName, PrimitiveType.Int32, header, source);
            var endReg = EmitExpression(end, header);
            var cond = AllocateRegister(PrimitiveType.Bool);
            header.AddInstruction(new IrInstruction(IrOpCode.CmpLt, cond, [indexReg, endReg], SourceNodeId: source));
            header.SetTerminator(new IrInstruction(IrOpCode.BranchIf, null, [cond], SourceNodeId: source), loopBody, exit);

            var bodyEnd = EmitBlock(body, loopBody, implicitReturn: false);
            if (bodyEnd.Terminator is null)
            {
                bodyEnd.SetTerminator(new IrInstruction(IrOpCode.Jump), step);
            }

            var currentIndex = LoadNamed(indexName, PrimitiveType.Int32, step, source);
            var one = AllocateRegister(PrimitiveType.Int32);
            step.AddInstruction(new IrInstruction(IrOpCode.LoadConst, one, [new IrConstant(1, PrimitiveType.Int32)], SourceNodeId: source));
            var next = AllocateRegister(PrimitiveType.Int32);
            step.AddInstruction(new IrInstruction(IrOpCode.Add, next, [currentIndex, one], SourceNodeId: source));
            StoreNamed(indexName, next, PrimitiveType.Int32, step, source);
            step.SetTerminator(new IrInstruction(IrOpCode.Jump), header);
            return exit;
        }

        private IrBasicBlock EmitForEach(AstForEachStatement each, IrBasicBlock currentBlock)
        {
            var collection = EmitExpression(each.Collection, currentBlock);
            var indexName = each.ItemName + ".index";
            var zero = AllocateRegister(PrimitiveType.Int32);
            currentBlock.AddInstruction(new IrInstruction(IrOpCode.LoadConst, zero, [new IrConstant(0, PrimitiveType.Int32)], SourceNodeId: each.SourceNodeId));
            StoreNamed(indexName, zero, PrimitiveType.Int32, currentBlock, each.SourceNodeId);

            var header = CreateBlock("foreach_header");
            var loopBody = CreateBlock("foreach_body");
            var step = CreateBlock("foreach_step");
            var exit = CreateBlock("foreach_exit");
            currentBlock.SetTerminator(new IrInstruction(IrOpCode.Jump, SourceNodeId: each.SourceNodeId), header);

            var indexReg = LoadNamed(indexName, PrimitiveType.Int32, header, each.SourceNodeId);
            var length = AllocateRegister(PrimitiveType.Int32);
            header.AddInstruction(new IrInstruction(IrOpCode.CollectionLength, length, [collection], SourceNodeId: each.SourceNodeId));
            var cond = AllocateRegister(PrimitiveType.Bool);
            header.AddInstruction(new IrInstruction(IrOpCode.CmpLt, cond, [indexReg, length], SourceNodeId: each.SourceNodeId));
            header.SetTerminator(new IrInstruction(IrOpCode.BranchIf, null, [cond], SourceNodeId: each.SourceNodeId), loopBody, exit);

            var item = AllocateRegister(EntityType.EntityUid);
            loopBody.AddInstruction(new IrInstruction(IrOpCode.CollectionGet, item, [collection, indexReg], SourceNodeId: each.SourceNodeId));
            StoreNamed(each.ItemName, item, EntityType.EntityUid, loopBody, each.SourceNodeId);
            var bodyEnd = EmitBlock(each.Body, loopBody, implicitReturn: false);
            if (bodyEnd.Terminator is null)
            {
                bodyEnd.SetTerminator(new IrInstruction(IrOpCode.Jump), step);
            }

            var currentIndex = LoadNamed(indexName, PrimitiveType.Int32, step, each.SourceNodeId);
            var one = AllocateRegister(PrimitiveType.Int32);
            step.AddInstruction(new IrInstruction(IrOpCode.LoadConst, one, [new IrConstant(1, PrimitiveType.Int32)], SourceNodeId: each.SourceNodeId));
            var next = AllocateRegister(PrimitiveType.Int32);
            step.AddInstruction(new IrInstruction(IrOpCode.Add, next, [currentIndex, one], SourceNodeId: each.SourceNodeId));
            StoreNamed(indexName, next, PrimitiveType.Int32, step, each.SourceNodeId);
            step.SetTerminator(new IrInstruction(IrOpCode.Jump), header);
            return exit;
        }

        private static void StoreNamed(string name, IrRegister value, AstraType type, IrBasicBlock block, NodeId? source)
        {
            block.AddInstruction(new IrInstruction(
                IrOpCode.StoreVariable,
                null,
                [new IrVariable(SymbolId.Empty, type, name), value],
                SourceNodeId: source));
        }

        private IrRegister LoadNamed(string name, AstraType type, IrBasicBlock block, NodeId? source)
        {
            var reg = AllocateRegister(type);
            block.AddInstruction(new IrInstruction(
                IrOpCode.LoadVariable,
                reg,
                [new IrVariable(SymbolId.Empty, type, name)],
                SourceNodeId: source));
            return reg;
        }
    }
}
