using AstraGraph.Core;
using AstraGraph.Persistence.Discovery;
using AstraGraph.Persistence.State;
using AstraGraph.Runtime;

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

        var discovered = _loader.DiscoverAll();
        var activated = 0;
        foreach (var graph in discovered)
        {
            if (graph.Document.Kind != GraphKind.System)
            {
                continue;
            }

            _hotReload.Publish(graph.Document, author, "Server startup bootstrap");
            var graphId = graph.Document.Id;
            _host.Scheduler.RegisterSystem(new SystemRegistration(
                graphId,
                graph.Document.Name,
                Before: [],
                After: [],
                Priority: 0,
                UpdateCallback: (_, _) =>
                {
                    if (_host.GetProgram(graphId) is not { } program)
                    {
                        return;
                    }

                    foreach (var entryPoint in program.EntryPoints)
                    {
                        _host.Vm.Execute(program, entryPoint, hostServices: null);
                    }
                }));
            activated++;
        }

        return activated;
    }

    public string? LastWarning { get; private set; }
}
