using System.Collections.Concurrent;
using AstraGraph.Binding;
using AstraGraph.Runtime.Security;
using Robust.Shared.Player;

namespace AstraGraph.Robust.Server;

/// <summary>
/// Connects AstraGraph's RBAC security model to RobustToolbox's session management
/// and SS14's AdminFlags.AstraGraph (1u << 24) gate.
/// </summary>
/// <summary>
/// Content-side lookup over IAdminManager. AstraGraph does not reference Content admin types.
/// </summary>
public interface IAstraAdminDirectory
{
    bool TryGetAdmin(string userId, out uint adminFlags, out string? rank, out bool isSandbox);
}

public sealed class RobustAdminPermissionProvider : IAstraPermissionProvider
{
    private readonly AstraAdminPermissionResolver _resolver;
    private readonly IAstraAdminDirectory? _directory;
    private readonly ConcurrentDictionary<string, AstraUser> _sessionCache = new();
    private readonly ConcurrentDictionary<string, (AstraPermission Permissions, SecurityProfile Profile)> _userOverrides = new();
    private readonly ConcurrentDictionary<string, AstraPermission> _userDenies = new();

    public event Action<object>? OnPermissionsChanged;

    public RobustAdminPermissionProvider(
        uint requiredAdminFlag = SS14AdminFlagsConstants.AdminFlagAstraGraph,
        IAstraAdminDirectory? directory = null)
    {
        _resolver = new AstraAdminPermissionResolver(requiredAdminFlag);
        _directory = directory;
    }

    public bool CanEnterAstra(object session)
    {
        return ResolveUser(session) != null;
    }

    public bool HasAccess(object session) => CanEnterAstra(session);

    public AstraUser? ResolveUser(object session)
    {
        if (session == null) return null;

        string sessionId = GetSessionIdentifier(session);
        if (_sessionCache.TryGetValue(sessionId, out var cachedUser))
        {
            return cachedUser;
        }

        // Check if explicit user override exists
        if (_userOverrides.TryGetValue(sessionId, out var userOverride))
        {
            var effectivePerms = userOverride.Permissions;
            if (_userDenies.TryGetValue(sessionId, out var denied))
            {
                effectivePerms &= ~denied;
            }

            var overriddenUser = new AstraUser(
                sessionId,
                GetSessionName(session),
                effectivePerms,
                userOverride.Profile);

            _sessionCache[sessionId] = overriddenUser;
            return overriddenUser;
        }

        // 1. Extract Admin Flags and Rank from session
        var (adminFlags, rank, isSandbox) = ExtractAdminMetadata(session);

        // 2. Resolve via AstraAdminPermissionResolver
        var resolution = _resolver.ResolveSession(sessionId, GetSessionName(session), adminFlags, rank, isSandbox);
        if (!resolution.IsAllowed || resolution.User == null)
        {
            return null;
        }

        var resolvedUser = resolution.User;
        if (_userDenies.TryGetValue(sessionId, out var denyMask))
        {
            resolvedUser = resolvedUser with { Permissions = resolvedUser.Permissions & ~denyMask };
        }

        _sessionCache[sessionId] = resolvedUser;
        return resolvedUser;
    }

    public AstraPermission GetEffectivePermissions(object session)
    {
        return ResolveUser(session)?.Permissions ?? AstraPermission.None;
    }

    public bool HasPermission(object session, AstraPermission permission)
    {
        var user = ResolveUser(session);
        return user != null && AstraAuthorizationService.HasPermission(user, permission);
    }

    public SecurityProfile GetSecurityProfile(object session)
    {
        return ResolveUser(session)?.Profile ?? SecurityProfile.Gameplay;
    }

    public void RegisterUserOverride(string userId, AstraPermission permissions, SecurityProfile profile = SecurityProfile.Gameplay)
    {
        _userOverrides[userId] = (permissions, profile);
        _sessionCache.TryRemove(userId, out _);
    }

    public void RegisterUserDeny(string userId, AstraPermission deniedPermissions)
    {
        _userDenies[userId] = deniedPermissions;
        _sessionCache.TryRemove(userId, out _);
    }

    /// <summary>
    /// Invalidates cached permissions upon deadmin or admin rank changes.
    /// </summary>
    public void InvalidateSession(object session)
    {
        if (session == null) return;
        var sessionId = GetSessionIdentifier(session);
        _sessionCache.TryRemove(sessionId, out _);
        OnPermissionsChanged?.Invoke(session);
    }

    private static string GetSessionIdentifier(object session)
    {
        if (session is IAstraAdminFacts facts)
        {
            return facts.UserId;
        }

        if (session is ICommonSession commonSession)
        {
            return commonSession.UserId.ToString();
        }

        return "unknown";
    }

    private static string GetSessionName(object session)
    {
        if (session is IAstraAdminFacts facts)
        {
            return facts.Name;
        }

        if (session is ICommonSession commonSession)
        {
            return commonSession.Name;
        }

        return GetSessionIdentifier(session);
    }

    private (uint Flags, string? Rank, bool IsSandbox) ExtractAdminMetadata(object session)
    {
        if (session is IAstraAdminFacts facts)
        {
            return (facts.AdminFlags, facts.Rank, facts.IsPlayerSandbox);
        }

        if (session is ICommonSession commonSession
            && _directory != null
            && _directory.TryGetAdmin(commonSession.UserId.ToString(), out var flags, out var rank, out var sandbox))
        {
            return (flags, rank, sandbox);
        }

        return (0, null, false);
    }
}
