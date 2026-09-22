using System.Security.Cryptography;
using AstraGraph.Binding;
using AstraGraph.Editor.Bridge;
using AstraGraph.Runtime.Security;

namespace AstraGraph.Studio.DevHost;

/// <summary>
/// Maps development role names onto the existing Astra permission flags.
/// </summary>
public static class DevAuthenticationService
{
    public const string UserId = "astra-dev";

    public static readonly AstraPermission DeveloperPermissions =
        AstraPermission.ViewGraphs
        | AstraPermission.EditDrafts
        | AstraPermission.Compile
        | AstraPermission.Debug
        | AstraPermission.InspectState
        | AstraPermission.Rollback;

    public static string CreateToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static AstraPermission ResolvePermissions(string? roles)
    {
        var permissions = AstraPermission.None;
        var sawRole = false;
        foreach (var part in (roles ?? "developer").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "developer":
                    sawRole = true;
                    permissions |= DeveloperPermissions;
                    break;
                case "publisher":
                    sawRole = true;
                    permissions |= DeveloperPermissions | AstraPermission.PublishServer | AstraPermission.PublishShared;
                    break;
                case "maintainer":
                    sawRole = true;
                    permissions |= DeveloperPermissions
                        | AstraPermission.PublishServer
                        | AstraPermission.PublishShared
                        | AstraPermission.ManageBindings
                        | AstraPermission.UseEngineProfile
                        | AstraPermission.ModifyPersistentState;
                    break;
            }
        }

        return sawRole ? permissions : DeveloperPermissions;
    }

    public static SecurityProfile ResolveProfile(string? roles)
    {
        var text = roles ?? string.Empty;
        if (text.Contains("maintainer", StringComparison.OrdinalIgnoreCase))
        {
            return SecurityProfile.Engine;
        }

        if (text.Contains("publisher", StringComparison.OrdinalIgnoreCase))
        {
            return SecurityProfile.Trusted;
        }

        return SecurityProfile.Gameplay;
    }

    public static AstraUser CreateUser(string? roles)
    {
        return new AstraUser(UserId, "Astra Developer", ResolvePermissions(roles), ResolveProfile(roles));
    }

    public static bool TokenMatches(string expected, string? presented) =>
        BridgeSecurityPolicy.ValidateTokenConstantTime(expected, presented);
}
