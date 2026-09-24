using System.Reflection;
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
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        if (_catalog.TryGetNamedType(typeName, out var named) && named != null)
        {
            return named;
        }

        var reflected = ReflectionEntryPointTypeResolver.Instance.Resolve(typeName);
        if (reflected != null)
        {
            return reflected;
        }

        return ShortNames().TryGetValue(typeName, out var found) ? found : null;
    }

    private Dictionary<string, Type>? _shortNames;

    private Dictionary<string, Type> ShortNames()
    {
        if (_shortNames != null)
        {
            return _shortNames;
        }

        var map = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
            {
                continue;
            }

            Type[] types;
            try
            {
                types = assembly.GetExportedTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.OfType<Type>().ToArray();
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var type in types)
            {
                if (type is { IsPublic: true, IsGenericTypeDefinition: false })
                {
                    map.TryAdd(type.Name, type);
                }
            }
        }

        _shortNames = map;
        return map;
    }
}
