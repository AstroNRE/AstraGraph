using AstraGraph.Core;
using AstraGraph.Persistence.Cache;
using AstraGraph.Persistence.Discovery;
using AstraGraph.Persistence.State;
using AstraGraph.Runtime;
using AstraGraph.Runtime.Integration;

namespace AstraGraph.HotReload;

/// <summary>
/// Single startup pipeline: crash recovery, state restore, discovery, compile, activation.
/// </summary>
public sealed class AstraBootstrapService
{
    private readonly AstraGraphHost _host;
    private readonly HotReloadManager _hotReload;
    private readonly BootstrapLoader _loader;
    private readonly PersistentStateStore? _stateStore;

    public AstraBootstrapService(
        AstraGraphHost host,
        HotReloadManager hotReload,
        BootstrapLoader loader,
        PersistentStateStore? stateStore = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _hotReload = hotReload ?? throw new ArgumentNullException(nameof(hotReload));
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _stateStore = stateStore;
    }

    public int Activate(string author = "Bootstrap")
    {
        _loader.RecoverOnStartup();

        if (_stateStore != null)
        {
            try
            {
                _stateStore.RestoreState("default", _host.State);
            }
            catch (Exception ex)
            {
                LastWarning = ex.Message;
            }
        }

        var manifestPath = EngineCompatibilityService.FindManifest();
        if (manifestPath != null)
        {
            var manifest = EngineCompatibilityService.Load(manifestPath).Manifest;
            if (!string.Equals(manifest.EngineFamily, "RobustToolbox", StringComparison.OrdinalIgnoreCase))
            {
                throw new EngineCompatibilityException($"Engine family '{manifest.EngineFamily}' is not RobustToolbox.");
            }

            if (manifest.DynamicNativeSystemOrdering)
            {
                throw new EngineCompatibilityException(EngineCompatibilityManifest.NativeOrderingWarning);
            }
        }

        var discovered = _loader.DiscoverAll();
        var cache = new CompilationCache(_loader.Layout);
        var activated = 0;
        foreach (var graph in discovered)
        {
            if (graph.Document.Kind != GraphKind.System)
            {
                continue;
            }

            var published = ActivateGraph(graph, cache, author);
            if (!published)
            {
                continue;
            }

            var graphId = graph.Document.Id;
            _host.Scheduler.RegisterSystem(new SystemRegistration(
                graphId,
                graph.Document.Name,
                Before: [],
                After: [],
                Priority: 0,
                UpdateCallback: (_, _) =>
                {
                    if (_host.Faults.IsOpen(graphId) || _host.GetProgram(graphId) is not { } program)
                    {
                        return;
                    }

                    foreach (var entryPoint in program.EntryPoints)
                    {
                        _host.Vm.Execute(program, entryPoint, hostServices: _host.HostServices);
                    }
                }));
            activated++;
        }

        return activated;
    }

    private bool ActivateGraph(DiscoveredGraph graph, CompilationCache cache, string author)
    {
        var key = CacheKey(graph.Document);
        var active = _host.GetProgram(graph.Document.Id);
        if (active != null &&
            active.SemanticHash == AstraHash.ComputeSemanticHash(graph.Document) &&
            cache.TryGet(key, out _))
        {
            return true;
        }

        var result = _hotReload.Publish(graph.Document, author, "Server startup bootstrap");
        if (!result.Success && graph.Origin == GraphOrigin.Override)
        {
            result = _hotReload.TryRestorePrevious(graph.Document.Id) ?? result;
        }

        if (!result.Success)
        {
            var required = graph.Document.Metadata.CustomAttributes.TryGetValue("required", out var flag) &&
                           flag.Equals("true", StringComparison.OrdinalIgnoreCase);
            LastWarning = result.ErrorMessage ?? $"Graph '{graph.Document.Name}' failed to activate.";
            if (required)
            {
                throw new InvalidOperationException($"Required graph '{graph.Document.Name}' failed to activate: {LastWarning}");
            }

            return false;
        }

        if (_host.GetProgram(graph.Document.Id) is { } program)
        {
            cache.Store(key, BytecodeSerializer.SerializeToBytes(program));
        }

        return true;
    }

    private static CompilationCacheKey CacheKey(GraphDocument document)
    {
        var schema = string.Join(",", document.Variables.Select(variable => variable.Name + ":" + variable.TypeName));
        return new CompilationCacheKey(
            AstraHash.ComputeSemanticHash(document),
            "1.0.0",
            "1.0.0",
            "",
            document.Side.ToString(),
            SchemaSetHash: schema);
    }

    public string? LastWarning { get; private set; }
}
