using AstraGraph.Binding;
using AstraGraph.Core;

namespace AstraGraph.Runtime.Security;

/// <summary>
/// Bitflag constants for Space Station 14 AdminFlags integration.
/// </summary>
public static class SS14AdminFlagsConstants
{
    // The bit allocated in Content.Shared for AstraGraph access gate
    public const uint AdminFlagAstraGraph = 1u << 24;
}

public sealed class AstraAccessResolutionResult
{
    public bool IsAllowed { get; init; }
    public string? RejectionReason { get; init; }
    public AstraUser? User { get; init; }
}

/// <summary>
/// Resolves SS14 admin sessions and player identities into effective fine-grained AstraGraph permissions.
/// Enforces separation between full engine developers, content developers, and player sandboxes.
/// </summary>
public sealed class AstraAdminPermissionResolver
{
    private readonly uint _requiredAdminFlag;

    public AstraAdminPermissionResolver(uint requiredAdminFlag = SS14AdminFlagsConstants.AdminFlagAstraGraph)
    {
        _requiredAdminFlag = requiredAdminFlag;
    }

    /// <summary>
    /// Resolves an SS14 user session against admin flags and role string.
    /// </summary>
    public AstraAccessResolutionResult ResolveSession(
        string userId,
        string userName,
        uint userAdminFlags,
        string? adminRank = null,
        bool isPlayerSandbox = false,
        string? sandboxEntityId = null)
    {
        // 1. Check if user has the SS14-level AstraGraph admin flag (unless in designated Player Sandbox mode)
        bool hasAdminFlag = (userAdminFlags & _requiredAdminFlag) == _requiredAdminFlag;

        if (!hasAdminFlag && !isPlayerSandbox)
        {
            return new AstraAccessResolutionResult
            {
                IsAllowed = false,
                RejectionReason = "User lacks required AdminFlags.AstraGraph flag."
            };
        }

        // 2. Resolve Role Profile
        if (isPlayerSandbox)
        {
            // Player sandbox: limited to editing within entity scope, no shared publishing, no engine calls
            var sandboxUser = new AstraUser(
                Id: userId,
                Name: userName,
                Permissions: AstraPermission.ViewGraphs | AstraPermission.EditDrafts | AstraPermission.Compile,
                Profile: SecurityProfile.Gameplay);

            return new AstraAccessResolutionResult
            {
                IsAllowed = true,
                User = sandboxUser
            };
        }

        // 3. Admin / Developer Ranks
        var rankLower = (adminRank ?? "").ToLowerInvariant();

        if (rankLower.Contains("head") || rankLower.Contains("lead") || rankLower.Contains("maintainer"))
        {
            // Full Admin with Engine Profile access
            var adminUser = new AstraUser(
                Id: userId,
                Name: userName,
                Permissions: AstraPermission.Admin,
                Profile: SecurityProfile.Engine);

            return new AstraAccessResolutionResult
            {
                IsAllowed = true,
                User = adminUser
            };
        }

        // Standard Content Developer
        var devPermissions = AstraPermission.ViewGraphs |
                             AstraPermission.EditDrafts |
                             AstraPermission.Compile |
                             AstraPermission.PublishServer |
                             AstraPermission.PublishShared |
                             AstraPermission.Rollback |
                             AstraPermission.Debug |
                             AstraPermission.InspectState;

        var devUser = new AstraUser(
            Id: userId,
            Name: userName,
            Permissions: devPermissions,
            Profile: SecurityProfile.Gameplay);

        return new AstraAccessResolutionResult
        {
            IsAllowed = true,
            User = devUser
        };
    }
}
