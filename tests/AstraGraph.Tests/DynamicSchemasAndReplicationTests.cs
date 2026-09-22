using AstraGraph.Core;
using AstraGraph.HotReload;
using AstraGraph.Runtime.Network;
using AstraGraph.State;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class DynamicSchemasAndReplicationTests
{
    [Test]
    public void DeltaReplication_ReplicatedFlag_FiltersUnreplicatedFields()
    {
        var serverStore = new DynamicComponentStore();
        var clientStore = new DynamicComponentStore();

        var schemaId = SchemaId.New();
        var fReplicated = FieldId.New();
        var fPersistentOnly = FieldId.New();
        var fLocalOnly = FieldId.New();

        var fields = new[]
        {
            new SchemaField(fReplicated, "NetHealth", PrimitiveType.Float64, Options: SchemaFieldOptions.Replicated),
            new SchemaField(fPersistentOnly, "DbSecret", PrimitiveType.Int64, Options: SchemaFieldOptions.Persistent),
            new SchemaField(fLocalOnly, "Scratchpad", PrimitiveType.Int64, Options: SchemaFieldOptions.None)
        };
        var schema = new SchemaType(schemaId, "ServerEntityState", true, fields);

        const int entity1 = 101;
        var initial = new[]
        {
            AstraValue.FromDouble(100.0),
            AstraValue.FromInt64(999),
            AstraValue.FromInt64(0)
        };

        var serverComp = serverStore.AddComponent(entity1, schema, initial);
        clientStore.AddComponent(entity1, schema, initial);
        serverComp.ClearDirty();

        // Mutate all 3 fields on server
        serverComp.SetField(0, AstraValue.FromDouble(85.0)); // Replicated
        serverComp.SetField(1, AstraValue.FromInt64(1234));  // Persistent only
        serverComp.SetField(2, AstraValue.FromInt64(42));    // Local only

        Assert.That(serverComp.HasAnyDirty, Is.True);

        // Collect replicated deltas only
        var replicatedDeltas = DeltaReplicationManager.CollectReplicatedDeltas(serverStore);

        Assert.That(replicatedDeltas.Count, Is.EqualTo(1));
        var packet = replicatedDeltas[0];
        Assert.That(packet.EntityUid, Is.EqualTo(entity1));
        Assert.That(packet.SchemaId, Is.EqualTo(schemaId));

        // Only NetHealth (slot 0) should be included!
        Assert.That(packet.DirtyFields.Count, Is.EqualTo(1));
        Assert.That(packet.DirtyFields[0].FieldId, Is.EqualTo(fReplicated));
        Assert.That(packet.DirtyFields[0].SlotIndex, Is.EqualTo(0));
        Assert.That(packet.DirtyFields[0].Value.AsDouble(), Is.EqualTo(85.0));

        // Apply deltas to client
        DeltaReplicationManager.ApplyDeltas(clientStore, replicatedDeltas);

        var clientComp = clientStore.GetComponent(entity1, schemaId);
        Assert.That(clientComp.GetField(0).AsDouble(), Is.EqualTo(85.0));
        Assert.That(clientComp.GetField(1).AsInt64(), Is.EqualTo(999)); // Unchanged
        Assert.That(clientComp.GetField(2).AsInt64(), Is.EqualTo(0));   // Unchanged
    }

    [Test]
    public void DeltaReplication_BinarySerialization_RoundTrip()
    {
        var schemaId = SchemaId.New();
        var f1 = FieldId.New();
        var f2 = FieldId.New();

        var originalPackets = new List<ComponentDeltaPacket>
        {
            new(
                EntityUid: 10,
                SchemaId: schemaId,
                DirtyFields:
                [
                    new(f1, 0, AstraValue.FromDouble(3.14159)),
                    new(f2, 1, AstraValue.FromString("ActiveState"))
                ]),
            new(
                EntityUid: 20,
                SchemaId: schemaId,
                DirtyFields:
                [
                    new(f1, 0, AstraValue.FromDouble(2.71828))
                ])
        };

        // Serialize to binary
        var bytes = DeltaReplicationManager.SerializeDeltas(originalPackets);
        Assert.That(bytes.Length, Is.GreaterThan(0));

        // Deserialize back
        var deserialized = DeltaReplicationManager.DeserializeDeltas(bytes);
        Assert.That(deserialized.Count, Is.EqualTo(2));

        Assert.That(deserialized[0].EntityUid, Is.EqualTo(10));
        Assert.That(deserialized[0].SchemaId, Is.EqualTo(schemaId));
        Assert.That(deserialized[0].DirtyFields.Count, Is.EqualTo(2));
        Assert.That(deserialized[0].DirtyFields[0].Value.AsDouble(), Is.EqualTo(3.14159).Within(0.00001));
        Assert.That(deserialized[0].DirtyFields[1].Value.AsString(), Is.EqualTo("ActiveState"));

        Assert.That(deserialized[1].EntityUid, Is.EqualTo(20));
        Assert.That(deserialized[1].SchemaId, Is.EqualTo(schemaId));
        Assert.That(deserialized[1].DirtyFields.Count, Is.EqualTo(1));
        Assert.That(deserialized[1].DirtyFields[0].Value.AsDouble(), Is.EqualTo(2.71828).Within(0.00001));
    }

    [Test]
    public void ComponentState_BinarySerialization_RoundTrip()
    {
        var schemaId = SchemaId.New();
        var fInt = FieldId.New();
        var fStr = FieldId.New();
        var fBool = FieldId.New();

        var schema = new SchemaType(schemaId, "TestComp", true,
        [
            new(fInt, "Count", PrimitiveType.Int64),
            new(fStr, "Tag", PrimitiveType.String),
            new(fBool, "Enabled", PrimitiveType.Bool)
        ]);

        var storage = new PackedFieldStorage(schema,
        [
            AstraValue.FromInt64(42),
            AstraValue.FromString("Omega"),
            AstraValue.FromBool(true)
        ]);

        var bytes = DeltaReplicationManager.SerializeComponent(storage);
        Assert.That(bytes.Length, Is.GreaterThan(0));

        var restored = DeltaReplicationManager.DeserializeComponent(bytes, schema);
        Assert.That(restored.FieldCount, Is.EqualTo(3));
        Assert.That(restored.GetField(0).AsInt64(), Is.EqualTo(42));
        Assert.That(restored.GetField(1).AsString(), Is.EqualTo("Omega"));
        Assert.That(restored.GetField(2).AsBool(), Is.True);
    }

    [Test]
    public void DynamicComponentStore_InPlaceSchemaMigration_MigratesAllEntities()
    {
        var store = new DynamicComponentStore();

        var schemaId = SchemaId.New();
        var fId1 = FieldId.New();
        var fId2 = FieldId.New();
        var fId3 = FieldId.New(); // To be dropped in v2
        var fIdNew = FieldId.New(); // To be added in v2

        // Schema V1
        var schemaV1 = new SchemaType(schemaId, "AirlockData", true,
        [
            new(fId1, "IsOpen", PrimitiveType.Bool),
            new(fId2, "PowerUsage", PrimitiveType.Float32),
            new(fId3, "ObsoleteField", PrimitiveType.String)
        ]);

        // Add 50 entities
        for (var i = 1; i <= 50; i++)
        {
            store.AddComponent(i, schemaV1,
            [
                AstraValue.FromBool(i % 2 == 0),
                AstraValue.FromFloat((float)(100 + i)),
                AstraValue.FromString($"Legacy_{i}")
            ]);
        }

        // Schema V2: fId1 preserved, fId2 widened to Float64, fId3 dropped, fIdNew added with default "7.5"
        var schemaV2 = new SchemaType(schemaId, "AirlockData", true,
        [
            new(fId1, "IsOpen", PrimitiveType.Bool),
            new(fId2, "PowerUsage", PrimitiveType.Float64), // Widened
            new(fIdNew, "AutoCloseSeconds", PrimitiveType.Float64, "7.5") // New
        ]);

        var plan = StateMigrationPlanner.CreatePlan(schemaV1, schemaV2);
        Assert.That(plan.CanAutoMigrate, Is.True);

        // Execute in-place migration on store
        store.MigrateSchema(schemaId, schemaV2, plan.Execute);

        // Verify entities migrated correctly
        var entities = store.GetEntitiesWithComponent(schemaId);
        Assert.That(entities.Count, Is.EqualTo(50));

        for (var i = 1; i <= 50; i++)
        {
            var comp = store.GetComponent(i, schemaId);
            Assert.That(comp.Schema, Is.SameAs(schemaV2));
            Assert.That(comp.FieldCount, Is.EqualTo(3));

            // Field 0: IsOpen preserved
            Assert.That(comp.GetField(0).AsBool(), Is.EqualTo(i % 2 == 0));

            // Field 1: PowerUsage widened to double
            Assert.That(comp.GetField(1).AsDouble(), Is.EqualTo(100.0 + i).Within(0.001));

            // Field 2: AutoCloseSeconds populated with default value 7.5
            Assert.That(comp.GetField(2).AsDouble(), Is.EqualTo(7.5));
        }
    }
}
