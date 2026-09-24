using System.Globalization;

namespace AstraGraph.UI.Catalog;

/// <summary>
/// Converts authored property values onto the CLR type a control property expects.
/// </summary>
public static class UiValueCoercion
{
    public static bool TryCoerce(object? value, Type targetType, out object? coerced)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (value is null)
        {
            coerced = null;
            return Nullable.GetUnderlyingType(targetType) != null || !targetType.IsValueType;
        }

        if (underlying.IsInstanceOfType(value))
        {
            coerced = value;
            return true;
        }

        if (underlying.IsEnum)
        {
            if (value is string text && Enum.TryParse(underlying, text, ignoreCase: true, out var parsed))
            {
                coerced = parsed;
                return true;
            }

            coerced = null;
            return false;
        }

        if (underlying == typeof(string))
        {
            coerced = Convert.ToString(value, CultureInfo.InvariantCulture);
            return true;
        }

        try
        {
            if (underlying == typeof(bool) && value is string flag && bool.TryParse(flag, out var parsedFlag))
            {
                coerced = parsedFlag;
                return true;
            }

            coerced = Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            coerced = null;
            return false;
        }
    }

    public static bool TypesCompatible(string? targetTypeName, string? sourceTypeName, string? converter)
    {
        if (!string.IsNullOrWhiteSpace(converter))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(targetTypeName) || string.IsNullOrWhiteSpace(sourceTypeName))
        {
            return true;
        }

        var target = Normalize(targetTypeName);
        var source = Normalize(sourceTypeName);
        if (target.Equals(source, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (target is "string")
        {
            return false;
        }

        if (target is "float" or "double" or "single" && source is "int" or "int32" or "integer" or "long" or "float" or "double")
        {
            return true;
        }

        if (target is "int" or "int32" or "integer" && source is "int" or "int32" or "integer" or "long")
        {
            return true;
        }

        return false;
    }

    public static string Normalize(string typeName)
    {
        var name = typeName.Contains('.', StringComparison.Ordinal)
            ? typeName[(typeName.LastIndexOf('.') + 1)..]
            : typeName;
        return name.ToLowerInvariant() switch
        {
            "int32" or "integer" => "int",
            "single" => "float",
            "boolean" => "bool",
            _ => name.ToLowerInvariant()
        };
    }
}
