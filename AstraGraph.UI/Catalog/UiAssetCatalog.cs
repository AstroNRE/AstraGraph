namespace AstraGraph.UI.Catalog;

public sealed record UiAsset(string Kind, string Path, string? Label = null);

/// <summary>
/// Textures, sprites, fonts, and prototypes supplied by the running consumer.
/// Studio picks from this list instead of typing a resource path.
/// </summary>
public sealed class UiAssetCatalog
{
    private readonly List<UiAsset> _assets = [];

    public IReadOnlyList<UiAsset> Assets => _assets;

    public void Add(string kind, string path, string? label = null)
    {
        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (_assets.Any(item => item.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase) && item.Path.Equals(path, StringComparison.Ordinal)))
        {
            return;
        }

        _assets.Add(new UiAsset(kind, path, label ?? System.IO.Path.GetFileName(path)));
    }

    public IReadOnlyList<UiAsset> OfKind(string kind) =>
        _assets.Where(item => item.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase)).ToArray();
}
