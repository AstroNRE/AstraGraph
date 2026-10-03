using Content.AstraGraph.Editor.Protocol;
using Content.AstraGraph.Runtime.Security;

namespace Content.AstraGraph.Robust.Server;

public sealed record AuthoringLaunchToken(
    string Token,
    string UserId,
    string UserName,
    AstraPermission Permissions,
    DateTimeOffset ExpiresAtUtc);

public interface IAstraAuthoringService
{
    AuthoringServerSession Session { get; }
    IAuthoringMessageHandler MessageHandler { get; }
    bool CanEnterAstra(object session);
    AuthoringLaunchToken? IssueLaunchToken(object session, TimeSpan? lifetime = null);
    bool TryValidateToken(string token, out AstraUser? user);
    void SyncDiscoveredGraphs();
}
