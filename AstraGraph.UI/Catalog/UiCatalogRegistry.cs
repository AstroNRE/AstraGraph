using System.Reflection;

namespace AstraGraph.UI.Catalog;

/// <summary>
/// Process-wide UI catalog. Builtin controls are always present; a fork indexes its assemblies into the same catalog.
/// </summary>
public static class UiCatalogRegistry
{
    public static UiControlCatalog Shared { get; } = BuiltinUiCatalog.Create();

    public static void IndexAssembly(Assembly assembly, Type controlBaseType) =>
        Shared.IndexAssembly(assembly, controlBaseType);

    /// <summary>
    /// Indexes every supplied assembly against <c>Robust.Client.UserInterface.Control</c> when that base type is loaded.
    /// Missing client UI assemblies are skipped so a dedicated server can still boot.
    /// </summary>
    public static void IndexConsumerAssemblies(IReadOnlyList<Assembly>? assemblies)
    {
        if (assemblies == null || assemblies.Count == 0)
        {
            return;
        }

        Type? controlBase = null;
        foreach (var assembly in assemblies)
        {
            controlBase ??= FindControlBase(assembly);
        }

        if (controlBase == null)
        {
            return;
        }

        foreach (var assembly in assemblies)
        {
            try
            {
                Shared.IndexAssembly(assembly, controlBase);
            }
            catch (Exception ex) when (ex is ReflectionTypeLoadException or FileNotFoundException or TypeLoadException)
            {
            }
        }
    }

    private static Type? FindControlBase(Assembly assembly)
    {
        try
        {
            return assembly.GetType("Robust.Client.UserInterface.Control", throwOnError: false);
        }
        catch (Exception ex) when (ex is ReflectionTypeLoadException or FileNotFoundException or TypeLoadException)
        {
            return null;
        }
    }
}
