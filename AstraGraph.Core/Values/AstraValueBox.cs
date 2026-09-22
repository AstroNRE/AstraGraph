using System.Collections;
using System.Reflection;

namespace AstraGraph.Core;

/// <summary>
/// Converts CLR values that cross the native boundary into Astra values.
/// Nullable wrappers and entity identifiers are unwrapped here.
/// </summary>
public static class AstraValueBox
{
    public static AstraValue Box(object? value)
    {
        if (value is null)
        {
            return AstraValue.Null;
        }

        var type = value.GetType();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            var hasValue = (bool)type.GetProperty(nameof(Nullable<int>.HasValue))!.GetValue(value)!;
            if (!hasValue)
            {
                return AstraValue.Null;
            }

            value = type.GetProperty(nameof(Nullable<int>.Value))!.GetValue(value);
            return Box(value);
        }

        if (type.Name == "EntityUid")
        {
            var id = type.GetField("Id")?.GetValue(value) ?? type.GetProperty("Id")?.GetValue(value);
            if (id is int entity)
            {
                return AstraValue.FromEntityUid(entity);
            }
        }

        if (value is IList list && value is not string)
        {
            var items = new AstraValue[list.Count];
            for (var i = 0; i < list.Count; i++)
            {
                items[i] = Box(list[i]);
            }

            return AstraValue.FromObject(new AstraList(items));
        }

        return AstraValue.FromObject(value);
    }

    public static AstraValue ReadMember(object? target, string member)
    {
        if (target is null || string.IsNullOrEmpty(member))
        {
            return AstraValue.Null;
        }

        if (target is AstraValue boxed)
        {
            target = boxed.AsObject();
        }

        if (target is null)
        {
            return AstraValue.Null;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        var type = target.GetType();
        var property = type.GetProperty(member, flags);
        if (property != null)
        {
            return Box(property.GetValue(target));
        }

        var field = type.GetField(member, flags);
        if (field != null)
        {
            return Box(field.GetValue(target));
        }

        return AstraValue.Null;
    }

    public static bool HasMember(object? target, string member)
    {
        if (target is null || string.IsNullOrEmpty(member))
        {
            return false;
        }

        if (target is AstraValue boxed)
        {
            target = boxed.AsObject();
        }

        if (target is null)
        {
            return false;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        var type = target.GetType();
        return type.GetProperty(member, flags) != null || type.GetField(member, flags) != null;
    }
}
