using AstraGraph.Editor.Protocol;
using AstraGraph.Robust.Client;

namespace Content.Integration;

public static class AstraClientRegistration
{
    public const string SystemName = ClientRegistration.SystemName;

    public static Task<string> OpenStudioAsync(ClientAstraGraphSystem client, IAuthoringMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(handler);
        return client.LaunchStudioAsync(handler);
    }
}
