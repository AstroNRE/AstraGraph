using System.Reflection;
using System.Reflection.Emit;
using AstraGraph.Core;

namespace AstraGraph.Runtime.Events;

/// <summary>
/// Delegate for reading a field or property from a ref event argument without boxing.
/// </summary>
public delegate AstraValue RefFieldGetter<TEvent>(ref TEvent ev);

/// <summary>
/// Delegate for writing a field or property directly into a ref event argument without boxing.
/// </summary>
public delegate void RefFieldSetter<TEvent>(ref TEvent ev, AstraValue value);

/// <summary>
/// High-performance accessor that compiles strongly-typed, unboxed delegates for reading and mutating
/// native event struct/class fields directly in memory.
/// </summary>
public sealed class TypedRefEventAccessor<TEvent>
{
    public static readonly TypedRefEventAccessor<TEvent> Instance = new();

    private readonly Dictionary<string, RefFieldGetter<TEvent>> _getters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RefFieldSetter<TEvent>> _setters = new(StringComparer.OrdinalIgnoreCase);

    public TypedRefEventAccessor()
    {
        BuildAccessors();
    }

    public AstraValue GetField(ref TEvent ev, string name)
    {
        if (_getters.TryGetValue(name, out var getter))
            return getter(ref ev);

        throw new KeyNotFoundException($"Event '{typeof(TEvent).Name}' has no accessible field or property '{name}'.");
    }

    public void SetField(ref TEvent ev, string name, AstraValue value)
    {
        if (_setters.TryGetValue(name, out var setter))
        {
            setter(ref ev, value);
            return;
        }

        throw new KeyNotFoundException($"Event '{typeof(TEvent).Name}' has no mutable field or property '{name}'.");
    }

    public bool TryGetField(ref TEvent ev, string name, out AstraValue value)
    {
        if (_getters.TryGetValue(name, out var getter))
        {
            value = getter(ref ev);
            return true;
        }

        value = AstraValue.Null;
        return false;
    }

    public bool TrySetField(ref TEvent ev, string name, AstraValue value)
    {
        if (_setters.TryGetValue(name, out var setter))
        {
            setter(ref ev, value);
            return true;
        }

        return false;
    }

    private void BuildAccessors()
    {
        var eventType = typeof(TEvent);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

        // 1. Discover public properties
        foreach (var prop in eventType.GetProperties(flags))
        {
            if (prop.GetMethod != null && prop.GetIndexParameters().Length == 0)
            {
                try
                {
                    _getters[prop.Name] = CreatePropertyGetter(prop);
                }
                catch
                {
                    // Fallback to reflection if dynamic method generation fails
                    var getterMethod = prop.GetMethod;
                    _getters[prop.Name] = (ref TEvent ev) =>
                    {
                        object? target = ev;
                        var result = getterMethod.Invoke(target, null);
                        return AstraValue.FromObject(result);
                    };
                }
            }

            if (prop.SetMethod != null && prop.GetIndexParameters().Length == 0)
            {
                try
                {
                    _setters[prop.Name] = CreatePropertySetter(prop);
                }
                catch
                {
                    var setterMethod = prop.SetMethod;
                    var propType = prop.PropertyType;
                    _setters[prop.Name] = (ref TEvent ev, AstraValue val) =>
                    {
                        object boxed = ev!;
                        var converted = ConvertAstraValue(val, propType);
                        setterMethod.Invoke(boxed, [converted]);
                        ev = (TEvent)boxed;
                    };
                }
            }
        }

        // 2. Discover public fields
        foreach (var field in eventType.GetFields(flags))
        {
            try
            {
                _getters[field.Name] = CreateFieldGetter(field);
            }
            catch
            {
                _getters[field.Name] = (ref TEvent ev) =>
                {
                    object? target = ev;
                    return AstraValue.FromObject(field.GetValue(target));
                };
            }

            if (!field.IsInitOnly)
            {
                try
                {
                    _setters[field.Name] = CreateFieldSetter(field);
                }
                catch
                {
                    var fieldType = field.FieldType;
                    _setters[field.Name] = (ref TEvent ev, AstraValue val) =>
                    {
                        object boxed = ev!;
                        var converted = ConvertAstraValue(val, fieldType);
                        field.SetValue(boxed, converted);
                        ev = (TEvent)boxed;
                    };
                }
            }
        }
    }

    private static RefFieldGetter<TEvent> CreateFieldGetter(FieldInfo field)
    {
        var eventType = typeof(TEvent);
        var dm = new DynamicMethod(
            $"Get_{eventType.Name}_{field.Name}",
            typeof(AstraValue),
            [eventType.MakeByRefType()],
            typeof(TypedRefEventAccessor<TEvent>).Module,
            true);

        var il = dm.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        if (!eventType.IsValueType)
        {
            il.Emit(OpCodes.Ldind_Ref);
        }
        il.Emit(OpCodes.Ldfld, field);
        EmitConvertToAstraValue(il, field.FieldType);
        il.Emit(OpCodes.Ret);

        return dm.CreateDelegate<RefFieldGetter<TEvent>>();
    }

    private static RefFieldSetter<TEvent> CreateFieldSetter(FieldInfo field)
    {
        var eventType = typeof(TEvent);
        var dm = new DynamicMethod(
            $"Set_{eventType.Name}_{field.Name}",
            typeof(void),
            [eventType.MakeByRefType(), typeof(AstraValue)],
            typeof(TypedRefEventAccessor<TEvent>).Module,
            true);

        var il = dm.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        if (!eventType.IsValueType)
        {
            il.Emit(OpCodes.Ldind_Ref);
        }
        il.Emit(OpCodes.Ldarga_S, 1);
        EmitConvertFromAstraValue(il, field.FieldType);
        il.Emit(OpCodes.Stfld, field);
        il.Emit(OpCodes.Ret);

        return dm.CreateDelegate<RefFieldSetter<TEvent>>();
    }

