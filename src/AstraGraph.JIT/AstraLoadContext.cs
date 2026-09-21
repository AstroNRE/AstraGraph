using System.Reflection;
using System.Runtime.Loader;

namespace AstraGraph.JIT;

/// <summary>
/// Collectible AssemblyLoadContext enabling zero-leak unloading of dynamically compiled JIT assemblies.
/// </summary>
public sealed class AstraLoadContext : AssemblyLoadContext
{
    public AstraLoadContext(string name) : base(name, isCollectible: true)
    {
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Fall back to default context for shared runtime and AstraGraph types
        return null;
    }
}
