using System.Text;
using AstraGraph.Editor.Bridge;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class EmbeddedAssetProviderTests
{
    [Test]
    public void NormalizePath_NormalizesSlashesAndDefaults()
    {
        Assert.That(EmbeddedWebAssetProvider.NormalizePath("/"), Is.EqualTo("/index.html"));
        Assert.That(EmbeddedWebAssetProvider.NormalizePath(""), Is.EqualTo("/index.html"));
        Assert.That(EmbeddedWebAssetProvider.NormalizePath(@"\css\studio.css"), Is.EqualTo("/css/studio.css"));
        Assert.That(EmbeddedWebAssetProvider.NormalizePath("js/canvas.js/"), Is.EqualTo("/js/canvas.js"));
    }

    [Test]
    public void GetMimeType_ReturnsCorrectMimeTypes()
    {
        Assert.That(EmbeddedWebAssetProvider.GetMimeType("index.html"), Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(EmbeddedWebAssetProvider.GetMimeType("studio.css"), Is.EqualTo("text/css; charset=utf-8"));
        Assert.That(EmbeddedWebAssetProvider.GetMimeType("transport.js"), Is.EqualTo("application/javascript; charset=utf-8"));
        Assert.That(EmbeddedWebAssetProvider.GetMimeType("data.json"), Is.EqualTo("application/json; charset=utf-8"));
        Assert.That(EmbeddedWebAssetProvider.GetMimeType("icon.svg"), Is.EqualTo("image/svg+xml"));
        Assert.That(EmbeddedWebAssetProvider.GetMimeType("logo.png"), Is.EqualTo("image/png"));
        Assert.That(EmbeddedWebAssetProvider.GetMimeType("font.woff2"), Is.EqualTo("font/woff2"));
        Assert.That(EmbeddedWebAssetProvider.GetMimeType("unknown.xyz"), Is.EqualTo("application/octet-stream"));
    }

    [Test]
    public void RegisterAsset_CanRetrieveRegisteredAssetWithEtag()
    {
        var provider = new EmbeddedWebAssetProvider();
        provider.RegisterTextAsset("/test.txt", "text/plain", "Hello Astra!");

        var asset = provider.TryGetAsset("/test.txt");
        Assert.That(asset, Is.Not.Null);
        Assert.That(asset!.Path, Is.EqualTo("/test.txt"));
        Assert.That(asset.ContentType, Is.EqualTo("text/plain"));
        Assert.That(Encoding.UTF8.GetString(asset.Content), Is.EqualTo("Hello Astra!"));
        Assert.That(asset.ETag, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void CreateWithDefaultStudio_HasAllEssentialStudioAssets()
    {
        var provider = EmbeddedWebAssetProvider.CreateWithDefaultStudio();

        var indexHtml = provider.TryGetAsset("/index.html");
        Assert.That(indexHtml, Is.Not.Null);
        Assert.That(indexHtml!.ContentType, Does.Contain("text/html"));
        Assert.That(Encoding.UTF8.GetString(indexHtml.Content), Does.Contain("Astra Studio Web"));

        var css = provider.TryGetAsset("/css/studio.css");
        Assert.That(css, Is.Not.Null);
        Assert.That(css!.ContentType, Does.Contain("text/css"));

        var transport = provider.TryGetAsset("/js/transport.js");
        Assert.That(transport, Is.Not.Null);
        Assert.That(transport!.ContentType, Does.Contain("javascript"));

        var canvas = provider.TryGetAsset("/js/canvas.js");
        Assert.That(canvas, Is.Not.Null);

        var studioJs = provider.TryGetAsset("/js/studio.js");
        Assert.That(studioJs, Is.Not.Null);
    }

    [Test]
    public void SpaFallback_NonAssetPath_FallsBackToIndexHtml()
    {
        var provider = EmbeddedWebAssetProvider.CreateWithDefaultStudio();

        // Path without file extension (SPA client-side route)
        var asset = provider.TryGetAsset("/graph/editor/my-graph");
        Assert.That(asset, Is.Not.Null);
        Assert.That(asset!.Path, Is.EqualTo("/index.html"));

        // API paths must NOT fall back to index.html
        var apiAsset = provider.TryGetAsset("/api/auth/validate-nonce");
        Assert.That(apiAsset, Is.Null);
    }
}
