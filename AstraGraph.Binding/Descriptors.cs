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
    public PropertyInfo Property { get; }
    public AstraType Type { get; }
    public Func<AstraValue, AstraValue>? Getter { get; }
    public Action<AstraValue, AstraValue>? Setter { get; }

    public NativePropertyDescriptor(
        string Name,
        PropertyInfo Property,
        AstraType Type,
        Func<AstraValue, AstraValue>? Getter = null,
        Action<AstraValue, AstraValue>? Setter = null)
    {
        this.Name = Name;
        this.Property = Property;
        this.Type = Type;
        this.Getter = Getter;
        this.Setter = Setter;
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
