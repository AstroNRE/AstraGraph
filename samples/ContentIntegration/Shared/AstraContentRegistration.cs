using AstraGraph.HotReload;
using AstraGraph.Persistence;
using AstraGraph.Runtime;
using AstraGraph.Runtime.Security;

namespace Content.Integration;

public static class AstraContentRegistration
{
    public const string ResourceDirectory = "Resources/AstraGraph";
    public const string DataDirectory = "data/AstraGraph";

    public static AstraGraphFacade CreateFacade(
        AstraGraphHost host,
        string resourceRoot,
        string dataRoot,
        IAstraPermissionProvider permissions)
    {
        var layout = new StorageLayout(
            Path.Combine(resourceRoot, "AstraGraph"),
            Path.Combine(dataRoot, "AstraGraph"));
        return AstraGraphFacade.ForServer(host, layout, permissions);
    }
}
