namespace AstraGraph.Core;

public sealed record EnumMember(string Name, int Value);

/// <summary>
/// User-defined enumeration type in AstraGraph.
/// </summary>
public sealed record EnumType(
    SymbolId Id,
    string Name,
    IReadOnlyList<EnumMember> Members) : AstraType
{
    public override string TypeName => Name;

    public override bool IsValueType => true;

    private readonly Dictionary<string, int> _byName = Members.ToDictionary(m => m.Name, m => m.Value, StringComparer.Ordinal);
    private readonly Dictionary<int, string> _byValue = Members.ToDictionary(m => m.Value, m => m.Name);

    public bool TryGetValue(string name, out int value) => _byName.TryGetValue(name, out value);

    public bool TryGetName(int value, out string? name) => _byValue.TryGetValue(value, out name);
}

public enum CollectionKind
{
    List,
    Set,
    Dictionary
}

/// <summary>
/// Statically-typed collection (List, Set, Dictionary).
/// </summary>
public sealed record CollectionType : AstraType
{
    public CollectionKind Kind { get; }
    public AstraType ElementType { get; }
    public AstraType? KeyType { get; }

    public override string TypeName { get; }
    public override bool IsValueType => false;

    public CollectionType(CollectionKind kind, AstraType elementType, AstraType? keyType = null)
    {
        Kind = kind;
        ElementType = elementType;
        KeyType = keyType;

        TypeName = kind switch
        {
            CollectionKind.List => $"List<{elementType.TypeName}>",
            CollectionKind.Set => $"Set<{elementType.TypeName}>",
            CollectionKind.Dictionary when keyType is not null => $"Dictionary<{keyType.TypeName}, {elementType.TypeName}>",
            _ => throw new ArgumentException($"Invalid collection kind {kind} or missing keyType.")
        };
    }
}

/// <summary>
/// Explicit nullable type wrapper (T?).
/// </summary>
public sealed record NullableType(AstraType UnderlyingType) : AstraType
{
    public override string TypeName => $"{UnderlyingType.TypeName}?";

    public override bool IsValueType => false;

    public override bool IsNullable => true;
}

/// <summary>
/// Reusable function signature type.
/// </summary>
public sealed record FunctionType(
    IReadOnlyList<AstraType> ParameterTypes,
    AstraType ReturnType) : AstraType
{
    public override string TypeName => $"({string.Join(", ", ParameterTypes.Select(p => p.TypeName))}) -> {ReturnType.TypeName}";

    public override bool IsValueType => false;
}
