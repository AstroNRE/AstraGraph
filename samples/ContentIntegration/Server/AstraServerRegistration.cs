using AstraGraph.Robust.Server;
using AstraGraph.Runtime.Security;

namespace Content.Integration;

public static class AstraServerRegistration
{
    public const string SystemName = ServerRegistration.SystemName;

    public static void Start(ServerAstraGraphSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        system.EnsureInitialized();
        system.ExecuteBootstrap();
    }

    public static AuthoringLaunchToken? IssueStudioToken(ServerAstraGraphSystem system, object session)
    {
        ArgumentNullException.ThrowIfNull(system);
        system.EnsureInitialized();
        return system.AuthoringService.IssueLaunchToken(session);
    }
}

public sealed class ContentAdminDirectory : IAstraAdminDirectory
{
    private readonly Func<string, (uint Flags, string? Rank, bool Sandbox)?> _lookup;

    public ContentAdminDirectory(Func<string, (uint Flags, string? Rank, bool Sandbox)?> lookup)
    {
        _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
    }

    public bool TryGetAdmin(string userId, out uint adminFlags, out string? rank, out bool isSandbox)
    {
        var found = _lookup(userId);
        if (found == null)
        {
            adminFlags = 0;
            rank = null;
            isSandbox = false;
            return false;
        }

        adminFlags = found.Value.Flags;
        rank = found.Value.Rank;
        isSandbox = found.Value.Sandbox;
        return true;
    }
}
