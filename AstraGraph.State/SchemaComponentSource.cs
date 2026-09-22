using AstraGraph.Core;

namespace AstraGraph.State;

/// <summary>
/// Reads and writes graph-defined components in the dynamic store.
/// </summary>
public sealed class SchemaComponentSource : ISchemaComponentSource
{
    private readonly DynamicComponentStore _store;
    private readonly AstraSchemaRegistry _registry;

    public SchemaComponentSource(DynamicComponentStore store, AstraSchemaRegistry registry)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public bool Has(AstraEntityId entity, string schemaName)
    {
        if (!_registry.TryGetSchema(schemaName, out var schema) || schema is null)
        {
            return false;
        }

        return _store.HasComponent(entity, schema.Id);
    }

    public SchemaComponentValue TryGet(AstraEntityId entity, string schemaName)
    {
        if (!_registry.TryGetSchema(schemaName, out var schema) || schema is null)
        {
            return SchemaComponentValue.Missing(schemaName);
        }

        if (!_store.TryGetComponent(entity, schema.Id, out var storage) || storage is null)
        {
            return SchemaComponentValue.Missing(schemaName);
        }

        return new SchemaComponentValue(schema.Name, true, fieldName =>
        {
            var field = schema.FindField(fieldName);
            return field == null ? AstraValue.Null : storage.GetField(field.Id);
        });
    }

    public bool Add(AstraEntityId entity, string schemaName)
    {
        if (!_registry.TryGetSchema(schemaName, out var schema) || schema is null)
        {
            return false;
        }

        if (_store.HasComponent(entity, schema.Id))
        {
            return true;
        }

        _store.AddComponent(entity, schema, _registry.CreateDefaultInstance(schema));
        return true;
    }

    public bool Remove(AstraEntityId entity, string schemaName)
    {
        if (!_registry.TryGetSchema(schemaName, out var schema) || schema is null)
        {
            return false;
        }

        return _store.RemoveComponent(entity, schema.Id);
    }

    public bool ApplyInitial(AstraEntityId entity, SchemaType schema, IReadOnlyDictionary<string, string> rawFields, string? prototypeName, out Diagnostic? error)
    {
        var mapped = rawFields.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal);
        var bound = SchemaYamlBinder.Bind(schema, mapped, prototypeName);
        if (!bound.Success)
        {
            error = bound.Diagnostics[0];
            return false;
        }

        if (_store.HasComponent(entity, schema.Id))
        {
            _store.RemoveComponent(entity, schema.Id);
        }

        _store.AddComponent(entity, schema, bound.Values);
        error = null;
        return true;
    }
}
