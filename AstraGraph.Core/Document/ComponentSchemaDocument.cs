namespace AstraGraph.Core;

/// <summary>
/// Authoring form of a graph-defined component or struct. Names are display data.
/// SchemaId and FieldId stay stable across renames.
/// </summary>
public sealed class ComponentSchemaDocument
{
    public SchemaId Id { get; init; } = SchemaId.New();

    public string Name { get; init; } = string.Empty;

    public string Kind { get; init; } = "Component";

    public List<ComponentFieldDocument> Fields { get; init; } = [];

    public bool IsComponent => Kind.Equals("Component", StringComparison.OrdinalIgnoreCase);
}

public sealed class ComponentFieldDocument
{
    public FieldId Id { get; init; } = FieldId.New();

    public string Name { get; init; } = string.Empty;

    public string TypeName { get; init; } = "int32";

    public string? DefaultValue { get; init; }
}

public static class SchemaDocuments
{
    public static SchemaType ToSchema(ComponentSchemaDocument document, TypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(registry);

        var fields = new List<SchemaField>(document.Fields.Count);
        foreach (var field in document.Fields)
        {
            if (!registry.TryGetType(field.TypeName, out var type) || type is null)
            {
                type = PrimitiveType.Int32;
            }

            fields.Add(new SchemaField(field.Id, field.Name, type, field.DefaultValue));
        }

        return new SchemaType(document.Id, document.Name, document.IsComponent, fields);
    }

    public static SchemaType? FirstComponent(GraphDocument document, TypeRegistry registry)
    {
        var declared = document.Schemas.FirstOrDefault(schema => schema.IsComponent && !string.IsNullOrWhiteSpace(schema.Name));
        return declared == null ? null : ToSchema(declared, registry);
    }
}
