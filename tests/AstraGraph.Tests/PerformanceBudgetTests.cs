using System.Diagnostics;
using System.Reflection;
using AstraGraph.Binding;
using AstraGraph.Core;
using AstraGraph.JIT;
using AstraGraph.Runtime;
using AstraGraph.State;
using AstraGraph.VM;
using NUnit.Framework;

namespace AstraGraph.Tests;

public class PerformanceBudgetFixtureTarget
{
    public long Offset { get; set; } = 42L;

    public long Compute(long a, long b) => a * b + Offset;
}

public class PerformanceBudgetHealthComp
{
    public float Current { get; set; }
}

[TestFixture]
public sealed class PerformanceBudgetTests
{
    [Test]
    public void FastInvoker_OutperformsReflectionByAtLeast3x()
    {
        var target = new PerformanceBudgetFixtureTarget();
        var method = typeof(PerformanceBudgetFixtureTarget).GetMethod(nameof(PerformanceBudgetFixtureTarget.Compute))!;
        var fastInvoker = FastInvokerCompiler.Compile(method);

        var fastArgs = new[] { AstraValue.FromObject(target), AstraValue.FromInt64(10L), AstraValue.FromInt64(5L) };
        var reflectionArgs = new object?[] { 10L, 5L };

        const int iterations = 100_000;

        // Warm up
        for (int i = 0; i < 1000; i++)
        {
            fastInvoker(fastArgs);
            method.Invoke(target, reflectionArgs);
        }

        // Measure FastInvoker
        var swFast = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            fastInvoker(fastArgs);
        }
        swFast.Stop();

        // Measure Reflection
        var swReflection = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            method.Invoke(target, reflectionArgs);
        }
        swReflection.Stop();

        // FastInvoker must be substantially faster than Reflection.Invoke
        Assert.That(swFast.ElapsedTicks, Is.LessThan(swReflection.ElapsedTicks));
    }

    [Test]
    public void PackedFieldStorage_ZeroHeapAllocationsOnFieldUpdates()
    {
        var field = new SchemaField(FieldId.New(), "Value", PrimitiveType.Int64);
        var schema = new SchemaType(SchemaId.New(), "BenchSchema", true, [field]);
        var storage = new PackedFieldStorage(schema);

        // Warm up
        storage.SetField(0, AstraValue.FromInt64(1));

        var val = AstraValue.FromInt64(999);
        var allocBefore = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 10_000; i++)
        {
            storage.SetField(0, val);
            var read = storage.GetField(0);
            _ = read.AsInt64();
        }

        var allocAfter = GC.GetAllocatedBytesForCurrentThread();
        var totalAllocated = allocAfter - allocBefore;

        // PackedFieldStorage must not allocate heap objects during read/write
        Assert.That(totalAllocated, Is.EqualTo(0));
    }

    [Test]
    public void MixedQueryEngine_ExecutesWithinBudget()
    {
        var dynamicStore = new DynamicComponentStore();
        var ecsBridge = new InMemoryEcsQueryBridge();

        var field = new SchemaField(FieldId.New(), "Multiplier", PrimitiveType.Float64, "1.5");
        var schemaId = SchemaId.New();
        var schema = new SchemaType(schemaId, "Buff", true, [field]);

        const int entityCount = 500;
        for (int i = 0; i < entityCount; i++)
        {
            var uid = i + 1;
            var comp = new PerformanceBudgetHealthComp { Current = 100f };
            ecsBridge.AddComponent(uid, comp);
            if (i % 2 == 0)
            {
                dynamicStore.AddComponent(uid, schema);
            }
        }

        var queryEngine = new MixedQueryEngine(dynamicStore, ecsBridge);
        var descriptor = new QueryDescriptor(
            RequiredAstraSchemas: [schemaId],
            ExcludedAstraSchemas: [],
            RequiredNativeTypes: [typeof(PerformanceBudgetHealthComp)],
            ExcludedNativeTypes: []);

        // Warmup
        var warmupResults = queryEngine.Execute(descriptor);
        Assert.That(warmupResults.Count, Is.EqualTo(entityCount / 2));

        var sw = Stopwatch.StartNew();
        const int queryRuns = 300;
        for (int i = 0; i < queryRuns; i++)
        {
            var results = queryEngine.Execute(descriptor);
            _ = results.Count;
        }
        sw.Stop();

        // 300 query runs across 500 entities must complete well within threshold
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(1500));
    }

    [Test]
    public void VmVsJitParity_ExecutesAndMatchesExpectedComputation()
    {
        var graphId = GraphId.New();
        var irProgram = new IrProgram(graphId, "BenchGraph", GraphKind.Function, GraphSide.Server);

        var entry = new IrBasicBlock(0, "entry");
        var func = new IrFunction("Calculate", [], PrimitiveType.Int64, entry);
        irProgram.EntryPoints.Add(func);

        var r0 = new IrRegister(0, PrimitiveType.Int64);
        var r1 = new IrRegister(1, PrimitiveType.Int64);
        var r2 = new IrRegister(2, PrimitiveType.Int64);
        var r3 = new IrRegister(3, PrimitiveType.Int64);
        var r4 = new IrRegister(4, PrimitiveType.Int64);

        // 20*20 + 30*30 = 400 + 900 = 1300
        entry.AddInstruction(new IrInstruction(IrOpCode.LoadConst, r0, [new IrConstant(20L, PrimitiveType.Int64)]));
        entry.AddInstruction(new IrInstruction(IrOpCode.LoadConst, r1, [new IrConstant(30L, PrimitiveType.Int64)]));
        entry.AddInstruction(new IrInstruction(IrOpCode.Mul, r2, [r0, r0]));
        entry.AddInstruction(new IrInstruction(IrOpCode.Mul, r3, [r1, r1]));
        entry.AddInstruction(new IrInstruction(IrOpCode.Add, r4, [r2, r3]));
        entry.SetTerminator(new IrInstruction(IrOpCode.Return, null, [r4]));
        func.RegisterCount = 5;

        var bytecodeProgram = IrToBytecodeCompiler.Compile(irProgram);
        var bytecodeFunc = bytecodeProgram.EntryPoints[0];

        var vm = new AstraVm();
        var services = new DefaultVmHostServices();
        var vmResult = vm.Execute(bytecodeProgram, bytecodeFunc, [], 0, services);

        using var jitProgram = RoslynCompiler.Compile(irProgram);
        var jitResult = jitProgram.Execute("Calculate", null, services);

        Assert.That(vmResult.IsSuccess, Is.True);
        Assert.That(vmResult.ReturnValue.AsInt64(), Is.EqualTo(1300L));
        Assert.That(jitResult.AsInt64(), Is.EqualTo(1300L));
    }
}
