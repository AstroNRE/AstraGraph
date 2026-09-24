namespace AstraGraph.Core;

/// <summary>
/// One struct value. Field identity is the schema id plus the field id.
/// The display name is stored so a graph can still read by name.
/// </summary>
public sealed class AstraStruct
{
    private readonly List<Slot> _fields = [];

    public AstraStruct(SchemaId schemaId, string schemaName)
    {
        SchemaId = schemaId;
        SchemaName = schemaName ?? string.Empty;
    }

    public SchemaId SchemaId { get; }

    public string SchemaName { get; }

    public IReadOnlyList<Slot> Fields => _fields;

    public AstraValue Get(FieldId fieldId, string name)
    {
        if (fieldId.Value != Guid.Empty)
        {
            foreach (var field in _fields)
            {
                if (field.Id == fieldId)
                {
                    return field.Value;
                }
            }
        }

        foreach (var field in _fields)
        {
            if (string.Equals(field.Name, name, StringComparison.Ordinal))
            {
                return field.Value;
            }
        }

        return AstraValue.Null;
    }

    public void Set(FieldId fieldId, string name, AstraValue value)
    {
        name ??= string.Empty;
        if (fieldId.Value != Guid.Empty)
        {
            for (var i = 0; i < _fields.Count; i++)
            {
                if (_fields[i].Id != fieldId)
                {
                    continue;
                }

                _fields[i] = new Slot(fieldId, name, value);
                return;
            }
        }

        for (var i = 0; i < _fields.Count; i++)
        {
            if (!string.Equals(_fields[i].Name, name, StringComparison.Ordinal))
            {
                continue;
            }

            var id = fieldId.Value == Guid.Empty ? _fields[i].Id : fieldId;
            _fields[i] = new Slot(id, name, value);
            return;
        }

        _fields.Add(new Slot(fieldId, name, value));
    }

    public AstraStruct Copy()
    {
        var copy = new AstraStruct(SchemaId, SchemaName);
        foreach (var field in _fields)
        {
            copy._fields.Add(new Slot(field.Id, field.Name, AstraValues.CopyValue(field.Value)));
        }

        return copy;
    }

    public readonly record struct Slot(FieldId Id, string Name, AstraValue Value);
}
