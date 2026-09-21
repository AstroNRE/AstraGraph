namespace AstraGraph.Binding;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class AstraCallableAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
public sealed class AstraPureAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class AstraServerAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class AstraClientAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class AstraSharedAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class AstraPredictedAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Class)]
public sealed class AstraHiddenAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
public sealed class AstraCostAttribute : Attribute
{
    public int CostUnits { get; }

    public AstraCostAttribute(int costUnits)
    {
        CostUnits = costUnits;
    }
}
