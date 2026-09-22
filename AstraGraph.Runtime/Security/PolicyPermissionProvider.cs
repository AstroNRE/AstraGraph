using AstraGraph.Binding;
using AstraGraph.Core;

namespace AstraGraph.Runtime.Security;

/// <summary>
/// In-memory permission provider for local authoring and tests.
/// Session keys are user id strings.
/// </summary>
public sealed class PolicyPermissionProvider : IAstraPermissionProvider
{
    private readonly Dictionary<string, AstraPermission> _permissions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SecurityProfile> _profiles = new(StringComparer.Ordinal);
    private readonly HashSet<string> _gated = new(StringComparer.Ordinal);
    private bool _failClosed;

    public event Action<object>? OnPermissionsChanged;

    public static PolicyPermissionProvider AllowLocalAuthor(string userId)
    {
        var provider = new PolicyPermissionProvider();
        provider.GrantGate(userId);
        provider.SetPermissions(userId, AstraPermission.Admin);
        provider.SetProfile(userId, SecurityProfile.Gameplay);
        return provider;
    }

    public void GrantGate(string userId) => _gated.Add(userId);

    public void SetPermissions(string userId, AstraPermission permissions) => _permissions[userId] = permissions;

    public void SetProfile(string userId, SecurityProfile profile) => _profiles[userId] = profile;

    public void FailClosedNow()
    {
        _failClosed = true;
        OnPermissionsChanged?.Invoke("*");
    }

    public bool CanEnterAstra(object session) => !_failClosed && _gated.Contains(Key(session));

    public AstraUser? ResolveUser(object session)
    {
        var id = Key(session);
        if (!CanEnterAstra(session))
        {
            return null;
        }

        return new AstraUser(id, id, GetEffectivePermissions(session), GetSecurityProfile(session));
    }

    public AstraPermission GetEffectivePermissions(object session)
    {
        if (!CanEnterAstra(session))
        {
            return AstraPermission.None;
        }

        return _permissions.GetValueOrDefault(Key(session), AstraPermission.ViewGraphs);
    }

    public bool HasPermission(object session, AstraPermission permission) =>
        GetEffectivePermissions(session).HasFlag(permission);

    public SecurityProfile GetSecurityProfile(object session) =>
        CanEnterAstra(session)
            ? _profiles.GetValueOrDefault(Key(session), SecurityProfile.Gameplay)
            : SecurityProfile.Gameplay;

    public void InvalidateSession(object session)
    {
        _gated.Remove(Key(session));
        OnPermissionsChanged?.Invoke(session);
    }

    private static string Key(object session) => session as string ?? session.ToString() ?? string.Empty;
}
