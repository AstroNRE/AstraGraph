using System.Collections.Concurrent;
using AstraGraph.Binding;
using AstraGraph.Core;
using AstraGraph.Editor.Protocol;
using AstraGraph.HotReload;
using AstraGraph.Persistence;
using AstraGraph.Persistence.Audit;
using AstraGraph.Persistence.Discovery;
using AstraGraph.Runtime.Debugging;
using AstraGraph.Runtime.Profiling;
using AstraGraph.Runtime.Security;

namespace AstraGraph.Robust.Server;

public sealed class AstraAuthoringService : IAstraAuthoringService
{
    private readonly IAstraPermissionProvider _permissionProvider;
    private readonly HotReloadManager _hotReloadManager;
    private readonly StorageLayout? _storageLayout;
    private readonly BootstrapLoader? _bootstrapLoader;
    private readonly AuthoringServerSession _serverSession;
    private readonly GraphDebugger? _debugger;
    private readonly GraphProfiler? _profiler;
    private readonly ConcurrentDictionary<string, (AstraUser User, DateTimeOffset ExpiresAtUtc)> _issuedTokens = new();

    public AuthoringServerSession Session => _serverSession;
    public string? DiscoveryWarning { get; private set; }
    public IAuthoringMessageHandler MessageHandler => _serverSession;

    public AstraAuthoringService(
        IAstraPermissionProvider permissionProvider,
        HotReloadManager hotReloadManager,
        AuditLogger? auditLogger = null,
        GraphDebugger? debugger = null,
        GraphProfiler? profiler = null,
        BindingCatalog? bindingCatalog = null,
        StorageLayout? storageLayout = null,
        BootstrapLoader? bootstrapLoader = null)
    {
        _permissionProvider = permissionProvider ?? throw new ArgumentNullException(nameof(permissionProvider));
        _hotReloadManager = hotReloadManager ?? throw new ArgumentNullException(nameof(hotReloadManager));
        _storageLayout = storageLayout;
        _bootstrapLoader = bootstrapLoader;

        _debugger = debugger;
        _profiler = profiler;
        _permissionProvider.OnPermissionsChanged += RecomputeSession;

        _serverSession = new AuthoringServerSession(
            userAuthenticator: AuthenticateToken,
            hotReloadManager: _hotReloadManager,
            auditLogger: auditLogger,
            debugger: debugger,
            profiler: profiler,
            bindingCatalog: bindingCatalog);

        SyncDiscoveredGraphs();
    }

    public bool CanEnterAstra(object session) => _permissionProvider.CanEnterAstra(session);

    public AuthoringLaunchToken? IssueLaunchToken(object session, TimeSpan? lifetime = null)
    {
        var user = _permissionProvider.ResolveUser(session);
        if (user == null || !_permissionProvider.CanEnterAstra(session))
        {
            return null;
        }

        var token = "astra_" + Guid.NewGuid().ToString("N");
        var expiresAt = DateTimeOffset.UtcNow + (lifetime ?? TimeSpan.FromMinutes(30));

        _issuedTokens[token] = (user, expiresAt);

        return new AuthoringLaunchToken(token, user.Id, user.Name, user.Permissions, expiresAt);
    }

    public bool TryValidateToken(string token, out AstraUser? user)
    {
        user = null;
        if (string.IsNullOrEmpty(token)) return false;

        if (_issuedTokens.TryGetValue(token, out var entry))
        {
            if (entry.ExpiresAtUtc >= DateTimeOffset.UtcNow)
            {
                user = entry.User;
                return true;
            }
            _issuedTokens.TryRemove(token, out _);
        }
        return false;
    }

    /// <summary>
    /// Recomputes an already connected authoring session after deadmin or a rank change.
    /// </summary>
    public void RecomputeSession(object session)
    {
        var userId = session is IAstraAdminFacts facts
            ? facts.UserId
            : session is global::Robust.Shared.Player.ICommonSession common ? common.UserId.ToString() : null;
        if (userId == null)
        {
            return;
        }

        var user = _permissionProvider.ResolveUser(session);
        foreach (var key in _issuedTokens.Keys.ToArray())
        {
            if (!_issuedTokens.TryGetValue(key, out var entry) || entry.User.Id != userId)
            {
                continue;
            }

            if (user == null)
            {
                _issuedTokens.TryRemove(key, out _);
            }
            else
            {
                _issuedTokens[key] = (user, entry.ExpiresAtUtc);
            }
        }

        _serverSession.ReplaceUser(userId, user);
        if (user == null || !AstraAuthorizationService.HasPermission(user, AstraPermission.Debug))
        {
            _debugger?.ClearBreakpoints();
            _profiler?.Reset();
        }
    }

    public AstraUser? AuthenticateToken(string token)
    {
        return TryValidateToken(token, out var user) ? user : null;
    }

    public void SyncDiscoveredGraphs()
    {
        if (_bootstrapLoader == null) return;

        try
        {
            var discovered = _bootstrapLoader.DiscoverAll();
            foreach (var graph in discovered)
            {
                var doc = graph.Document;
                var activeRev = _hotReloadManager.Host.GetProgram(doc.Id)?.Revision ?? RevisionId.New();
                _serverSession.RegisterGraph(new GraphSummaryDto(
                    doc.Id,
                    doc.Name,
                    doc.Kind,
                    doc.Side,
                    activeRev,
                    1));
            }
        }
        catch (Exception ex)
        {
            DiscoveryWarning = ex.Message;
        }
    }
}
