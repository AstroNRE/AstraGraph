using AstraGraph.Core;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class TypeSystemTests
{
    private TypeRegistry _registry = null!;

    [SetUp]
    public void SetUp()
    {
        _registry = TypeRegistry.CreateDefault();
    }

    [Test]
    public void Primitives_BasicPropertiesAndWidening()
    {
        Assert.That(PrimitiveType.Int32.IsNumeric, Is.True);
        Assert.That(PrimitiveType.Int32.IsInteger, Is.True);
        Assert.That(PrimitiveType.Float32.IsFloatingPoint, Is.True);
        Assert.That(PrimitiveType.String.IsValueType, Is.False);
        Assert.That(PrimitiveType.Int32.IsValueType, Is.True);

        // Valid widening: int32 -> int64, float32 -> float64, int32 -> float64
        Assert.That(PrimitiveType.Int64.IsAssignableFrom(PrimitiveType.Int32), Is.True);
        Assert.That(PrimitiveType.Float64.IsAssignableFrom(PrimitiveType.Float32), Is.True);
        Assert.That(PrimitiveType.Float64.IsAssignableFrom(PrimitiveType.Int32), Is.True);

        // Invalid narrowing
        Assert.That(PrimitiveType.Int16.IsAssignableFrom(PrimitiveType.Int32), Is.False);
        Assert.That(PrimitiveType.Float32.IsAssignableFrom(PrimitiveType.Float64), Is.False);
        Assert.That(PrimitiveType.Bool.IsAssignableFrom(PrimitiveType.Int32), Is.False);
    }

    [Test]
    public void Nullability_RulesEnforcement()
    {
        var intType = PrimitiveType.Int32;
        var nullableInt = new NullableType(intType);

        Assert.That(nullableInt.IsNullable, Is.True);
        Assert.That(intType.IsNullable, Is.False);

        // Implicit wrap: T -> T? is allowed
        Assert.That(nullableInt.IsAssignableFrom(intType), Is.True);

        // Implicit unwrap: T? -> T is FORBIDDEN without unwrap
        Assert.That(intType.IsAssignableFrom(nullableInt), Is.False);

        // Nullable EntityUid
        var entityType = EntityType.EntityUid;
        var nullableEntity = new NullableType(entityType);
        Assert.That(nullableEntity.IsAssignableFrom(entityType), Is.True);
        Assert.That(entityType.IsAssignableFrom(nullableEntity), Is.False);
    }

    [Test]
    public void SchemaType_FieldsAndMigrationIds()
    {
        var schemaId = SchemaId.New();
        var field1Id = FieldId.New();
        var field2Id = FieldId.New();

        var heatField = new SchemaField(field1Id, "Heat", PrimitiveType.Float32, "0.0", SchemaFieldOptions.Replicated);
        var coolingField = new SchemaField(field2Id, "Cooling", PrimitiveType.Float32, "5.0", SchemaFieldOptions.Persistent);

        var cyberwareHeat = new SchemaType(schemaId, "CyberwareHeat", IsComponentSchema: true, [heatField, coolingField]);
        _registry.RegisterSchema(cyberwareHeat);

        Assert.That(cyberwareHeat.FieldCount, Is.EqualTo(2));
        Assert.That(cyberwareHeat.FindField(field1Id), Is.EqualTo(heatField));
        Assert.That(cyberwareHeat.FindField("Cooling"), Is.EqualTo(coolingField));
        Assert.That(heatField.IsReplicated, Is.True);
        Assert.That(coolingField.IsPersistent, Is.True);

        Assert.That(_registry.FindSchema(schemaId), Is.EqualTo(cyberwareHeat));
        Assert.That(_registry.GetType("CyberwareHeat"), Is.EqualTo(cyberwareHeat));
    }

    [Test]
    public void EnumType_MembersAndLookups()
    {
        var enumId = SymbolId.New();
        var members = new List<EnumMember>
        {
            new("Safe", 0),
            new("Semi", 1),
            new("Burst", 2),
            new("Auto", 3)
        };

        var fireMode = new EnumType(enumId, "WeaponFireMode", members);
        _registry.RegisterEnum(fireMode);

        Assert.That(fireMode.TryGetValue("Burst", out var val), Is.True);
        Assert.That(val, Is.EqualTo(2));
        Assert.That(fireMode.TryGetName(3, out var name), Is.True);
        Assert.That(name, Is.EqualTo("Auto"));

        Assert.That(_registry.FindEnum(enumId), Is.EqualTo(fireMode));
        Assert.That(_registry.GetType("WeaponFireMode"), Is.EqualTo(fireMode));
    }

    [Test]
    public void TypeRegistry_DynamicSyntaxResolution()
    {
        // Nullable resolution via ?
        var resolvedNullable = _registry.GetType("int32?");
        Assert.That(resolvedNullable, Is.TypeOf<NullableType>());
        Assert.That(((NullableType)resolvedNullable).UnderlyingType, Is.EqualTo(PrimitiveType.Int32));

        // List<T> resolution
        var resolvedList = _registry.GetType("List<EntityUid>");
        Assert.That(resolvedList, Is.TypeOf<CollectionType>());
        var col = (CollectionType)resolvedList;
        Assert.That(col.Kind, Is.EqualTo(CollectionKind.List));
        Assert.That(col.ElementType, Is.EqualTo(EntityType.EntityUid));

        // Alias resolution
        Assert.That(_registry.GetType("System.Single"), Is.EqualTo(PrimitiveType.Float32));
        Assert.That(_registry.GetType("Robust.Shared.GameObjects.EntityUid"), Is.EqualTo(EntityType.EntityUid));
    }

    [Test]
    public void NativeClrType_PolymorphismAndAssignment()
    {
        var nativeException = _registry.GetOrCreateNativeType(typeof(Exception));
        var nativeArgException = _registry.GetOrCreateNativeType(typeof(ArgumentException));

        Assert.That(nativeException.IsAssignableFrom(nativeArgException), Is.True);
        Assert.That(nativeArgException.IsAssignableFrom(nativeException), Is.False);
    }
}
