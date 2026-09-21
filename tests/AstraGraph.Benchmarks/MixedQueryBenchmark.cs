using AstraGraph.Core;
using AstraGraph.Runtime;
using AstraGraph.State;
using BenchmarkDotNet.Attributes;

namespace AstraGraph.Benchmarks;

public class NativeHealthBenchmark
{
    public float Current { get; set; }
}

[MemoryDiagnoser]
public class MixedQueryBenchmark
{
    private const int EntityCount = 1_000;
    private readonly Dictionary<int, NativeHealthBenchmark> _nativeEntities = new();
    private DynamicComponentStore _dynamicStore = null!;
    private InMemoryEcsQueryBridge _ecsBridge = null!;
    private MixedQueryEngine _queryEngine = null!;
    private QueryDescriptor _queryDescriptor = null!;
    private readonly SchemaId _buffSchemaId = SchemaId.New();

    [GlobalSetup]
    public void Setup()
    {
        _dynamicStore = new DynamicComponentStore();
        _ecsBridge = new InMemoryEcsQueryBridge();

        var fieldDef = new SchemaField(FieldId.New(), "Multiplier", PrimitiveType.Float64, "1.5");
        var buffSchema = new SchemaType(_buffSchemaId, "ActiveBuff", true, [fieldDef]);

        for (int i = 0; i < EntityCount; i++)
        {
            var uid = i + 1;
            var comp = new NativeHealthBenchmark { Current = 100.0f };
            _nativeEntities[uid] = comp;
            _ecsBridge.AddComponent(uid, comp);

            // Half of the entities also have the Astra dynamic component
            if (i % 2 == 0)
            {
                _dynamicStore.AddComponent(uid, buffSchema);
            }
        }

        _queryEngine = new MixedQueryEngine(_dynamicStore, _ecsBridge);
        _queryDescriptor = new QueryDescriptor(
            RequiredAstraSchemas: [_buffSchemaId],
            ExcludedAstraSchemas: [],
            RequiredNativeTypes: [typeof(NativeHealthBenchmark)],
            ExcludedNativeTypes: []);
    }

    [Benchmark(Baseline = true)]
    public int PureCSharpQuery()
    {
        int count = 0;
        foreach (var (uid, health) in _nativeEntities)
        {
            if (health.Current > 0 && uid % 2 == 0)
            {
                count++;
            }
        }
        return count;
    }

    [Benchmark]
    public int MixedEngineQuery()
    {
        var results = _queryEngine.Execute(_queryDescriptor);
        return results.Count;
    }
}
