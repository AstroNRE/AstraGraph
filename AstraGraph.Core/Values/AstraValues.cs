namespace AstraGraph.Core;

/// <summary>
/// VM operations on struct values and lists.
/// A field key is either a name or "{field-id}|{name}". The id is what persistence keeps across a rename.
/// </summary>
public static class AstraValues
{
    public static AstraValue MakeStruct(string schemaKey)
    {
        var (id, name) = SplitKey(schemaKey);
        return AstraValue.FromObject(new AstraStruct(new SchemaId(id), name));
    }

    public static AstraValue MakeList() => AstraValue.FromObject(new AstraList());

    public static AstraValue CopyValue(AstraValue value)
    {
        return value.AsObject() switch
        {
            AstraStruct item => AstraValue.FromObject(item.Copy()),
            AstraList list => AstraValue.FromObject(list.Copy()),
            _ => value
        };
    }

    public static AstraValue GetField(AstraValue target, string fieldKey)
    {
        var (fieldId, name) = SplitKey(fieldKey);
        return target.AsObject() switch
        {
            AstraStruct item => item.Get(new FieldId(fieldId), name),
            SchemaComponentValue component => component.Read(name),
            _ => AstraValue.Null
        };
    }

    public static AstraValue SetField(AstraValue target, string fieldKey, AstraValue value)
    {
        var (fieldId, name) = SplitKey(fieldKey);
        switch (target.AsObject())
        {
            case AstraStruct item:
                item.Set(new FieldId(fieldId), name, value);
                return target;
            case SchemaComponentValue component:
                component.TryWrite(name, value);
                return target;
            default:
                return AstraValue.Null;
        }
    }

    public static AstraValue CollectionAdd(AstraValue collection, AstraValue item)
    {
        if (collection.AsObject() is not AstraList list)
        {
            return AstraValue.Null;
        }

        list.Add(item);
        return collection;
    }

    public static AstraValue CollectionSet(AstraValue collection, int index, AstraValue item)
    {
        if (collection.AsObject() is not AstraList list)
        {
            return AstraValue.Null;
        }

        list.Set(index, item);
        return collection;
    }

    public static bool CollectionContains(AstraValue collection, AstraValue item)
    {
        if (collection.AsObject() is not AstraList list)
        {
            return false;
        }

        foreach (var entry in list.Items)
        {
            if (entry.Equals(item))
            {
                return true;
            }
        }

        return false;
    }

    public static bool CollectionIntersects(AstraValue left, AstraValue right)
    {
        if (left.AsObject() is not AstraList list)
        {
            return false;
        }

        foreach (var entry in list.Items)
        {
            if (CollectionContains(right, entry))
            {
                return true;
            }
        }

        return false;
    }

    public static bool CollectionContainsAll(AstraValue collection, AstraValue required)
    {
        if (required.AsObject() is not AstraList list)
        {
            return false;
        }

        foreach (var entry in list.Items)
        {
            if (!CollectionContains(collection, entry))
            {
                return false;
            }
        }

        return true;
    }

    public static AstraValue CollectionRemove(AstraValue collection, int index)
    {
        if (collection.AsObject() is not AstraList list)
        {
            return AstraValue.Null;
        }

        list.RemoveAt(index);
        return collection;
    }

    public static (Guid Id, string Name) SplitKey(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return (Guid.Empty, string.Empty);
        }

        if (key.Length > 37 && key[36] == '|' && Guid.TryParse(key.AsSpan(0, 36), out var id))
        {
            return (id, key[37..]);
        }

        return (Guid.Empty, key);
    }
}
