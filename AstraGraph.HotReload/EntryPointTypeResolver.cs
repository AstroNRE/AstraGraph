using AstraGraph.Binding;
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

/// <summary>
/// Resolves graph event and component names from the indexed gameplay surface, then falls back to <see cref="Type.GetType(string)"/>.
/// </summary>
public sealed class CatalogEntryPointTypeResolver : IEntryPointTypeResolver
{
    private readonly BindingCatalog _catalog;

    public CatalogEntryPointTypeResolver(BindingCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public Type? Resolve(string typeName)
    {
        if (_catalog.TryGetNamedType(typeName, out var named) && named != null)
        {
            return named;
        }

        return ReflectionEntryPointTypeResolver.Instance.Resolve(typeName);
    }
}
