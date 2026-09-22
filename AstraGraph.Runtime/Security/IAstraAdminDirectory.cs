namespace AstraGraph.Runtime.Security;

/// <summary>
/// Content-side lookup over the fork's admin manager.
/// AstraGraph does not reference Content admin types.
/// </summary>
public interface IAstraAdminDirectory
{
    bool TryGetAdmin(string userId, out uint adminFlags, out string? rank, out bool isSandbox);
}
