using System.Collections.Concurrent;
using System.Reflection;
using AstraGraph.Runtime.Security;
using Robust.Shared.Player;

namespace AstraGraph.Robust.Server;

/// <summary>
/// Connects AstraGraph's RBAC security model to RobustToolbox's session management
/// and SS14's AdminFlags.AstraGraph (1u &lt;&lt; 24) gate.
/// </summary>
public sealed class RobustAdminPermissionProvider : IAstraPermissionProvider
{
    private readonly AstraAdminPermissionResolver _resolver;
    private readonly ConcurrentDictionary<string, AstraUser> _sessionCache = new();

    public RobustAdminPermissionProvider(uint requiredAdminFlag = SS14AdminFlagsConstants.AdminFlagAstraGraph)
    {
        _resolver = new AstraAdminPermissionResolver(requiredAdminFlag);
    }

    public bool HasAccess(object playerSession)
    {
        return ResolveUser(playerSession) != null;
    }

    public AstraUser? ResolveUser(object playerSession)
    {
        if (playerSession == null) return null;

        string sessionId = GetSessionIdentifier(playerSession);
        if (_sessionCache.TryGetValue(sessionId, out var cachedUser))
        {
            return cachedUser;
        }

        // 1. Extract Admin Flags and Rank from session via reflection
        var (adminFlags, rank, isSandbox) = ExtractAdminMetadata(playerSession);

        // 2. Resolve via AstraAdminPermissionResolver
        var resolution = _resolver.ResolveSession(sessionId, sessionId, adminFlags, rank, isSandbox);
        if (!resolution.IsAllowed || resolution.User == null)
        {
            return null;
        }

        _sessionCache[sessionId] = resolution.User;
        return resolution.User;
    }

    /// <summary>
    /// Invalidates cached permissions upon deadmin or admin rank changes.
    /// </summary>
    public void InvalidateSession(object playerSession)
    {
        if (playerSession == null) return;
        var sessionId = GetSessionIdentifier(playerSession);
        _sessionCache.TryRemove(sessionId, out _);
    }

    private static string GetSessionIdentifier(object session)
    {
        if (session is ICommonSession commonSession)
        {
            return commonSession.UserId.ToString();
        }

        var prop = session.GetType().GetProperty("UserId") ?? session.GetType().GetProperty("Name");
        return prop?.GetValue(session)?.ToString() ?? session.ToString() ?? "unknown";
    }

    private static (uint Flags, string? Rank, bool IsSandbox) ExtractAdminMetadata(object session)
    {
        uint flags = 0;
        string? rank = null;
        var isSandbox = false;

        var type = session.GetType();

        // Check for AdminData or AdminFlags property
        var adminDataProp = type.GetProperty("AdminData");
        var targetObj = adminDataProp?.GetValue(session) ?? session;
        var targetType = targetObj.GetType();

        var flagsProp = targetType.GetProperty("AdminFlags") ?? targetType.GetProperty("Flags");
        if (flagsProp != null)
        {
            var rawFlags = flagsProp.GetValue(targetObj);
            if (rawFlags is uint u) flags = u;
            else if (rawFlags is int i) flags = (uint)i;
            else if (rawFlags is Enum e) flags = Convert.ToUInt32(e);
        }

        var titleProp = targetType.GetProperty("Title") ?? targetType.GetProperty("Rank");
        if (titleProp != null)
        {
            rank = titleProp.GetValue(targetObj)?.ToString();
        }

        var sandboxProp = targetType.GetProperty("IsPlayerSandbox") ?? targetType.GetProperty("Sandbox");
        if (sandboxProp != null && sandboxProp.GetValue(targetObj) is bool b)
        {
            isSandbox = b;
        }

        return (flags, rank, isSandbox);
    }
}
