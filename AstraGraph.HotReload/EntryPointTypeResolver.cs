using AstraGraph.Core;

namespace AstraGraph.HotReload;

public interface IEntryPointTypeResolver
{
    Type? Resolve(string typeName);
}

public sealed class ReflectionEntryPointTypeResolver : IEntryPointTypeResolver
{
    public static ReflectionEntryPointTypeResolver Instance { get; } = new();

    public Type? Resolve(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        return Type.GetType(typeName, throwOnError: false);
    }
}
