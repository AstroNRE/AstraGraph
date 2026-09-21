using AstraGraph.Core;
using AstraGraph.State;
using BenchmarkDotNet.Attributes;

namespace AstraGraph.Benchmarks;

public class NativeComponentBenchmark
{
    public long Value { get; set; }
}

[MemoryDiagnoser]
public class ComponentAccessBenchmark
{
    private NativeComponentBenchmark _native = null!;
    private PackedFieldStorage _packedStorage = null!;
    private DynamicComponentStore _store = null!;
    private SchemaType _schema = null!;
    private readonly SchemaId _schemaId = SchemaId.New();
    private const int EntityUid = 1001;

    [GlobalSetup]
    public void Setup()
    {
        _native = new NativeComponentBenchmark { Value = 42L };

        var fieldDef = new SchemaField(FieldId.New(), "Value", PrimitiveType.Int64);
        _schema = new SchemaType(_schemaId, "BenchComponent", true, [fieldDef]);

        _packedStorage = new PackedFieldStorage(_schema);
        _packedStorage.SetField(0, AstraValue.FromInt64(42L));

        _store = new DynamicComponentStore();
        _store.AddComponent(EntityUid, _schema, [AstraValue.FromInt64(42L)]);
    }

    [Benchmark(Baseline = true)]
    public long DirectPropertyReadWrite()
    {
        _native.Value += 1;
        return _native.Value;
    }

    [Benchmark]
    public AstraValue PackedStorageReadWrite()
    {
        var val = _packedStorage.GetField(0);
        var updated = AstraValue.FromInt64(val.AsInt64() + 1);
        _packedStorage.SetField(0, updated);
        return updated;
    }

    [Benchmark]
    public AstraValue DynamicStoreReadWrite()
    {
        var storage = _store.GetComponent(EntityUid, _schemaId)!;
        var val = storage.GetField(0);
        var updated = AstraValue.FromInt64(val.AsInt64() + 1);
        storage.SetField(0, updated);
        return updated;
    }
}
