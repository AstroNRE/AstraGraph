using System.Globalization;
using AstraGraph.Core;

namespace AstraGraph.State;

/// <summary>
/// Schemas discovered before prototype load. Lookup is by stable id and by the SS14 component name.
/// </summary>
public sealed class AstraSchemaRegistry : IAstraSchemaRegistry
{
    private readonly Dictionary<SchemaId, SchemaType> _byId = [];
    private readonly Dictionary<string, SchemaType> _byName = new(StringComparer.Ordinal);

    public void RegisterSchema(SchemaType schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        _byId[schema.Id] = schema;
        _byName[schema.Name] = schema;
    }

    public bool TryGetSchema(string name, out SchemaType? schema) =>
        _byName.TryGetValue(name, out schema);

    public bool TryGetSchema(SchemaId id, out SchemaType? schema) =>
        _byId.TryGetValue(id, out schema);

    public SchemaField? ResolveField(SchemaType schema, string fieldName) => schema.FindField(fieldName);

    public SchemaField? ResolveField(SchemaType schema, FieldId fieldId) => schema.FindField(fieldId);

    public AstraValue[] CreateDefaultInstance(SchemaType schema)
    {
        var values = new AstraValue[schema.FieldCount];
        for (var i = 0; i < schema.FieldCount; i++)
        {
            values[i] = SchemaYamlBinder.DefaultValue(schema.Fields[i]);
        }

        return values;
    }

    public SchemaBindResult ValidateValue(SchemaType schema, string fieldName, object? raw, string? prototypeName = null)
    {
        var field = schema.FindField(fieldName);
        if (field == null)
        {
            return SchemaBindResult.Fail(SchemaYamlBinder.UnknownField(prototypeName, schema.Name, fieldName));
        }

        if (!SchemaYamlBinder.TryConvert(field, raw, out var value, out var diagnostic, prototypeName, schema.Name))
        {
            return SchemaBindResult.Fail(diagnostic!);
        }

        return SchemaBindResult.Ok([value]);
    }

    public IReadOnlyList<SchemaType> All() => _byName.Values.ToArray();
}

public interface IAstraSchemaRegistry
{
    void RegisterSchema(SchemaType schema);

    bool TryGetSchema(string name, out SchemaType? schema);

    bool TryGetSchema(SchemaId id, out SchemaType? schema);

    SchemaField? ResolveField(SchemaType schema, string fieldName);

    SchemaField? ResolveField(SchemaType schema, FieldId fieldId);

    AstraValue[] CreateDefaultInstance(SchemaType schema);

    SchemaBindResult ValidateValue(SchemaType schema, string fieldName, object? raw, string? prototypeName = null);
}
