using System.Text.RegularExpressions;

namespace AstraGraph.UI.Catalog;

/// <summary>
/// Reads style class names from a Robust stylesheet. The fork owns the stylesheet; Studio only lists the classes.
/// </summary>
public static partial class UiStyleCatalog
{
    public static IReadOnlyList<string> Scan(string? stylesheet)
    {
        if (string.IsNullOrWhiteSpace(stylesheet))
        {
            return [];
        }

        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Match match in ClassPattern().Matches(stylesheet))
        {
            var name = match.Groups["name"].Value;
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }
        }

        return names.ToArray();
    }

    public static void Register(UiControlCatalog catalog, string? stylesheet)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        foreach (var name in Scan(stylesheet))
        {
            catalog.RegisterStyleClass(name);
        }
    }

    [GeneratedRegex(@"(?<=\.)(?<name>[A-Za-z_][\w-]*)|(?<=StyleClass\s*\(\s*"")(?<name>[^""]+)")]
    private static partial Regex ClassPattern();
}
