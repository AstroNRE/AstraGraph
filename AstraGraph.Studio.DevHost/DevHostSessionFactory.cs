using System.Reflection;
using AstraGraph.Binding;
using AstraGraph.Core;
using AstraGraph.Editor.Protocol;
using AstraGraph.HotReload;
using AstraGraph.Persistence;
using AstraGraph.Persistence.Discovery;
using AstraGraph.Runtime;
using AstraGraph.Runtime.Profiling;
using AstraGraph.Runtime.Security;

namespace AstraGraph.Studio.DevHost;

public sealed class StandaloneDevSession
{
    public required AstraGraphFacade Facade { get; init; }
    public required AuthoringServerSession Session { get; init; }
    public required StorageLayout Layout { get; init; }
    public required IAuthoringMessageHandler Handler { get; init; }
}

public static class DevHostSessionFactory
{
    public static StandaloneDevSession CreateStandalone(DevHostOptions options, string token, Action<string> log)
    {
        var (project, data) = ResolveStorage(options);
        var host = new AstraGraphHost();
        var permissions = new PolicyPermissionProvider();
        permissions.GrantGate(DevAuthenticationService.UserId);
        permissions.SetPermissions(DevAuthenticationService.UserId, DevAuthenticationService.ResolvePermissions(options.Permissions));
        permissions.SetProfile(DevAuthenticationService.UserId, DevAuthenticationService.ResolveProfile(options.Permissions));

        var facade = AstraGraphFacade.ForServer(host, new StorageLayout(project, data), permissions);
        RegisterAstraBuiltins(facade.Catalog);
        facade.Initialize();

        var user = DevAuthenticationService.CreateUser(options.Permissions);
        var session = new AuthoringServerSession(
            authorToken => DevAuthenticationService.TokenMatches(token, authorToken) ? user : null,
            hotReloadManager: facade.Reloader,
            debugger: facade.Host.Debugger,
            profiler: new GraphProfiler(),
            bindingCatalog: facade.Catalog);

        if (facade.Discovery != null)
        {
            foreach (var graph in facade.Discovery.DiscoverAll())
            {
                var document = graph.Document;
                var revision = facade.Host.GetProgram(document.Id)?.Revision ?? RevisionId.New();
                session.RegisterGraph(new GraphSummaryDto(document.Id, document.Name, document.Kind, document.Side, revision, 1));
            }
        }

        facade.Host.Debugger.OnSuspension += suspension =>
        {
            session.Publish(new DebugStreamEventMsg
            {
                CurrentNode = suspension.NodeId,
                Action = "paused",
                TimestampTicks = suspension.Timestamp.UtcTicks
            });
        };

        return new StandaloneDevSession
        {
            Facade = facade,
            Session = session,
            Layout = facade.Discovery?.Layout ?? new StorageLayout(project, data),
            Handler = new LoggingAuthoringHandler(session, token, log)
        };
    }

    public static (string Project, string Data) ResolveStorage(DevHostOptions options)
    {
        if (options.ProjectPath != null || options.DataPath != null)
        {
            return (
                options.ProjectPath ?? Path.Combine("AstraGraphDev", "Resources", "AstraGraph"),
                options.DataPath ?? Path.Combine("AstraGraphDev", "data", "AstraGraph"));
        }

        var root = Path.Combine(Directory.GetCurrentDirectory(), "AstraGraphDev");
        return (Path.Combine(root, "Resources", "AstraGraph"), Path.Combine(root, "data", "AstraGraph"));
    }

    private static void RegisterAstraBuiltins(BindingCatalog catalog)
    {
        catalog.IndexAssembly(typeof(AstraGraphHost).Assembly);
        catalog.IndexAssembly(typeof(BindingCatalog).Assembly);
        var method = typeof(string).GetMethod(nameof(string.IsNullOrEmpty), BindingFlags.Public | BindingFlags.Static, [typeof(string)]);
        if (method != null)
        {
            catalog.RegisterMethod(
                method,
                customDescriptor: "Astra.String.IsNullOrEmpty(string)",
                isDeterministic: true,
                profile: SecurityProfile.Gameplay);
        }
    }
}

sealed class LoggingAuthoringHandler : IAuthoringMessageHandler, IAuthoringSessionHost
{
    private readonly AuthoringServerSession _session;
    private readonly string _token;
    private readonly Action<string> _log;

    public LoggingAuthoringHandler(AuthoringServerSession session, string token, Action<string> log)
    {
        _session = session;
        _token = token;
        _log = log;
    }

    public AuthoringServerSession Session => _session;

    public async Task<AuthoringMessage?> HandleAsync(AuthoringMessage message, CancellationToken ct)
    {
        if (message is AuthHandshakeRequestMsg handshake && string.IsNullOrEmpty(handshake.AuthorToken))
        {
            message = new AuthHandshakeRequestMsg
            {
                MessageId = handshake.MessageId,
                Timestamp = handshake.Timestamp,
                ClientVersion = handshake.ClientVersion,
                AuthorName = handshake.AuthorName,
                AuthorToken = _token
            };
        }

        var response = await _session.HandleAsync(message, ct);
        if (message is AuthHandshakeRequestMsg && response is AuthHandshakeResponseMsg hello && hello.Status == AuthoringStatusCode.Success)
        {
            _log("[Authoring] Session authenticated");
        }
        else if (message is DraftCompileRequestMsg && response is DraftCompileResponseMsg compile && compile.Status == AuthoringStatusCode.Success)
        {
            _log("[Authoring] Graph compiled");
        }
        else if (message is DraftPublishRequestMsg && response is DraftPublishResponseMsg published && published.Status == AuthoringStatusCode.Success)
        {
            _log($"[Authoring] Graph published {published.PublishedRevision}");
        }

        return response;
    }
}