    private static RefFieldGetter<TEvent> CreatePropertyGetter(PropertyInfo prop)
    {
        var eventType = typeof(TEvent);
        var method = prop.GetMethod!;
        var dm = new DynamicMethod(
            $"Get_{eventType.Name}_{prop.Name}",
            typeof(AstraValue),
            [eventType.MakeByRefType()],
            typeof(TypedRefEventAccessor<TEvent>).Module,
            true);

        var il = dm.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        if (!eventType.IsValueType)
        {
            il.Emit(OpCodes.Ldind_Ref);
        }
        il.Emit(method.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, method);
        EmitConvertToAstraValue(il, prop.PropertyType);
        il.Emit(OpCodes.Ret);

        return dm.CreateDelegate<RefFieldGetter<TEvent>>();
    }

    private static RefFieldSetter<TEvent> CreatePropertySetter(PropertyInfo prop)
    {
        var eventType = typeof(TEvent);
        var method = prop.SetMethod!;
        var dm = new DynamicMethod(
            $"Set_{eventType.Name}_{prop.Name}",
            typeof(void),
            [eventType.MakeByRefType(), typeof(AstraValue)],
            typeof(TypedRefEventAccessor<TEvent>).Module,
            true);

        var il = dm.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        if (!eventType.IsValueType)
        {
            il.Emit(OpCodes.Ldind_Ref);
        }
        il.Emit(OpCodes.Ldarga_S, 1);
        EmitConvertFromAstraValue(il, prop.PropertyType);
        il.Emit(method.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, method);
        il.Emit(OpCodes.Ret);

        return dm.CreateDelegate<RefFieldSetter<TEvent>>();
    }

    private static void EmitConvertToAstraValue(ILGenerator il, Type type)
    {
        if (type == typeof(bool))
        {
            il.Emit(OpCodes.Call, typeof(AstraValue).GetMethod(nameof(AstraValue.FromBool), [typeof(bool)])!);
        }
        else if (type == typeof(int))
        {
            il.Emit(OpCodes.Call, typeof(AstraValue).GetMethod(nameof(AstraValue.FromInt32), [typeof(int)])!);
        }
        else if (type == typeof(long))
        {
            il.Emit(OpCodes.Call, typeof(AstraValue).GetMethod(nameof(AstraValue.FromInt64), [typeof(long)])!);
        }
        else if (type == typeof(float))
        {
            il.Emit(OpCodes.Call, typeof(AstraValue).GetMethod(nameof(AstraValue.FromFloat), [typeof(float)])!);
        }
        else if (type == typeof(double))
        {
            il.Emit(OpCodes.Call, typeof(AstraValue).GetMethod(nameof(AstraValue.FromDouble), [typeof(double)])!);
        }
        else if (type == typeof(string))
        {
            il.Emit(OpCodes.Call, typeof(AstraValue).GetMethod(nameof(AstraValue.FromString), [typeof(string)])!);
        }
        else
        {
            if (type.IsValueType)
            {
                il.Emit(OpCodes.Box, type);
            }
            il.Emit(OpCodes.Call, typeof(AstraValue).GetMethod(nameof(AstraValue.FromObject), [typeof(object)])!);
        }
    }

    private static void EmitConvertFromAstraValue(ILGenerator il, Type type)
    {
        if (type == typeof(bool))
        {
            il.Emit(OpCodes.Call, typeof(AstraValue).GetMethod(nameof(AstraValue.AsBool))!);
        }
        else if (type == typeof(int))
        {
            il.Emit(OpCodes.Call, typeof(AstraValue).GetMethod(nameof(AstraValue.AsInt32))!);
        }
        else if (type == typeof(long))
        {
            il.Emit(OpCodes.Call, typeof(AstraValue).GetMethod(nameof(AstraValue.AsInt64))!);
        }
        else if (type == typeof(float))
        {
            il.Emit(OpCodes.Call, typeof(AstraValue).GetMethod(nameof(AstraValue.AsDouble))!);
            il.Emit(OpCodes.Conv_R4);
        }
        else if (type == typeof(double))
        {
            il.Emit(OpCodes.Call, typeof(AstraValue).GetMethod(nameof(AstraValue.AsDouble))!);
        }
        else if (type == typeof(string))
        {
            il.Emit(OpCodes.Call, typeof(AstraValue).GetMethod(nameof(AstraValue.AsString))!);
        }
        else
        {
            il.Emit(OpCodes.Call, typeof(AstraValue).GetMethod(nameof(AstraValue.AsObject))!);
            if (type.IsValueType)
            {
                il.Emit(OpCodes.Unbox_Any, type);
            }
            else if (type != typeof(object))
            {
                il.Emit(OpCodes.Castclass, type);
            }
        }
    }

    private static object? ConvertAstraValue(AstraValue val, Type targetType)
    {
        if (targetType == typeof(bool)) return val.AsBool();
        if (targetType == typeof(int)) return val.AsInt32();
        if (targetType == typeof(long)) return val.AsInt64();
        if (targetType == typeof(float)) return (float)val.AsDouble();
        if (targetType == typeof(double)) return val.AsDouble();
        if (targetType == typeof(string)) return val.AsString();
        return val.AsObject();
    }
}
