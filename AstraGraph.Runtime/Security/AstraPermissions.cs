using System;
using AstraGraph.Binding;
using AstraGraph.Core;

namespace AstraGraph.Runtime.Security;

/// <summary>
/// Fine-grained Role-Based Access Control (RBAC) permissions for AstraGraph users and administrators.
/// </summary>
[Flags]
public enum AstraPermission : uint
{
    None = 0,
    ViewGraphs = 1 << 0,
    EditDrafts = 1 << 1,
    Compile = 1 << 2,
    PublishServer = 1 << 3,
    PublishShared = 1 << 4,
    Rollback = 1 << 5,
    Debug = 1 << 6,
    InspectState = 1 << 7,
    ModifyPersistentState = 1 << 8,
    ManageBindings = 1 << 9,
    UseEngineProfile = 1 << 10,
    Admin = 0xFFFFFFFF
}

/// <summary>
/// Authenticated user identity in the AstraGraph authoring session.
/// </summary>
public sealed record AstraUser(
    string Id,
    string Name,
    AstraPermission Permissions,
    SecurityProfile Profile);

/// <summary>
/// Central authorization service validating caller permissions against gameplay operations and graph sides.
/// </summary>
public static class AstraAuthorizationService
{
    public static bool HasPermission(AstraUser user, AstraPermission requiredPermission)
    {
        ArgumentNullException.ThrowIfNull(user);
        return (user.Permissions & requiredPermission) == requiredPermission ||
               (user.Permissions & AstraPermission.Admin) == AstraPermission.Admin;
    }

    public static bool CanEditDraft(AstraUser user) => HasPermission(user, AstraPermission.EditDrafts);

    public static bool CanCompile(AstraUser user) => HasPermission(user, AstraPermission.Compile);

    public static bool CanDebug(AstraUser user) => HasPermission(user, AstraPermission.Debug);

    public static bool CanRollback(AstraUser user) => HasPermission(user, AstraPermission.Rollback);

    public static bool CanModifyPersistentState(AstraUser user) => HasPermission(user, AstraPermission.ModifyPersistentState);

    public static bool CanPublish(AstraUser user, GraphSide side, SecurityProfile graphSecurityProfile)
    {
        ArgumentNullException.ThrowIfNull(user);

        // 1. Check side permissions: Shared/Predicted requires higher clearance
        if (side is GraphSide.Shared or GraphSide.SharedPredicted)
        {
            if (!HasPermission(user, AstraPermission.PublishShared))
                return false;
        }
        else
        {
            if (!HasPermission(user, AstraPermission.PublishServer))
                return false;
        }

        // 2. Check security profile
        if (graphSecurityProfile == SecurityProfile.Engine &&
            !HasPermission(user, AstraPermission.UseEngineProfile))
        {
            return false;
        }

        return AccessPolicy.IsAccessAllowed(graphSecurityProfile, user.Profile);
    }
}

/// <summary>
/// Service provider interface resolving caller sessions (e.g. Robust ICommonSession) into authenticated AstraUsers.
/// </summary>
public interface IAstraPermissionProvider
{
    AstraUser? ResolveUser(object playerSession);
    bool HasAccess(object playerSession);
}

