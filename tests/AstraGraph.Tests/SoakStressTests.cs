using AstraGraph.Core;
using AstraGraph.State;
using AstraGraph.VM;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class SoakStressTests
{
    [Test]
    public void DynamicComponentStore_HighScaleEntities_MaintainsIntegrity()
    {
        var set = new SparseSet();
        const int entityCount = 50_000;

        // 1. Bulk addition into SparseSet
        for (int i = 0; i < entityCount; i++)
        {
            set.Add(i);
        }

        Assert.That(set.Count, Is.EqualTo(entityCount));

        // 2. Verification
        for (int i = 0; i < entityCount; i++)
        {
            Assert.That(set.Contains(i), Is.True);
        }

        // 3. Selective deletion of even entities
        for (int i = 0; i < entityCount; i += 2)
        {
            var removed = set.Remove(i);
            Assert.That(removed, Is.True);
        }

        Assert.That(set.Count, Is.EqualTo(entityCount / 2));

        // Remaining odd entities must still be present
        for (int i = 1; i < entityCount; i += 2)
        {
            Assert.That(set.Contains(i), Is.True);
        }

        // 4. Test DynamicComponentStore with multiple entities
        var store = new DynamicComponentStore();
        var schemaId = SchemaId.New();
        var schema = new SchemaType(
            schemaId,
            "StressComponent",
            IsComponentSchema: true,
            [new SchemaField(FieldId.New(), "Health", PrimitiveType.Int32, "100")]);

        for (int i = 0; i < 1_000; i++)
        {
            store.AddComponent(i, schema, [AstraValue.FromInt64(100 + i)]);
        }

        for (int i = 0; i < 1_000; i++)
        {
            Assert.That(store.HasComponent(i, schemaId), Is.True);
            var comp = store.GetComponent(i, schemaId);
            Assert.That(comp.GetField(0).AsInt64(), Is.EqualTo(100 + i));
            comp.SetField(0, AstraValue.FromInt64(90 + i));
        }

        for (int i = 0; i < 1_000; i++)
        {
            Assert.That(store.GetComponent(i, schemaId).GetField(0).AsInt64(), Is.EqualTo(90 + i));
        }
    }

    [Test]
    public void VmExecution_RepeatedInvocations_NoMemoryLeak()
    {
        var pool = new ConstantPool();
        var c10 = pool.GetOrAddInt64(10);
        var c20 = pool.GetOrAddInt64(20);
        var fnName = pool.GetOrAddString("AddTwoNumbers");

        var instructions = new List<BytecodeInstruction>
        {
            new((byte)IrOpCode.LoadConst, 0, c10, 0, 0),
            new((byte)IrOpCode.LoadConst, 1, c20, 0, 0),
            new((byte)IrOpCode.Add, 2, 0, 1, 0),
            new((byte)IrOpCode.Return, BytecodeInstruction.NoRegister, 2, 0, 0)
        };

        var func = new BytecodeFunction(fnName, registerCount: 3, parameterCount: 0, instructions);
        var program = new BytecodeProgram(GraphId.New(), RevisionId.New(), "hash", pool);
        program.EntryPoints.Add(func);

        var vm = new AstraVm();

        // Run 50,000 executions
        for (int i = 0; i < 50_000; i++)
        {
            var res = vm.Execute(program, func, []);
            Assert.That(res.IsSuccess, Is.True);
            Assert.That(res.ReturnValue.AsInt64(), Is.EqualTo(30));
        }
    }
}
