using AstraGraph.Core;
using AstraGraph.State;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class StateAndDynamicComponentTests
{
    [Test]
    public void SparseSet_AddRemoveAndContainsBehaviors()
    {
        var set = new SparseSet();

        Assert.That(set.Contains(42), Is.False);
        Assert.That(set.Count, Is.EqualTo(0));

        // Add discontinuous entities
        set.Add(10);
        set.Add(100500);
        set.Add(3);

        Assert.That(set.Count, Is.EqualTo(3));
        Assert.That(set.Contains(10), Is.True);
        Assert.That(set.Contains(100500), Is.True);
        Assert.That(set.Contains(3), Is.True);
        Assert.That(set.Contains(99), Is.False);

        // Remove middle entity
        var removed = set.Remove(10);
        Assert.That(removed, Is.True);
        Assert.That(set.Count, Is.EqualTo(2));
        Assert.That(set.Contains(10), Is.False);
        Assert.That(set.Contains(3), Is.True);
        Assert.That(set.Contains(100500), Is.True);
    }

    [Test]
    public void PackedFieldStorage_FieldAccessAndDirtyTracking()
    {
        var schemaId = SchemaId.New();
        var f1 = FieldId.New();
        var f2 = FieldId.New();

        var schema = new SchemaType(
            schemaId,
            "Battery",
            IsComponentSchema: true,
            [
                new SchemaField(f1, "Charge", PrimitiveType.Float32, "100.0"),
                new SchemaField(f2, "MaxCharge", PrimitiveType.Float32, "100.0")
            ]);

        var storage = new PackedFieldStorage(schema, [AstraValue.FromFloat(100.0f), AstraValue.FromFloat(100.0f)]);
        Assert.That(storage.HasAnyDirty, Is.False);

        // Read by FieldId
        Assert.That(storage.GetField(f1).AsFloat(), Is.EqualTo(100.0f));

        // Mutate field
        storage.SetField(f1, AstraValue.FromFloat(95.0f));
        Assert.That(storage.HasAnyDirty, Is.True);
        Assert.That(storage.IsDirty(0), Is.True);
        Assert.That(storage.IsDirty(1), Is.False);
        Assert.That(storage.GetField(f1).AsFloat(), Is.EqualTo(95.0f));

        // Clear dirty
        storage.ClearDirty();
        Assert.That(storage.HasAnyDirty, Is.False);
    }

    [Test]
    public void DynamicComponentStore_AddGetRemoveAndEntityDeletion()
    {
        var store = new DynamicComponentStore();
        var schemaId = SchemaId.New();
        var schema = new SchemaType(
            schemaId,
            "TargetingComponent",
            IsComponentSchema: true,
            [new SchemaField(FieldId.New(), "Range", PrimitiveType.Float32, "10.0")]);

        var entity1 = 42;
        var entity2 = 100;

        store.AddComponent(entity1, schema, [AstraValue.FromFloat(15.0f)]);
        store.AddComponent(entity2, schema, [AstraValue.FromFloat(25.0f)]);

        Assert.That(store.HasComponent(entity1, schemaId), Is.True);
        Assert.That(store.HasComponent(entity2, schemaId), Is.True);
        Assert.That(store.HasComponent(999, schemaId), Is.False);

        var entities = store.GetEntitiesWithComponent(schemaId);
        Assert.That(entities.Count, Is.EqualTo(2));

        Assert.That(store.GetComponent(entity1, schemaId).GetField(0).AsFloat(), Is.EqualTo(15.0f));
        Assert.That(store.GetComponent(entity2, schemaId).GetField(0).AsFloat(), Is.EqualTo(25.0f));

        // Entity deletion simulation
        store.ClearEntity(entity1);
        Assert.That(store.HasComponent(entity1, schemaId), Is.False);
        Assert.That(store.HasComponent(entity2, schemaId), Is.True);
    }

    [Test]
    public void AstraStateStore_PersistentVariablesSnapshotAndRestore()
    {
        var stateStore = new AstraStateStore();
        var graphId = GraphId.New();
        var var1Id = SymbolId.New();
        var var2Id = SymbolId.New();

        // var1 is persistent (e.g. WorldScore)
        stateStore.SetVariable(graphId, var1Id, "WorldScore", AstraValue.FromInt64(500), isPersistent: true);
        // var2 is transient (e.g. TempTimer)
        stateStore.SetVariable(graphId, var2Id, "TempTimer", AstraValue.FromFloat(1.5f), isPersistent: false);

        Assert.That(stateStore.GetVariable(graphId, var1Id, "WorldScore").AsInt64(), Is.EqualTo(500));
        Assert.That(stateStore.GetVariable(graphId, var2Id, "TempTimer").AsFloat(), Is.EqualTo(1.5f));

        // Snapshot persistent
        var snapshot = stateStore.GetPersistentSnapshot();
        Assert.That(snapshot.Count, Is.EqualTo(1));
        Assert.That(snapshot.ContainsKey((graphId, var1Id)), Is.True);

        // Restore into new store
        var newStore = new AstraStateStore();
        newStore.RestorePersistentSnapshot(snapshot);

        Assert.That(newStore.GetVariable(graphId, var1Id, "WorldScore").AsInt64(), Is.EqualTo(500));
        Assert.That(newStore.GetVariable(graphId, var2Id, "TempTimer"), Is.EqualTo(AstraValue.Null));
    }
}
