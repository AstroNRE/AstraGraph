namespace AstraGraph.Core;

[Flags]
public enum SchemaFieldOptions
{
    None = 0,
    Persistent = 1 << 0,
    Replicated = 1 << 1,
    Predicted = 1 << 2,
    ReadOnly = 1 << 3
}

/// <summary>
/// A typed field definition within a component schema or struct.
/// </summary>
public sealed record SchemaField(
    FieldId Id,
    string Name,
    AstraType Type,
    string? DefaultValue = null,
    SchemaFieldOptions Options = SchemaFieldOptions.None)
{
    public bool IsPersistent => Options.HasFlag(SchemaFieldOptions.Persistent);
    public bool IsReplicated => Options.HasFlag(SchemaFieldOptions.Replicated);
    public bool IsPredicted => Options.HasFlag(SchemaFieldOptions.Predicted);
    public bool IsReadOnly => Options.HasFlag(SchemaFieldOptions.ReadOnly);
}

/// <summary>
/// Represents a user-defined dynamic component schema or struct schema.
/// Uses stable GUIDs for schema and fields to support live state migration.
/// </summary>
public sealed record SchemaType(
    SchemaId Id,
    string Name,
    bool IsComponentSchema,
    IReadOnlyList<SchemaField> Fields) : AstraType
{
    public override string TypeName => Name;

    public override bool IsValueType => !IsComponentSchema;

    private readonly Dictionary<FieldId, SchemaField> _fieldsById = Fields.ToDictionary(f => f.Id);
    private readonly Dictionary<string, SchemaField> _fieldsByName = Fields.ToDictionary(f => f.Name, StringComparer.Ordinal);

    public SchemaField? FindField(FieldId fieldId) => _fieldsById.GetValueOrDefault(fieldId);

    public SchemaField? FindField(string fieldName) => _fieldsByName.GetValueOrDefault(fieldName);

    public int FieldCount => Fields.Count;

    public int GetFieldIndex(FieldId fieldId)
    {
        for (var i = 0; i < Fields.Count; i++)
        {
            if (Fields[i].Id == fieldId) return i;
        }
        return -1;
    }
}
