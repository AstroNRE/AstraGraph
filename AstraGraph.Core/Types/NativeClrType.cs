namespace AstraGraph.Core;

/// <summary>
/// Represents a native C# / CLR type exposed from RobustToolbox or Content.
/// </summary>
public sealed record NativeClrType(Type ClrType) : AstraType
{
    public override string TypeName => ClrType.FullName ?? ClrType.Name;

    public override bool IsValueType => ClrType.IsValueType;

    public string AssemblyName => ClrType.Assembly.GetName().Name ?? string.Empty;

    public bool IsComponent => ClrType.Name.EndsWith("Component", StringComparison.Ordinal) ||
                               ClrType.GetInterfaces().Any(i => i.Name.Contains("Component", StringComparison.Ordinal));

    public bool IsSystem => ClrType.Name.EndsWith("System", StringComparison.Ordinal);

    public bool IsEvent => ClrType.Name.EndsWith("Event", StringComparison.Ordinal) ||
                           ClrType.Name.EndsWith("Message", StringComparison.Ordinal);
}
