using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AstraGraph.Core;

namespace AstraGraph.Runtime.Profiling;

public sealed record GraphPerformanceMetric(
    long Invocations,
    double TotalMicroseconds,
    double AverageMicroseconds,
    long InstructionsExecuted,
    long NativeCallsExecuted,
    long Yields);

/// <summary>
/// High-resolution profiler measuring execution timing, instruction throughput,
/// native calls, and node heat maps with near-zero overhead.
/// </summary>
public sealed class GraphProfiler
{
    public readonly struct ProfileScope : IDisposable
    {
        private readonly GraphProfiler _profiler;
        private readonly GraphId _graphId;
        private readonly long _startTimestamp;

        public ProfileScope(GraphProfiler profiler, GraphId graphId)
        {
            _profiler = profiler;
            _graphId = graphId;
            _startTimestamp = Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            var elapsedTicks = Stopwatch.GetTimestamp() - _startTimestamp;
            var microseconds = (elapsedTicks * 1_000_000.0) / Stopwatch.Frequency;
            _profiler.RecordElapsed(_graphId, microseconds);
        }
    }

    private sealed class MetricsAccumulator
    {
        public long Invocations;
        public double TotalMicroseconds;
        public long InstructionsExecuted;
        public long NativeCallsExecuted;
        public long Yields;
    }

    private readonly ConcurrentDictionary<GraphId, MetricsAccumulator> _metrics = new();
    private readonly ConcurrentDictionary<NodeId, long> _nodeHits = new();

    public ProfileScope BeginScope(GraphId graphId) => new(this, graphId);

    public void RecordElapsed(GraphId graphId, double microseconds)
    {
        var acc = _metrics.GetOrAdd(graphId, _ => new MetricsAccumulator());
        lock (acc)
        {
            acc.Invocations++;
            acc.TotalMicroseconds += microseconds;
        }
    }

    public void RecordInstruction(GraphId graphId, NodeId? sourceNode = null)
    {
        var acc = _metrics.GetOrAdd(graphId, _ => new MetricsAccumulator());
        System.Threading.Interlocked.Increment(ref acc.InstructionsExecuted);

        if (sourceNode.HasValue)
        {
            _nodeHits.AddOrUpdate(sourceNode.Value, 1, (_, old) => old + 1);
        }
    }

    public void RecordNativeCall(GraphId graphId)
    {
        var acc = _metrics.GetOrAdd(graphId, _ => new MetricsAccumulator());
        System.Threading.Interlocked.Increment(ref acc.NativeCallsExecuted);
    }

    public void RecordYield(GraphId graphId)
    {
        var acc = _metrics.GetOrAdd(graphId, _ => new MetricsAccumulator());
        System.Threading.Interlocked.Increment(ref acc.Yields);
    }

    public GraphPerformanceMetric GetMetrics(GraphId graphId)
    {
        if (_metrics.TryGetValue(graphId, out var acc))
        {
            lock (acc)
            {
                var avg = acc.Invocations > 0 ? acc.TotalMicroseconds / acc.Invocations : 0.0;
                return new GraphPerformanceMetric(
                    acc.Invocations,
                    acc.TotalMicroseconds,
                    avg,
                    acc.InstructionsExecuted,
                    acc.NativeCallsExecuted,
                    acc.Yields);
            }
        }

        return new GraphPerformanceMetric(0, 0, 0, 0, 0, 0);
    }

    public IReadOnlyList<KeyValuePair<NodeId, long>> GetHottestNodes(int top = 10)
    {
        return _nodeHits
            .OrderByDescending(kv => kv.Value)
            .Take(top)
            .ToList();
    }

    public void Reset()
    {
        _metrics.Clear();
        _nodeHits.Clear();
    }
}
