namespace AstraGraph.Core;

/// <summary>
/// Schema component operations shared by the VM host. Native components stay on the engine side.
/// </summary>
public interface ISchemaComponentSource
{
    bool Has(AstraEntityId entity, string schemaName);

    SchemaComponentValue TryGet(AstraEntityId entity, string schemaName);

    bool Add(AstraEntityId entity, string schemaName);

    bool Remove(AstraEntityId entity, string schemaName);
}
