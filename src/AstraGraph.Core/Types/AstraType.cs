namespace AstraGraph.Core;

/// <summary>
/// Abstract base record for all types in the statically-typed AstraGraph language.
/// </summary>
public abstract record AstraType
{
    public abstract string TypeName { get; }

    public abstract bool IsValueType { get; }

    public virtual bool IsNullable => false;

    public override string ToString() => TypeName;

    /// <summary>
    /// Checks whether a value of type <paramref name="from"/> can be assigned to this type
    /// according to language assignment and widening rules.
    /// </summary>
    public bool IsAssignableFrom(AstraType from) => TypeCoercionRules.IsAssignableFrom(this, from);
}
