using System.Reflection;
using AstraGraph.Binding;
using AstraGraph.Core;
using BenchmarkDotNet.Attributes;

namespace AstraGraph.Benchmarks;

public class SampleTarget
{
    public long Offset { get; set; } = 42L;

    public long Compute(long a, long b) => a * b + Offset;
}

[MemoryDiagnoser]
public class NativeCallBenchmark
{
    private SampleTarget _target = null!;
    private MethodInfo _methodInfo = null!;
    private Func<AstraValue[], AstraValue> _fastInvoker = null!;
    private AstraValue[] _args = null!;
    private object?[] _reflectionArgs = null!;

    [GlobalSetup]
    public void Setup()
    {
        _target = new SampleTarget();
        _methodInfo = typeof(SampleTarget).GetMethod(nameof(SampleTarget.Compute))!;
        _fastInvoker = FastInvokerCompiler.Compile(_methodInfo);
        _args = [AstraValue.FromObject(_target), AstraValue.FromInt64(10L), AstraValue.FromInt64(5L)];
        _reflectionArgs = [10L, 5L];
    }

    [Benchmark(Baseline = true)]
    public long DirectCall()
    {
        return _target.Compute(10L, 5L);
    }

    [Benchmark]
    public AstraValue FastInvokerCall()
    {
        return _fastInvoker(_args);
    }

    [Benchmark]
    public object? ReflectionCall()
    {
        return _methodInfo.Invoke(_target, _reflectionArgs);
    }
}
