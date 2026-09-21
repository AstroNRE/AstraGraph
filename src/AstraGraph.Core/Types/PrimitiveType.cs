namespace AstraGraph.Core;

public enum PrimitiveKind
{
    Void,
    Bool,
    Int8,
    Int16,
    Int32,
    Int64,
    UInt8,
    UInt16,
    UInt32,
    UInt64,
    Float32,
    Float64,
    String,
    TimeSpan
}

/// <summary>
/// Built-in primitive types in AstraGraph.
/// </summary>
public sealed record PrimitiveType(PrimitiveKind Kind, string Name, bool ValueType) : AstraType
{
    public override string TypeName => Name;
    public override bool IsValueType => ValueType;

    public static readonly PrimitiveType Void = new(PrimitiveKind.Void, "void", true);
    public static readonly PrimitiveType Bool = new(PrimitiveKind.Bool, "bool", true);
    public static readonly PrimitiveType Int8 = new(PrimitiveKind.Int8, "int8", true);
    public static readonly PrimitiveType Int16 = new(PrimitiveKind.Int16, "int16", true);
    public static readonly PrimitiveType Int32 = new(PrimitiveKind.Int32, "int32", true);
    public static readonly PrimitiveType Int64 = new(PrimitiveKind.Int64, "int64", true);
    public static readonly PrimitiveType UInt8 = new(PrimitiveKind.UInt8, "uint8", true);
    public static readonly PrimitiveType UInt16 = new(PrimitiveKind.UInt16, "uint16", true);
    public static readonly PrimitiveType UInt32 = new(PrimitiveKind.UInt32, "uint32", true);
    public static readonly PrimitiveType UInt64 = new(PrimitiveKind.UInt64, "uint64", true);
    public static readonly PrimitiveType Float32 = new(PrimitiveKind.Float32, "float32", true);
    public static readonly PrimitiveType Float64 = new(PrimitiveKind.Float64, "float64", true);
    public static readonly PrimitiveType String = new(PrimitiveKind.String, "string", false);
    public static readonly PrimitiveType TimeSpan = new(PrimitiveKind.TimeSpan, "TimeSpan", true);

    public bool IsInteger => Kind is PrimitiveKind.Int8 or PrimitiveKind.Int16 or PrimitiveKind.Int32 or PrimitiveKind.Int64
                                 or PrimitiveKind.UInt8 or PrimitiveKind.UInt16 or PrimitiveKind.UInt32 or PrimitiveKind.UInt64;

    public bool IsFloatingPoint => Kind is PrimitiveKind.Float32 or PrimitiveKind.Float64;

    public bool IsNumeric => IsInteger || IsFloatingPoint;
}
