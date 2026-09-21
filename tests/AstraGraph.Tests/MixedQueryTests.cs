using AstraGraph.Core;
using AstraGraph.Runtime;
using AstraGraph.State;
using NUnit.Framework;

namespace AstraGraph.Tests;

public sealed class NativeTransformComponent
{
    public float X { get; set; }
    public float Y { get; set; }
}

public sealed class DeadComponent { }

[TestFixture]
public sealed class MixedQueryTests
{
    [Test]
    public void MixedQueryEngine_SelectsMostSelectiveIndex_FiltersNativeAndAstraComponents()
    {
        var componentStore = new DynamicComponentStore();
        var ecsBridge = new InMemoryEcsQueryBridge();
        var queryEngine = new MixedQueryEngine(componentStore, ecsBridge);

        var shieldSchemaId = SchemaId.New();
        var shieldSchema = new SchemaType(
            shieldSchemaId,
            "CustomShield",
            IsComponentSchema: true,
            [new SchemaField(FieldId.New(), "Charge", PrimitiveType.Float32, "100.0")]);

        // 1. Setup 1,000 entities with NativeTransformComponent
        for (var i = 1; i <= 1000; i++)
        {
            ecsBridge.AddComponent(i, new NativeTransformComponent { X = i, Y = i });
        }

        // 2. Add CustomShield to only 5 entities: 10, 20, 30, 40, 50
        componentStore.AddComponent(10, shieldSchema, [AstraValue.FromFloat(100.0f)]);
        componentStore.AddComponent(20, shieldSchema, [AstraValue.FromFloat(50.0f)]);
        componentStore.AddComponent(30, shieldSchema, [AstraValue.FromFloat(25.0f)]);
        componentStore.AddComponent(40, shieldSchema, [AstraValue.FromFloat(80.0f)]);
        componentStore.AddComponent(50, shieldSchema, [AstraValue.FromFloat(10.0f)]);

        // 3. Mark entity 30 as Dead
        ecsBridge.AddComponent(30, new DeadComponent());

        // 4. Query: (NativeTransform AND CustomShield) WHERE NOT Dead
        var query = new QueryDescriptor(
            RequiredAstraSchemas: [shieldSchemaId],
            ExcludedAstraSchemas: [],
            RequiredNativeTypes: [typeof(NativeTransformComponent)],
            ExcludedNativeTypes: [typeof(DeadComponent)]);

        var results = queryEngine.Execute(query);

        // Expected: 10, 20, 40, 50 (30 is excluded because it is Dead)
        Assert.That(results.Count, Is.EqualTo(4));
        var matchedIds = results.Select(r => r.EntityUid).OrderBy(id => id).ToList();
        Assert.That(matchedIds, Is.EqualTo((int[])[10, 20, 40, 50]));

        // Verify dynamic component access
        var entity10 = results.First(r => r.EntityUid == 10);
        Assert.That(entity10.AstraComponents.ContainsKey(shieldSchemaId), Is.True);
        Assert.That(entity10.AstraComponents[shieldSchemaId].GetField(0).AsFloat(), Is.EqualTo(100.0f));
    }
}
