using System.Globalization;
using AstraGraph.Core;

namespace AstraGraph.State;

public sealed record SchemaBindResult(bool Success, AstraValue[] Values, IReadOnlyList<Diagnostic> Diagnostics)
{
    public static SchemaBindResult Ok(AstraValue[] values) => new(true, values, []);

    public static SchemaBindResult Fail(params Diagnostic[] diagnostics) => new(false, [], diagnostics);
}

/// <summary>
/// Turns a prototype mapping into schema slot values. Missing keys use schema defaults.
/// </summary>
public static class SchemaYamlBinder
{
    public static SchemaBindResult Bind(
        SchemaType schema,
        IReadOnlyDictionary<string, object?> fields,
        string? prototypeName = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(fields);

        var diagnostics = new List<Diagnostic>();
        var known = new HashSet<string>(schema.Fields.Select(field => field.Name), StringComparer.Ordinal);
        foreach (var key in fields.Keys)
        {
            if (!known.Contains(key))
            {
                diagnostics.Add(UnknownField(prototypeName, schema.Name, key));
            }
        }

        var values = new AstraValue[schema.FieldCount];
        for (var i = 0; i < schema.FieldCount; i++)
        {
            var field = schema.Fields[i];
            if (!fields.TryGetValue(field.Name, out var raw))
            {
                values[i] = DefaultValue(field);
                continue;
            }

            if (!TryConvert(field, raw, out var converted, out var diagnostic, prototypeName, schema.Name))
            {
                diagnostics.Add(diagnostic!);
                values[i] = AstraValue.Null;
                continue;
            }

            values[i] = converted;
        }

        return diagnostics.Count == 0
            ? SchemaBindResult.Ok(values)
            : new SchemaBindResult(false, values, diagnostics);
    }

    public static AstraValue DefaultValue(SchemaField field)
    {
        if (string.IsNullOrEmpty(field.DefaultValue))
        {
            return Zero(field.Type);
        }

        return TryConvert(field, field.DefaultValue, out var value, out _, null, field.Name)
            ? value
            : Zero(field.Type);
    }

