namespace AstraGraph.Core;

/// <summary>
/// A flat instruction within an IR basic block.
/// </summary>
public sealed record IrInstruction(
    IrOpCode OpCode,
    IrRegister? Destination = null,
    IReadOnlyList<IrOperand>? Operands = null,
    string? StringPayload = null,
    object? Metadata = null,
    NodeId? SourceNodeId = null)
{
    public override string ToString()
    {
        var dest = Destination != null ? $"{Destination} = " : string.Empty;
        var ops = Operands != null && Operands.Count > 0 ? string.Join(", ", Operands) : string.Empty;
        var payload = StringPayload != null ? $" \"{StringPayload}\"" : string.Empty;
        return $"{dest}{OpCode} {ops}{payload}".Trim();
    }
}

/// <summary>
/// A single-entry, single-exit basic block in the Control-Flow Graph (CFG).
/// </summary>
public sealed class IrBasicBlock
{
    public int Id { get; }
    public string Name { get; }
    public List<IrInstruction> Instructions { get; } = [];
    public IrInstruction? Terminator { get; set; }

    public List<IrBasicBlock> Predecessors { get; } = [];
    public List<IrBasicBlock> Successors { get; } = [];

    public IrBasicBlock(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public void AddInstruction(IrInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        Instructions.Add(instruction);
    }

    public void SetTerminator(IrInstruction terminator, params IrBasicBlock[] targetSuccessors)
    {
        ArgumentNullException.ThrowIfNull(terminator);
        Terminator = terminator;

        Successors.Clear();
        foreach (var succ in targetSuccessors)
        {
            if (succ is not null)
            {
                Successors.Add(succ);
                if (!succ.Predecessors.Contains(this))
                {
                    succ.Predecessors.Add(this);
                }
            }
        }
    }

    public override string ToString() => $"block {Name} ({Instructions.Count} instrs, term: {Terminator?.OpCode})";
}

/// <summary>
/// An executable function or event handler represented as a CFG of basic blocks.
/// </summary>
public sealed class IrFunction
{
    public string Name { get; }
    public IReadOnlyList<IrVariable> Parameters { get; }
    public AstraType ReturnType { get; }
    public List<IrBasicBlock> Blocks { get; } = [];
    public IrBasicBlock EntryBlock { get; }
    public int RegisterCount { get; set; }
    public EntryPointTrigger Trigger { get; set; } = EntryPointTrigger.Update;

    public IrFunction(string name, IReadOnlyList<IrVariable> parameters, AstraType returnType, IrBasicBlock entryBlock)
    {
        Name = name;
        Parameters = parameters;
        ReturnType = returnType;
        EntryBlock = entryBlock;
        Blocks.Add(entryBlock);
    }

    public IrBasicBlock CreateBlock(string name)
    {
        var block = new IrBasicBlock(Blocks.Count, name);
        Blocks.Add(block);
        return block;
    }

    public IrBasicBlock? FindBlock(string name) => Blocks.FirstOrDefault(b => b.Name == name);
}

/// <summary>
/// Intermediate representation of an entire AstraGraph program.
/// </summary>
public sealed class IrProgram
{
    public GraphId Id { get; }
    public string Name { get; }
    public GraphKind Kind { get; }
    public GraphSide Side { get; }
    public List<IrVariable> Variables { get; } = [];
    public List<IrFunction> Functions { get; } = [];
    public List<IrFunction> EntryPoints { get; } = [];

    public IrProgram(GraphId id, string name, GraphKind kind, GraphSide side)
    {
        Id = id;
        Name = name;
        Kind = kind;
        Side = side;
    }

    public IrFunction? FindFunction(string name) =>
        Functions.FirstOrDefault(f => f.Name == name) ?? EntryPoints.FirstOrDefault(e => e.Name == name);
}
