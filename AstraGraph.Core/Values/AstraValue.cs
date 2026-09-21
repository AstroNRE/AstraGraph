using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace AstraGraph.Core;

public enum AstraValueType : byte
{
    Null = 0,
    Bool = 1,
    Int64 = 2,
    Double = 3,
    EntityUid = 4,
    Vector2 = 5,
    Object = 6
}

/// <summary>
/// Compact, high-performance tagged union representing an unboxed runtime value in AstraGraph.
/// Eliminates heap allocation for primitives, EntityUid, and Vector2.
/// </summary>
[StructLayout(LayoutKind.Explicit)]
public readonly struct AstraValue : IEquatable<AstraValue>
{
    [FieldOffset(0)]
    public readonly AstraValueType Type;

    // Fast 64-bit integer / boolean / raw bits payload
    [FieldOffset(8)]
    private readonly long _intPayload;

    // Fast 64-bit double precision float payload
    [FieldOffset(8)]
    private readonly double _doublePayload;

    // Fast 2D Vector payload (two 32-bit floats = 64-bit)
    [FieldOffset(8)]
    private readonly float _vecX;

    [FieldOffset(12)]
    private readonly float _vecY;

    // Reference payload for strings, collections, schemas, and CLR objects
    [FieldOffset(16)]
    private readonly object? _objectPayload;

    public static readonly AstraValue Null = new(AstraValueType.Null, 0, null);
    public static readonly AstraValue True = new(AstraValueType.Bool, 1, null);
    public static readonly AstraValue False = new(AstraValueType.Bool, 0, null);

    private AstraValue(AstraValueType type, long intPayload, object? objectPayload)
    {
        Type = type;
        _doublePayload = 0;
        _vecX = 0;
        _vecY = 0;
        _intPayload = intPayload;
        _objectPayload = objectPayload;
    }

    private AstraValue(double doublePayload)
    {
        Type = AstraValueType.Double;
        _intPayload = 0;
        _vecX = 0;
        _vecY = 0;
        _doublePayload = doublePayload;
        _objectPayload = null;
    }

    private AstraValue(float x, float y)
    {
        Type = AstraValueType.Vector2;
        _intPayload = 0;
        _doublePayload = 0;
        _vecX = x;
        _vecY = y;
        _objectPayload = null;
    }

    public static AstraValue FromBool(bool value) => value ? True : False;

    public static AstraValue FromInt64(long value) => new(AstraValueType.Int64, value, null);

    public static AstraValue FromInt32(int value) => new(AstraValueType.Int64, value, null);

    public static AstraValue FromDouble(double value) => new(value);

    public static AstraValue FromFloat(float value) => new(value);

    public static AstraValue FromEntityUid(int entityId) => new(AstraValueType.EntityUid, entityId, null);

    public static AstraValue FromVector2(Vector2 vector) => new(vector.X, vector.Y);

    public static AstraValue FromVector2(float x, float y) => new(x, y);

    public static AstraValue FromString(string? value) =>
        value is null ? Null : new(AstraValueType.Object, 0, value);

    public static AstraValue FromObject(object? value)
    {
        if (value is null) return Null;
        return value switch
        {
            bool b => FromBool(b),
            byte u8 => FromInt64(u8),
            sbyte i8 => FromInt64(i8),
            short i16 => FromInt64(i16),
            ushort u16 => FromInt64(u16),
            int i32 => FromInt64(i32),
            uint u32 => FromInt64(u32),
            long i64 => FromInt64(i64),
            float f32 => FromDouble(f32),
            double f64 => FromDouble(f64),
            string s => FromString(s),
            Vector2 v2 => FromVector2(v2),
            _ => new(AstraValueType.Object, 0, value)
        };
    }

    public bool AsBool()
    {
        if (Type != AstraValueType.Bool) ThrowTypeMismatch(AstraValueType.Bool);
        return _intPayload != 0;
    }

    public long AsInt64()
    {
        if (Type == AstraValueType.Int64 || Type == AstraValueType.EntityUid) return _intPayload;
        if (Type == AstraValueType.Double) return (long)_doublePayload;
        ThrowTypeMismatch(AstraValueType.Int64);
        return 0;
    }

    public int AsInt32() => (int)AsInt64();

    public double AsDouble()
    {
        if (Type == AstraValueType.Double) return _doublePayload;
        if (Type == AstraValueType.Int64) return _intPayload;
        ThrowTypeMismatch(AstraValueType.Double);
        return 0.0;
    }

    public float AsFloat() => (float)AsDouble();

    public int AsEntityUid()
    {
        if (Type != AstraValueType.EntityUid && Type != AstraValueType.Int64) ThrowTypeMismatch(AstraValueType.EntityUid);
        return (int)_intPayload;
    }

    public Vector2 AsVector2()
    {
        if (Type != AstraValueType.Vector2) ThrowTypeMismatch(AstraValueType.Vector2);
        return new Vector2(_vecX, _vecY);
    }

    public string? AsString()
    {
        if (Type == AstraValueType.Null) return null;
        if (Type == AstraValueType.Object && _objectPayload is string str) return str;
        return ToString();
    }

    public object? AsObject() => Type switch
    {
        AstraValueType.Null => null,
        AstraValueType.Bool => AsBool(),
        AstraValueType.Int64 => AsInt64(),
        AstraValueType.Double => AsDouble(),
        AstraValueType.EntityUid => AsEntityUid(),
        AstraValueType.Vector2 => AsVector2(),
        AstraValueType.Object => _objectPayload,
        _ => null
    };

    public bool Equals(AstraValue other)
    {
        if (Type != other.Type) return false;
        return Type switch
        {
            AstraValueType.Null => true,
            AstraValueType.Bool or AstraValueType.Int64 or AstraValueType.EntityUid => _intPayload == other._intPayload,
            AstraValueType.Double => _doublePayload.Equals(other._doublePayload),
            AstraValueType.Vector2 => _vecX.Equals(other._vecX) && _vecY.Equals(other._vecY),
            AstraValueType.Object => Equals(_objectPayload, other._objectPayload),
            _ => false
        };
    }

    public override bool Equals(object? obj) => obj is AstraValue val && Equals(val);

    public override int GetHashCode() => Type switch
    {
        AstraValueType.Null => 0,
        AstraValueType.Bool or AstraValueType.Int64 or AstraValueType.EntityUid => _intPayload.GetHashCode(),
        AstraValueType.Double => _doublePayload.GetHashCode(),
        AstraValueType.Vector2 => HashCode.Combine(_vecX, _vecY),
        AstraValueType.Object => _objectPayload?.GetHashCode() ?? 0,
        _ => 0
    };

    public static bool operator ==(AstraValue left, AstraValue right) => left.Equals(right);
    public static bool operator !=(AstraValue left, AstraValue right) => !left.Equals(right);

    public override string ToString() => Type switch
    {
        AstraValueType.Null => "null",
        AstraValueType.Bool => AsBool() ? "true" : "false",
        AstraValueType.Int64 => _intPayload.ToString(CultureInfo.InvariantCulture),
        AstraValueType.Double => _doublePayload.ToString(CultureInfo.InvariantCulture),
        AstraValueType.EntityUid => $"Entity({_intPayload})",
        AstraValueType.Vector2 => $"Vector2({_vecX}, {_vecY})",
        AstraValueType.Object => _objectPayload?.ToString() ?? "null",
        _ => "unknown"
    };

    private void ThrowTypeMismatch(AstraValueType expected) =>
        throw new InvalidOperationException($"Cannot cast AstraValue of type '{Type}' to expected type '{expected}'.");
}
