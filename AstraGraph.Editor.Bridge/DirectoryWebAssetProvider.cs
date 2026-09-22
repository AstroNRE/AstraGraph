namespace AstraGraph.Editor.Bridge;

/// <summary>
/// Serves Astra Studio files from a directory on the loopback bridge.
/// </summary>
public sealed class DirectoryWebAssetProvider : IWebAssetProvider
{
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".svg"] = "image/svg+xml"
    };

    private readonly string _root;

    public DirectoryWebAssetProvider(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
    }

    public WebAsset? TryGetAsset(string path)
    {
        var relative = (path ?? "/").Trim();
        if (string.IsNullOrEmpty(relative) || relative == "/")
        {
            relative = "/index.html";
        }

        relative = relative.Replace('\\', '/');
        if (relative.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        var full = Path.GetFullPath(Path.Combine(_root, relative.TrimStart('/')));
        var rootWithSep = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootWithSep, StringComparison.Ordinal) && !string.Equals(full, _root, StringComparison.Ordinal))
        {
            return null;
        }

        if (!File.Exists(full))
        {
            return null;
        }

        var ext = Path.GetExtension(full);
        var contentType = ContentTypes.GetValueOrDefault(ext, "application/octet-stream");
        return new WebAsset(relative, contentType, File.ReadAllBytes(full));
    }
}
