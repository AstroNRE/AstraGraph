namespace AstraGraph.Core;

/// <summary>
/// Static rules governing type compatibility, assignment, and numeric widening.
/// </summary>
public static class TypeCoercionRules
{
    public static bool IsAssignableFrom(AstraType target, AstraType source)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);

        // 1. Identical types
        if (target.Equals(source))
        {
            return true;
        }

        // 2. Nullable target: T? can receive T or S where T is assignable from S
        if (target is NullableType targetNullable)
        {
            if (source is NullableType sourceNullable)
            {
                return IsAssignableFrom(targetNullable.UnderlyingType, sourceNullable.UnderlyingType);
            }
            return IsAssignableFrom(targetNullable.UnderlyingType, source);
        }

        // Target is NOT nullable, but source IS nullable: reject implicit assignment
        if (source is NullableType)
        {
            return false;
        }

        // 3. Primitive numeric widening
        if (target is PrimitiveType targetPrim && source is PrimitiveType sourcePrim)
        {
            return IsNumericWideningAllowed(targetPrim.Kind, sourcePrim.Kind);
        }

        // 4. Native CLR type hierarchy
        if (target is NativeClrType targetClr && source is NativeClrType sourceClr)
        {
            return targetClr.ClrType.IsAssignableFrom(sourceClr.ClrType);
        }

        // 5. Schema types must match exactly by SchemaId
        if (target is SchemaType targetSchema && source is SchemaType sourceSchema)
        {
            return targetSchema.Id == sourceSchema.Id;
        }

        // 6. Enum types must match by SymbolId
        if (target is EnumType targetEnum && source is EnumType sourceEnum)
        {
            return targetEnum.Id == sourceEnum.Id;
        }

        // 7. Collection covariance / invariance
        if (target is CollectionType targetCol && source is CollectionType sourceCol)
        {
            if (targetCol.Kind != sourceCol.Kind) return false;
            if (targetCol.Kind == CollectionKind.Dictionary)
            {
                return targetCol.KeyType != null && sourceCol.KeyType != null &&
                       targetCol.KeyType.Equals(sourceCol.KeyType) &&
                       targetCol.ElementType.Equals(sourceCol.ElementType);
            }
            return targetCol.ElementType.Equals(sourceCol.ElementType);
        }

        return false;
    }

    private static bool IsNumericWideningAllowed(PrimitiveKind target, PrimitiveKind source)
    {
        return (target, source) switch
        {
            (PrimitiveKind.Int16, PrimitiveKind.Int8) => true,
            (PrimitiveKind.Int32, PrimitiveKind.Int8 or PrimitiveKind.Int16) => true,
            (PrimitiveKind.Int64, PrimitiveKind.Int8 or PrimitiveKind.Int16 or PrimitiveKind.Int32) => true,

            (PrimitiveKind.UInt16, PrimitiveKind.UInt8) => true,
            (PrimitiveKind.UInt32, PrimitiveKind.UInt8 or PrimitiveKind.UInt16) => true,
            (PrimitiveKind.UInt64, PrimitiveKind.UInt8 or PrimitiveKind.UInt16 or PrimitiveKind.UInt32) => true,

            // Unsigned to larger signed
            (PrimitiveKind.Int16, PrimitiveKind.UInt8) => true,
            (PrimitiveKind.Int32, PrimitiveKind.UInt8 or PrimitiveKind.UInt16) => true,
            (PrimitiveKind.Int64, PrimitiveKind.UInt8 or PrimitiveKind.UInt16 or PrimitiveKind.UInt32) => true,

            // Floating point widening
            (PrimitiveKind.Float64, PrimitiveKind.Float32) => true,
            (PrimitiveKind.Float32, PrimitiveKind.Int8 or PrimitiveKind.Int16 or PrimitiveKind.UInt8 or PrimitiveKind.UInt16) => true,
            (PrimitiveKind.Float64, PrimitiveKind.Int8 or PrimitiveKind.Int16 or PrimitiveKind.Int32 or PrimitiveKind.UInt8 or PrimitiveKind.UInt16 or PrimitiveKind.UInt32) => true,

            _ => false
        };
    }
}
