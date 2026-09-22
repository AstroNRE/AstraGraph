using System.Reflection;
using AstraGraph.Core;

namespace AstraGraph.Binding;

public sealed record NativeParameterDescriptor(
    string Name,
    Type ClrType,
    AstraType Type,
    bool IsOptional = false,
    object? DefaultValue = null);

/// <summary>
/// Complete runtime descriptor and fast invoker for an indexed native C# method.
/// </summary>
public sealed class NativeMethodDescriptor
{
    public string Name { get; }
    public string DeclaringTypeName { get; }
    public string Descriptor { get; }
    public MethodInfo Method { get; }
    public IReadOnlyList<NativeParameterDescriptor> Parameters { get; }
    public Type ReturnClrType { get; }
    public AstraType ReturnType { get; }
    public GraphSide Side { get; }
    public bool IsPure { get; }
    public bool IsDeterministic { get; }
    public SecurityProfile RequiredProfile { get; }
    public int Cost { get; }
    public Func<AstraValue[], AstraValue> Invoker { get; }

    public NativeMethodDescriptor(
        string name,
        string declaringTypeName,
        string descriptor,
        MethodInfo method,
        IReadOnlyList<NativeParameterDescriptor> parameters,
        Type returnClrType,
        AstraType returnType,
        GraphSide side,
        bool isPure,
        bool isDeterministic,
        SecurityProfile requiredProfile,
        int cost,
        Func<AstraValue[], AstraValue> invoker)
    {
        Name = name;
        DeclaringTypeName = declaringTypeName;
        Descriptor = descriptor;
        Method = method;
        Parameters = parameters;
        ReturnClrType = returnClrType;
        ReturnType = returnType;
        Side = side;
        IsPure = isPure;
        IsDeterministic = isDeterministic;
        RequiredProfile = requiredProfile;
        Cost = cost;
        Invoker = invoker;
    }

    public override string ToString() => Descriptor;
}

public sealed class NativePropertyDescriptor
{
    public string Name { get; }
    public string DeclaringTypeName { get; init; } = string.Empty;
    public PropertyInfo? Property { get; }
    public AstraType Type { get; }
    public bool CanRead { get; init; }
    public bool CanWrite { get; init; }
    public bool IsField { get; init; }
    public GraphSide Side { get; init; } = GraphSide.Shared;
    public Func<AstraValue, AstraValue>? Getter { get; }
    public Action<AstraValue, AstraValue>? Setter { get; }

    public NativePropertyDescriptor(
        string name,
        PropertyInfo? property,
        AstraType type,
        Func<AstraValue, AstraValue>? getter = null,
        Action<AstraValue, AstraValue>? setter = null)
    {
        Name = name;
        Property = property;
        Type = type;
        Getter = getter;
        Setter = setter;
        CanRead = getter != null;
        CanWrite = setter != null;
    }
}

public sealed class NativeTypeDescriptor
{
    public Type ClrType { get; }
    public string Name { get; }
    public List<NativeMethodDescriptor> Methods { get; } = [];
    public List<NativePropertyDescriptor> Properties { get; } = [];

    public NativeTypeDescriptor(Type clrType, string name)
    {
        ClrType = clrType;
        Name = name;
    }
}
