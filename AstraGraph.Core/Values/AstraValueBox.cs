using System.Collections;
using System.Globalization;
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

    /// <summary>
    /// Writes a primitive onto a public property. Init-only record properties fall back to the backing field,
    /// so a boxed event struct keeps the value when the caller unboxes it.
    /// </summary>
    public static bool WriteMember(object? target, string member, AstraValue value)
    {
        if (target is null || string.IsNullOrEmpty(member) || value.Type == AstraValueType.Null)
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
        var property = type.GetProperty(member, flags);
        if (property == null || property.GetIndexParameters().Length != 0)
        {
            return false;
        }

        if (!TryConvert(value, property.PropertyType, out var converted))
        {
            return false;
        }

        if (property.CanWrite)
        {
            try
            {
                property.SetValue(target, converted);
                return true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
            }
        }

        var backing = type.GetField("<" + member + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        if (backing == null || !TryConvert(value, backing.FieldType, out converted))
        {
            return false;
        }

        backing.SetValue(target, converted);
        return true;
    }

    private static bool TryConvert(AstraValue value, Type target, out object? converted)
    {
        converted = null;
        if (target == typeof(bool) && (value.Type == AstraValueType.Bool || value.Type == AstraValueType.Int64))
        {
            converted = value.Type == AstraValueType.Bool ? value.AsBool() : value.AsInt64() != 0;
            return true;
        }

        if (target == typeof(string))
        {
            converted = value.AsString();
            return true;
        }

        if (target == typeof(int) || target == typeof(long) || target == typeof(short) || target == typeof(byte))
        {
            converted = Convert.ChangeType(value.AsInt64(), target, CultureInfo.InvariantCulture);
            return true;
        }

        if (target == typeof(float) || target == typeof(double))
        {
            converted = Convert.ChangeType(value.AsDouble(), target, CultureInfo.InvariantCulture);
            return true;
        }

        return false;
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