    public static bool TryConvert(
        SchemaField field,
        object? raw,
        out AstraValue value,
        out Diagnostic? diagnostic,
        string? prototypeName,
        string schemaName)
    {
        value = AstraValue.Null;
        diagnostic = null;
        if (raw is null)
        {
            value = Zero(field.Type);
            return true;
        }

        if (field.Type is ProtoIdType || field.Type == PrimitiveType.String)
        {
            value = AstraValue.FromString(raw.ToString());
            return true;
        }

        if (field.Type is EnumType enumType)
        {
            var text = raw.ToString() ?? string.Empty;
            if (enumType.TryGetValue(text, out var member))
            {
                value = AstraValue.FromInt64(member);
                return true;
            }

            diagnostic = TypeMismatch(prototypeName, schemaName, field, "enum", raw);
            return false;
        }

        if (field.Type == PrimitiveType.Bool)
        {
            if (raw is bool flag)
            {
                value = AstraValue.FromBool(flag);
                return true;
            }

            if (bool.TryParse(raw.ToString(), out var parsed))
            {
                value = AstraValue.FromBool(parsed);
                return true;
            }

            diagnostic = TypeMismatch(prototypeName, schemaName, field, Received(raw), raw);
            return false;
        }

        if (field.Type == EntityType.EntityUid)
        {
            if (TryInteger(raw, out var entity))
            {
                value = AstraValue.FromEntityUid((int)entity);
                return true;
            }

            diagnostic = TypeMismatch(prototypeName, schemaName, field, Received(raw), raw);
            return false;
        }

        if (field.Type is PrimitiveType primitive && primitive.IsInteger)
        {
            if (TryInteger(raw, out var integer))
            {
                value = AstraValue.FromInt64(integer);
                return true;
            }

            diagnostic = TypeMismatch(prototypeName, schemaName, field, Received(raw), raw);
            return false;
        }

        if (field.Type is PrimitiveType floating && floating.IsFloatingPoint)
        {
            if (raw is float or double)
            {
                value = AstraValue.FromDouble(Convert.ToDouble(raw, CultureInfo.InvariantCulture));
                return true;
            }

            if (double.TryParse(raw.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                value = AstraValue.FromDouble(number);
                return true;
            }

            diagnostic = TypeMismatch(prototypeName, schemaName, field, Received(raw), raw);
            return false;
        }

        if (field.Type is CollectionType collection &&
            collection.ElementType == PrimitiveType.String &&
            collection.Kind is CollectionKind.List or CollectionKind.Set)
        {
            value = AstraValue.FromObject(ReadStringList(raw));
            return true;
        }

        if (field.Type is NullableType nullable)
        {
            var inner = new SchemaField(field.Id, field.Name, nullable.UnderlyingType, field.DefaultValue, field.Options);
            return TryConvert(inner, raw, out value, out diagnostic, prototypeName, schemaName);
        }

        value = AstraValue.FromObject(raw);
        return true;
    }

    public static Diagnostic UnknownField(string? prototypeName, string schemaName, string fieldName)
    {
        var where = string.IsNullOrEmpty(prototypeName) ? string.Empty : $"Prototype {prototypeName}{Environment.NewLine}";
        return new Diagnostic(
            DiagnosticCodes.UnknownField,
            DiagnosticSeverity.Error,
            $"{where}Component {schemaName}{Environment.NewLine}Field {fieldName}{Environment.NewLine}Unknown field");
    }

    public static Diagnostic TypeMismatch(string? prototypeName, string schemaName, SchemaField field, string received, object? raw)
    {
        var where = string.IsNullOrEmpty(prototypeName) ? string.Empty : $"Prototype {prototypeName}{Environment.NewLine}";
        _ = raw;
        return new Diagnostic(
            DiagnosticCodes.InvalidYamlField,
            DiagnosticSeverity.Error,
            $"{where}Component {schemaName}{Environment.NewLine}Field {field.Name}{Environment.NewLine}Expected: {Expected(field.Type)}{Environment.NewLine}Received: {received}");
    }

    public static string Expected(AstraType type) => type switch
    {
        PrimitiveType primitive when primitive.Kind == PrimitiveKind.Int32 => "Int32",
        PrimitiveType primitive when primitive.Kind == PrimitiveKind.Int64 => "Int64",
        PrimitiveType primitive when primitive.Kind == PrimitiveKind.Float32 => "Float",
        PrimitiveType primitive when primitive.Kind == PrimitiveKind.Float64 => "Double",
        PrimitiveType primitive when primitive.Kind == PrimitiveKind.Bool => "Bool",
        PrimitiveType primitive when primitive.Kind == PrimitiveKind.String => "String",
        ProtoIdType => "EntProtoId",
        EnumType enumType => enumType.Name,
        _ => type.TypeName
    };

    private static string Received(object raw) => raw switch
    {
        string => "string",
        bool => "bool",
        int or long or short or byte => "number",
        float or double => "number",
        _ => raw.GetType().Name
    };

    private static AstraValue Zero(AstraType type)
    {
        if (type is ProtoIdType || type == PrimitiveType.String)
        {
            return AstraValue.FromString(string.Empty);
        }

        if (type == PrimitiveType.Bool)
        {
            return AstraValue.False;
        }

        if (type is PrimitiveType primitive && primitive.IsFloatingPoint)
        {
            return AstraValue.FromDouble(0);
        }

        if (type == EntityType.EntityUid)
        {
            return AstraValue.FromEntityUid(0);
        }

        if (type is PrimitiveType integer && integer.IsInteger)
        {
            return AstraValue.FromInt64(0);
        }

        if (type is CollectionType collection &&
            collection.ElementType == PrimitiveType.String &&
            collection.Kind is CollectionKind.List or CollectionKind.Set)
        {
            return AstraValue.FromObject(new AstraList());
        }

        return AstraValue.Null;
    }

    public const char ListSeparator = '\u001f';

    public static AstraList ReadStringList(object? raw)
    {
        var list = new AstraList();
        if (raw is null)
        {
            return list;
        }

        if (raw is string text)
        {
            if (text.Length == 0)
            {
                return list;
            }

            foreach (var piece in text.Split(ListSeparator))
            {
                list.Add(AstraValue.FromString(piece));
            }

            return list;
        }

        if (raw is System.Collections.IEnumerable items)
        {
            foreach (var item in items)
            {
                if (item is null)
                {
                    continue;
                }

                list.Add(AstraValue.FromString(item.ToString()));
            }
        }

        return list;
    }

    private static bool TryInteger(object raw, out long integer)
    {
        switch (raw)
        {
            case int value:
                integer = value;
                return true;
            case long value:
                integer = value;
                return true;
            case short value:
                integer = value;
                return true;
            case byte value:
                integer = value;
                return true;
            default:
                return long.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out integer);
        }
    }
}
