using System.Collections.Concurrent;

namespace AstraGraph.Core;

/// <summary>
/// Central registry of known AstraGraph types, schemas, enums, and native bindings.
/// </summary>
public sealed class TypeRegistry
{
    private readonly ConcurrentDictionary<string, AstraType> _typesByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Type, NativeClrType> _nativeTypesByClrType = new();
    private readonly ConcurrentDictionary<SchemaId, SchemaType> _schemasById = new();
    private readonly ConcurrentDictionary<SymbolId, EnumType> _enumsById = new();

    public static TypeRegistry Default { get; } = CreateDefault();

    public static TypeRegistry CreateDefault()
    {
        var registry = new TypeRegistry();

        // Primitives
        registry.RegisterType(PrimitiveType.Void);
        registry.RegisterType(PrimitiveType.Bool);
        registry.RegisterType(PrimitiveType.Int8);
        registry.RegisterType(PrimitiveType.Int16);
        registry.RegisterType(PrimitiveType.Int32);
        registry.RegisterType(PrimitiveType.Int64);
        registry.RegisterType(PrimitiveType.UInt8);
        registry.RegisterType(PrimitiveType.UInt16);
        registry.RegisterType(PrimitiveType.UInt32);
        registry.RegisterType(PrimitiveType.UInt64);
        registry.RegisterType(PrimitiveType.Float32);
        registry.RegisterType(PrimitiveType.Float64);
        registry.RegisterType(PrimitiveType.String);
        registry.RegisterType(PrimitiveType.TimeSpan);

        // CLR Aliases
        registry.RegisterAlias("System.Void", PrimitiveType.Void);
        registry.RegisterAlias("System.Boolean", PrimitiveType.Bool);
        registry.RegisterAlias("System.SByte", PrimitiveType.Int8);
        registry.RegisterAlias("System.Int16", PrimitiveType.Int16);
        registry.RegisterAlias("System.Int32", PrimitiveType.Int32);
        registry.RegisterAlias("System.Int64", PrimitiveType.Int64);
        registry.RegisterAlias("System.Byte", PrimitiveType.UInt8);
        registry.RegisterAlias("System.UInt16", PrimitiveType.UInt16);
        registry.RegisterAlias("System.UInt32", PrimitiveType.UInt32);
        registry.RegisterAlias("System.UInt64", PrimitiveType.UInt64);
        registry.RegisterAlias("System.Single", PrimitiveType.Float32);
        registry.RegisterAlias("System.Double", PrimitiveType.Float64);
        registry.RegisterAlias("System.String", PrimitiveType.String);
        registry.RegisterAlias("System.TimeSpan", PrimitiveType.TimeSpan);

        // Common C# keyword aliases
        registry.RegisterAlias("int", PrimitiveType.Int32);
        registry.RegisterAlias("float", PrimitiveType.Float32);
        registry.RegisterAlias("double", PrimitiveType.Float64);
        registry.RegisterAlias("long", PrimitiveType.Int64);
        registry.RegisterAlias("short", PrimitiveType.Int16);
        registry.RegisterAlias("byte", PrimitiveType.UInt8);

        // Entities
        registry.RegisterType(EntityType.EntityUid);
        registry.RegisterType(EntityType.NetEntity);
        registry.RegisterAlias("Robust.Shared.GameObjects.EntityUid", EntityType.EntityUid);
        registry.RegisterAlias("Robust.Shared.GameObjects.NetEntity", EntityType.NetEntity);

        // Geometry
        registry.RegisterType(GeometryType.Vector2);
        registry.RegisterType(GeometryType.Angle);
        registry.RegisterType(GeometryType.EntityCoordinates);
        registry.RegisterType(GeometryType.MapCoordinates);
        registry.RegisterAlias("System.Numerics.Vector2", GeometryType.Vector2);
        registry.RegisterAlias("Robust.Shared.Maths.Vector2", GeometryType.Vector2);
        registry.RegisterAlias("Robust.Shared.Maths.Angle", GeometryType.Angle);
        registry.RegisterAlias("Robust.Shared.Map.EntityCoordinates", GeometryType.EntityCoordinates);
        registry.RegisterAlias("Robust.Shared.Map.MapCoordinates", GeometryType.MapCoordinates);

        registry.RegisterType(ProtoIdType.EntProtoId);
        registry.RegisterType(ProtoIdType.ProtoId);
        registry.RegisterAlias("EntProtoId<EntityPrototype>", ProtoIdType.EntProtoId);
        registry.RegisterAlias("ProtoId<EntityPrototype>", ProtoIdType.ProtoId);

        return registry;
    }

    public void RegisterType(AstraType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        _typesByName[type.TypeName] = type;
    }

    public void RegisterAlias(string alias, AstraType type)
    {
        ArgumentNullException.ThrowIfNull(alias);
        ArgumentNullException.ThrowIfNull(type);
        _typesByName[alias] = type;
    }

    public void RegisterSchema(SchemaType schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        _schemasById[schema.Id] = schema;
        _typesByName[schema.Name] = schema;
    }

    public void RegisterEnum(EnumType enumType)
    {
        ArgumentNullException.ThrowIfNull(enumType);
        _enumsById[enumType.Id] = enumType;
        _typesByName[enumType.Name] = enumType;
    }

    public NativeClrType GetOrCreateNativeType(Type clrType)
    {
        ArgumentNullException.ThrowIfNull(clrType);
        return _nativeTypesByClrType.GetOrAdd(clrType, t =>
        {
            var native = new NativeClrType(t);
            RegisterType(native);
            return native;
        });
    }

    public bool TryGetType(string name, out AstraType? type)
    {
        if (_typesByName.TryGetValue(name, out type))
        {
            return true;
        }

        // Handle Nullable syntax T?
        if (name.EndsWith('?'))
        {
            var underlyingName = name[..^1];
            if (TryGetType(underlyingName, out var underlying) && underlying is not null)
            {
                type = new NullableType(underlying);
                _typesByName[name] = type;
                return true;
            }
        }

        // Handle List<T> syntax
        if (name.StartsWith("List<", StringComparison.Ordinal) && name.EndsWith('>'))
        {
            var elementTypeName = name[5..^1];
            if (TryGetType(elementTypeName, out var elemType) && elemType is not null)
            {
                type = new CollectionType(CollectionKind.List, elemType);
                _typesByName[name] = type;
                return true;
            }
        }

        type = null;
        return false;
    }

    public AstraType GetType(string name)
    {
        if (TryGetType(name, out var type) && type is not null)
        {
            return type;
        }
        throw new KeyNotFoundException($"Type '{name}' is not registered in AstraGraph TypeRegistry.");
    }

    public SchemaType? FindSchema(SchemaId schemaId) => _schemasById.GetValueOrDefault(schemaId);

    public EnumType? FindEnum(SymbolId enumId) => _enumsById.GetValueOrDefault(enumId);
}
