using System;
using System.Collections.Concurrent;
using System.Reflection;
using AstraGraph.Core;
using AstraGraph.VM;

namespace AstraGraph.JIT;

public delegate AstraValue JitFunctionInvoker(
    AstraValue[]? initialRegisters,
    IVmHostServices services,
    ExecutionBudget budget);

/// <summary>
/// An executable JIT-compiled AstraGraph program residing in a collectible AssemblyLoadContext.
/// Supports zero-leak unloading upon hot reload.
/// </summary>
public sealed class JitCompiledProgram : IDisposable
{
    private AstraLoadContext? _loadContext;
    private Assembly? _assembly;
    private readonly ConcurrentDictionary<string, JitFunctionInvoker> _invokers = new(StringComparer.Ordinal);
    private bool _isDisposed;

    public GraphId Id { get; }
    public RevisionId Revision { get; }
    public string GeneratedClassName { get; }
    public bool IsAlive => _loadContext != null && !_isDisposed;

    public JitCompiledProgram(
        GraphId id,
        RevisionId revision,
        string generatedClassName,
        AstraLoadContext loadContext,
        Assembly assembly)
    {
        Id = id;
        Revision = revision;
        GeneratedClassName = generatedClassName;
        _loadContext = loadContext ?? throw new ArgumentNullException(nameof(loadContext));
        _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
    }

    public void RegisterInvoker(string functionName, JitFunctionInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(functionName);
        ArgumentNullException.ThrowIfNull(invoker);
        _invokers[functionName] = invoker;
    }

    public AstraValue Execute(
        string functionName,
        AstraValue[]? initialRegisters,
        IVmHostServices services,
        ExecutionBudget? budget = null)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (!_invokers.TryGetValue(functionName, out var invoker))
        {
            throw new MissingMethodException($"JIT-compiled function '{functionName}' not found in program {Id}.");
        }

        var b = budget ?? new ExecutionBudget();
        return invoker(initialRegisters, services, b);
    }

    public void Unload()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _invokers.Clear();
        _assembly = null;

        if (_loadContext != null)
        {
            var ctx = _loadContext;
            _loadContext = null;
            ctx.Unload();
        }
    }

    public void Dispose()
    {
        Unload();
        GC.SuppressFinalize(this);
    }
}
