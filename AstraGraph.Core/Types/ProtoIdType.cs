namespace AstraGraph.Core;

/// <summary>
/// Prototype id carried as text. The engine binding decides which prototype kind it names.
/// </summary>
public sealed record ProtoIdType(string Name) : AstraType
{
    public override string TypeName => Name;

    public override bool IsValueType => true;

    public static readonly ProtoIdType EntProtoId = new("EntProtoId");

    public static readonly ProtoIdType ProtoId = new("ProtoId");
}
