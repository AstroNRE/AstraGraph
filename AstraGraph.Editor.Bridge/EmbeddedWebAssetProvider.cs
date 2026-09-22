using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace AstraGraph.Editor.Bridge;

/// <summary>
/// Web asset provider that serves assets from assembly embedded resources and dynamic in-memory registrations.
/// Supports automatic MIME type resolution, ETag caching and SPA fallback to /index.html.
/// </summary>
public sealed class EmbeddedWebAssetProvider : IWebAssetProvider
{
    private readonly Assembly _assembly;
    private readonly string _resourcePrefix;
    private readonly bool _spaFallback;
    private readonly ConcurrentDictionary<string, WebAsset> _registeredAssets = new(StringComparer.OrdinalIgnoreCase);

    public EmbeddedWebAssetProvider(
        Assembly? assembly = null,
        string resourcePrefix = "AstraGraph.Editor.Bridge.Assets",
        bool spaFallback = true)
    {
        _assembly = assembly ?? typeof(EmbeddedWebAssetProvider).Assembly;
        _resourcePrefix = resourcePrefix.TrimEnd('.');
        _spaFallback = spaFallback;
    }

    public void RegisterAsset(string path, string contentType, byte[] content, string? etag = null)
    {
        var normalized = NormalizePath(path);
        etag ??= ComputeETag(content);
        _registeredAssets[normalized] = new WebAsset(normalized, contentType, content, etag);
    }

    public void RegisterTextAsset(string path, string contentType, string text)
    {
        RegisterAsset(path, contentType, Encoding.UTF8.GetBytes(text));
    }

    public WebAsset? TryGetAsset(string path)
    {
        var normalized = NormalizePath(path);

        // 1. Check dynamic/registered cache
        if (_registeredAssets.TryGetValue(normalized, out var asset))
            return asset;

        // 2. Try loading from assembly embedded resource
        asset = TryLoadFromEmbeddedResource(normalized);
        if (asset != null)
        {
            _registeredAssets[normalized] = asset;
            return asset;
        }

        // 3. SPA Fallback: if not an asset file with dot extension and not an API call, serve /index.html
        if (_spaFallback && !normalized.Contains('.') && !normalized.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            return TryGetAsset("/index.html");
        }

        return null;
    }

    private WebAsset? TryLoadFromEmbeddedResource(string normalizedPath)
    {
        // Convert normalized path "/css/studio.css" -> "AstraGraph.Editor.Bridge.Assets.css.studio.css"
        string subPath = normalizedPath.TrimStart('/').Replace('/', '.').Replace('\\', '.');
        string fullResourceName = $"{_resourcePrefix}.{subPath}";

        using var stream = _assembly.GetManifestResourceStream(fullResourceName);
        if (stream == null)
        {
            // Case-insensitive fallback among resource names
            var allNames = _assembly.GetManifestResourceNames();
            var matched = allNames.FirstOrDefault(n => string.Equals(n, fullResourceName, StringComparison.OrdinalIgnoreCase));
            if (matched == null)
                return null;

            using var fallbackStream = _assembly.GetManifestResourceStream(matched);
            if (fallbackStream == null) return null;
            return ReadStreamToAsset(normalizedPath, fallbackStream);
        }

        return ReadStreamToAsset(normalizedPath, stream);
    }

    private static WebAsset ReadStreamToAsset(string normalizedPath, Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        byte[] bytes = ms.ToArray();
        string contentType = GetMimeType(normalizedPath);
        string etag = ComputeETag(bytes);
        return new WebAsset(normalizedPath, contentType, bytes, etag);
    }

    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
            return "/index.html";

        string normalized = path.Replace('\\', '/');
        if (!normalized.StartsWith('/'))
            normalized = "/" + normalized;

        return normalized.TrimEnd('/');
    }

    public static string GetMimeType(string path)
    {
        int extIndex = path.LastIndexOf('.');
        if (extIndex < 0)
            return "application/octet-stream";

        string ext = path[extIndex..].ToLowerInvariant();
        return ext switch
        {
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" or ".mjs" => "application/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            ".woff2" => "font/woff2",
            ".woff" => "font/woff",
            ".ttf" => "font/ttf",
            ".txt" => "text/plain; charset=utf-8",
            _ => "application/octet-stream"
        };
    }

    public static string ComputeETag(byte[] content)
    {
        var hash = SHA256.HashData(content);
        return "\"" + Convert.ToHexString(hash)[..16].ToLowerInvariant() + "\"";
    }

    /// <summary>
    /// Creates a default provider pre-populated with default studio frontend assets.
    /// </summary>
    public static EmbeddedWebAssetProvider CreateWithDefaultStudio()
    {
        var provider = new EmbeddedWebAssetProvider();
        var root = FindStudioWebRoot();
        if (root != null)
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var relative = "/" + Path.GetRelativePath(root, file).Replace('\\', '/');
                provider.RegisterAsset(relative, GetMimeType(file), File.ReadAllBytes(file));
            }

            return provider;
        }

        return provider;
    }

    public static string? FindStudioWebRoot()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
        if (File.Exists(bundled))
        {
            return Path.GetDirectoryName(bundled);
        }

        var cursor = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 8 && cursor != null; depth++)
        {
            var candidate = Path.Combine(cursor.FullName, "AstraGraph.StudioWeb", "wwwroot", "index.html");
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(candidate);
            }

            cursor = cursor.Parent;
        }

        return null;
    }
}
