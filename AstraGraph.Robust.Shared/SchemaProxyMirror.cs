using System.Globalization;
using AstraGraph.Core;
using AstraGraph.State;
using Robust.Shared.GameObjects;

namespace AstraGraph.Robust.Shared;

/// <summary>
/// Writes primitive schema fields back onto the Robust component shell.
/// The map saver reads that shell, so a changed barrel rate survives a restart.
/// </summary>
public sealed class SchemaProxyMirror : ISchemaComponentSource
{
    private readonly SchemaComponentSource _inner;
    private readonly IEntityManager _entities;

    public SchemaProxyMirror(SchemaComponentSource inner, IEntityManager entities)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
    }

    public bool Has(AstraEntityId entity, string schemaName) => _inner.Has(entity, schemaName);

    public bool Add(AstraEntityId entity, string schemaName) => _inner.Add(entity, schemaName);

    public bool Remove(AstraEntityId entity, string schemaName) => _inner.Remove(entity, schemaName);

    public SchemaComponentValue TryGet(AstraEntityId entity, string schemaName)
    {
        var inner = _inner.TryGet(entity, schemaName);
        if (!inner.Found)
        {
            return inner;
        }

        return new SchemaComponentValue(inner.SchemaName, true, inner.Read, (name, value) =>
        {
            inner.TryWrite(name, value);
            Mirror(entity, inner.SchemaName, name, value);
        });
    }

    public static string? FormatField(AstraValue value)
    {
        return value.Type switch
        {
            AstraValueType.Bool => value.AsBool() ? "true" : "false",
            AstraValueType.Int64 or AstraValueType.EntityUid => value.AsInt64().ToString(CultureInfo.InvariantCulture),
            AstraValueType.Double => value.AsDouble().ToString(CultureInfo.InvariantCulture),
            AstraValueType.PersistentId => value.AsPersistentId().Value.ToString("D"),
            AstraValueType.Object when value.AsObject() is string text => text,
            AstraValueType.Object when value.AsObject() is AstraList list => FormatList(list),
            _ => null
        };
    }

    private static string FormatList(AstraList list)
    {
        var parts = new List<string>(list.Count);
        foreach (var item in list.Items)
        {
            if (item.AsString() is string text)
            {
                parts.Add(text);
            }
        }

        return string.Join(SchemaYamlBinder.ListSeparator, parts);
    }

    private void Mirror(AstraEntityId entity, string schemaName, string fieldName, AstraValue value)
    {
        var text = FormatField(value);
        if (text == null)
        {
            return;
        }

        var uid = new EntityUid((int)entity.Value);
        if (!_entities.EntityExists(uid))
        {
            return;
        }

        foreach (var component in _entities.GetComponents(uid))
        {
            if (component is AstraSchemaComponentProxy proxy && proxy.SchemaName == schemaName)
            {
                proxy.Fields[fieldName] = text;
            }
        }
    }
}
