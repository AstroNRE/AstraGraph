using System.Text;

namespace AstraGraph.Core;

/// <summary>
/// Fast binary serializer for Astra bytecode streams.
/// </summary>
public static class BytecodeSerializer
{
    public static byte[] SerializeToBytes(BytecodeProgram program)
    {
        using var ms = new MemoryStream();
        Serialize(program, ms);
        return ms.ToArray();
    }

    public static void Serialize(BytecodeProgram program, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(stream);

        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        // Header
        writer.Write(BytecodeProgram.MagicHeader);
        writer.Write(BytecodeProgram.FormatVersion);
        writer.Write(program.Id.Value.ToByteArray());
        writer.Write(program.Revision.Value.ToByteArray());
        writer.Write(program.SemanticHash);

        // Constant Pool
        writer.Write(program.Constants.Count);
        foreach (var entry in program.Constants.Entries)
        {
            writer.Write((byte)entry.Kind);
            switch (entry.Kind)
            {
                case ConstantKind.Null:
                    break;
                case ConstantKind.Bool:
                    writer.Write((bool)entry.Value!);
                    break;
                case ConstantKind.Int64:
                    writer.Write((long)entry.Value!);
                    break;
                case ConstantKind.Double:
                    writer.Write((double)entry.Value!);
                    break;
                case ConstantKind.String:
                    writer.Write((string)entry.Value!);
                    break;
                case ConstantKind.Guid:
                    writer.Write(((Guid)entry.Value!).ToByteArray());
                    break;
            }
        }

        // Functions
        WriteFunctions(writer, program.Functions);

        // EntryPoints
        WriteFunctions(writer, program.EntryPoints);
    }

    private static void WriteFunctions(BinaryWriter writer, List<BytecodeFunction> functions)
    {
        writer.Write(functions.Count);
        foreach (var func in functions)
        {
            writer.Write(func.NameConstantIndex);
            writer.Write(func.RegisterCount);
            writer.Write(func.ParameterCount);
            writer.Write(func.Instructions.Count);

            foreach (var instr in func.Instructions)
            {
                writer.Write(instr.OpCode);
                writer.Write(instr.DestRegister);
                writer.Write(instr.Op1);
                writer.Write(instr.Op2);
                writer.Write(instr.Extra);
            }
        }
    }

    public static BytecodeProgram DeserializeFromBytes(ReadOnlySpan<byte> bytes)
    {
        using var ms = new MemoryStream(bytes.ToArray());
        return Deserialize(ms);
    }

    public static BytecodeProgram Deserialize(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var magic = reader.ReadUInt32();
        if (magic != BytecodeProgram.MagicHeader)
        {
            throw new InvalidDataException($"Invalid bytecode magic header: 0x{magic:X8}, expected 0x{BytecodeProgram.MagicHeader:X8}.");
        }

        var version = reader.ReadByte();
        if (version != BytecodeProgram.FormatVersion)
        {
            throw new InvalidDataException($"Unsupported bytecode version {version}.");
        }

        var graphId = new GraphId(new Guid(reader.ReadBytes(16)));
        var revisionId = new RevisionId(new Guid(reader.ReadBytes(16)));
        var semanticHash = reader.ReadString();

        var constCount = reader.ReadInt32();
        var pool = new ConstantPool();

        for (var i = 0; i < constCount; i++)
        {
            var kind = (ConstantKind)reader.ReadByte();
            object? val = kind switch
            {
                ConstantKind.Null => null,
                ConstantKind.Bool => reader.ReadBoolean(),
                ConstantKind.Int64 => reader.ReadInt64(),
                ConstantKind.Double => reader.ReadDouble(),
                ConstantKind.String => reader.ReadString(),
                ConstantKind.Guid => new Guid(reader.ReadBytes(16)),
                _ => throw new InvalidDataException($"Unknown constant kind {kind}.")
            };
            pool.AddEntry(new ConstantEntry(kind, val));
        }

        var program = new BytecodeProgram(graphId, revisionId, semanticHash, pool);

        // Read Functions
        ReadFunctions(reader, program.Functions);

        // Read EntryPoints
        ReadFunctions(reader, program.EntryPoints);

        return program;
    }

    private static void ReadFunctions(BinaryReader reader, List<BytecodeFunction> list)
    {
        var count = reader.ReadInt32();
        for (var i = 0; i < count; i++)
        {
            var nameIdx = reader.ReadInt32();
            var regCount = reader.ReadInt32();
            var paramCount = reader.ReadInt32();
            var instrCount = reader.ReadInt32();

            var instrs = new BytecodeInstruction[instrCount];
            for (var j = 0; j < instrCount; j++)
            {
                var opCode = reader.ReadByte();
                var destReg = reader.ReadUInt16();
                var op1 = reader.ReadInt32();
                var op2 = reader.ReadInt32();
                var extra = reader.ReadInt32();
                instrs[j] = new BytecodeInstruction(opCode, destReg, op1, op2, extra);
            }

            list.Add(new BytecodeFunction(nameIdx, regCount, paramCount, instrs));
        }
    }
}
