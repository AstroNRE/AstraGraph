using AstraGraph.Editor.Bridge;
using AstraGraph.Editor.InGame;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
[Category("Bridge.W3")]
public sealed class DeepLinkAndInGameTests
{
    // ─── StudioDeepLinkUrlBuilder ──────────────────────────────────────────

    [Test]
    public void DeepLink_Build_ContainsAction()
    {
        var ctx = new StudioDeepLinkContext { Action = StudioAction.InspectEntity, EntityUid = "42:3" };
        string url = StudioDeepLinkUrlBuilder.Build("http://127.0.0.1:1234/?nonce=abc", ctx);
        Assert.That(url, Does.Contain("action=inspectentity"));
        Assert.That(url, Does.Contain("entity=42%3A3"));
    }

    [Test]
    public void DeepLink_Build_AllParameters()
    {
        var ctx = new StudioDeepLinkContext
        {
            Action = StudioAction.OpenRuntimeError,
            EntityUid = "ent-1",
            GraphId = "graph-weapon",
            NodeId = "node-7F3A",
            PinId = "pin-out",
            Revision = "53",
            DiagnosticCode = "AG0042",
            ExecutionTick = 12345
        };
        string url = StudioDeepLinkUrlBuilder.Build("http://127.0.0.1:1234/?nonce=xyz", ctx);
        Assert.That(url, Does.Contain("entity=ent-1"));
        Assert.That(url, Does.Contain("graph=graph-weapon"));
        Assert.That(url, Does.Contain("node=node-7F3A"));
        Assert.That(url, Does.Contain("pin=pin-out"));
        Assert.That(url, Does.Contain("revision=53"));
        Assert.That(url, Does.Contain("error=AG0042"));
        Assert.That(url, Does.Contain("tick=12345"));
    }

    [Test]
    public void DeepLink_Build_NullOptionalFields_OmitsThem()
    {
        var ctx = new StudioDeepLinkContext { Action = StudioAction.OpenStudio };
        string url = StudioDeepLinkUrlBuilder.Build("http://127.0.0.1:1234/?nonce=abc", ctx);
        Assert.That(url, Does.Not.Contain("entity="));
        Assert.That(url, Does.Not.Contain("graph="));
        Assert.That(url, Does.Not.Contain("node="));
    }

    [Test]
    public void DeepLink_Build_ThrowsOnNullOrEmpty()
    {
        var ctx = new StudioDeepLinkContext();
        Assert.Throws<ArgumentException>(() => StudioDeepLinkUrlBuilder.Build("", ctx));
        Assert.Throws<ArgumentNullException>(() => StudioDeepLinkUrlBuilder.Build("http://x", null!));
    }

    // ─── StudioDeepLinkUrlBuilder – Parse ─────────────────────────────────

    [Test]
    public void DeepLink_Parse_RoundTrip()
    {
        var original = new StudioDeepLinkContext
        {
            Action = StudioAction.OpenNode,
            GraphId = "graph-abc",
            NodeId = "node-123",
            EntityUid = "ent-42"
        };

        string url = StudioDeepLinkUrlBuilder.Build("http://127.0.0.1:1234/?nonce=tok", original);
        var parsed = StudioDeepLinkUrlBuilder.Parse(url);

        Assert.That(parsed, Is.Not.Null);
        Assert.That(parsed!.Action, Is.EqualTo(StudioAction.OpenNode));
        Assert.That(parsed.GraphId, Is.EqualTo("graph-abc"));
        Assert.That(parsed.NodeId, Is.EqualTo("node-123"));
        Assert.That(parsed.EntityUid, Is.EqualTo("ent-42"));
    }

    [Test]
    public void DeepLink_Parse_DefaultsToOpenStudio_WhenNoAction()
    {
        var parsed = StudioDeepLinkUrlBuilder.Parse("http://127.0.0.1:1234/?nonce=abc");
        Assert.That(parsed, Is.Not.Null);
        Assert.That(parsed!.Action, Is.EqualTo(StudioAction.OpenStudio));
    }

    [Test]
    public void DeepLink_Parse_ReturnsNull_ForInvalidUrl()
    {
        Assert.That(StudioDeepLinkUrlBuilder.Parse(null!), Is.Null);
        Assert.That(StudioDeepLinkUrlBuilder.Parse(""), Is.Null);
        Assert.That(StudioDeepLinkUrlBuilder.Parse("not_a_url"), Is.Null);
    }

    [Test]
    public void DeepLink_Parse_ParsesTick()
    {
        string url = "http://127.0.0.1:1234/?nonce=abc&action=openruntimeerror&tick=9999";
        var parsed = StudioDeepLinkUrlBuilder.Parse(url);
        Assert.That(parsed?.ExecutionTick, Is.EqualTo(9999L));
    }

    // ─── Sanitization ─────────────────────────────────────────────────────

    [Test]
    public void DeepLink_Build_EscapesSpecialCharsInValues()
    {
        var ctx = new StudioDeepLinkContext
        {
            Action = StudioAction.InspectEntity,
            EntityUid = "uid with spaces"
        };
        string url = StudioDeepLinkUrlBuilder.Build("http://127.0.0.1:1234/?nonce=x", ctx);
        Assert.That(url, Does.Not.Contain(" "));
    }

    [Test]
    public void DeepLink_Parse_Sanitize_RemovesInjectionChars()
    {
        // Attempt injection via entity param
        string url = "http://127.0.0.1:1234/?nonce=abc&entity=<script>alert(1)</script>&action=openstudio";
        var parsed = StudioDeepLinkUrlBuilder.Parse(url);
        // After sanitization, injection chars are stripped
        Assert.That(parsed?.EntityUid, Does.Not.Contain("<"));
        Assert.That(parsed?.EntityUid, Does.Not.Contain(">"));
    }

    // ─── AstraRuntimeStatusReporter ───────────────────────────────────────

    [Test]
    public async Task RuntimeStatusReporter_ReflectsBridgeState()
    {
        var nonceMgr = new SessionNonceManager();
        var security = new BridgeSecurityPolicy();

        await using var bridge = new AstraLocalBridge(
            nonceMgr, security, new NullAssetProvider(),
            (ctx, ct) => Task.CompletedTask);

        var reporter = new AstraRuntimeStatusReporter(bridge);

        var statusBefore = reporter.GetStatus();
        Assert.That(statusBefore.BridgeActive, Is.False);

        await bridge.StartAsync();
        var statusRunning = reporter.GetStatus();
        Assert.That(statusRunning.BridgeActive, Is.True);
        Assert.That(statusRunning.BridgePort, Is.GreaterThan(0));
        Assert.That(statusRunning.BridgeStartedAt, Is.Not.Null);
    }

    [Test]
    public void RuntimeStatusReporter_RecordsAndClearsError()
    {
        var nonceMgr = new SessionNonceManager();
        var security = new BridgeSecurityPolicy();

        using var bridge = new AstraLocalBridge(
            nonceMgr, security, new NullAssetProvider(),
            (ctx, ct) => Task.CompletedTask);

        var reporter = new AstraRuntimeStatusReporter(bridge);

        reporter.RecordError("something went wrong");
        Assert.That(reporter.GetStatus().LastError, Is.EqualTo("something went wrong"));

        reporter.ClearError();
        Assert.That(reporter.GetStatus().LastError, Is.Null);
    }

    // ─── AstraInGameLauncher – URL building (no browser open) ─────────────

    [Test]
    public async Task InGameLauncher_BuildUrl_InspectEntity_ContainsEntityParam()
    {
        var nonceMgr = new SessionNonceManager();
        var security = new BridgeSecurityPolicy();

        await using var bridge = new AstraLocalBridge(
            nonceMgr, security, new NullAssetProvider(),
            (ctx, ct) => Task.CompletedTask);

        await bridge.StartAsync();

        var reporter = new AstraRuntimeStatusReporter(bridge);
        var launcher = new AstraInGameLauncher(bridge, reporter);

        var ctx = new StudioDeepLinkContext
        {
            Action = StudioAction.InspectEntity,
            EntityUid = "ent-99"
        };
        string url = launcher.BuildUrl(ctx);

        Assert.That(url, Does.StartWith($"http://127.0.0.1:{bridge.Port}/"));
        Assert.That(url, Does.Contain("nonce="));
        Assert.That(url, Does.Contain("entity=ent-99"));
        Assert.That(url, Does.Contain("action=inspectentity"));
    }

    [Test]
    public async Task InGameLauncher_BuildUrl_OpenRuntimeError_ContainsAllFields()
    {
        var nonceMgr = new SessionNonceManager();
        var security = new BridgeSecurityPolicy();

        await using var bridge = new AstraLocalBridge(
            nonceMgr, security, new NullAssetProvider(),
            (ctx, ct) => Task.CompletedTask);

        await bridge.StartAsync();

        var reporter = new AstraRuntimeStatusReporter(bridge);
        var launcher = new AstraInGameLauncher(bridge, reporter);

        var ctx = new StudioDeepLinkContext
        {
            Action = StudioAction.OpenRuntimeError,
            GraphId = "DrugMetabolism",
            NodeId = "7F3A",
            DiagnosticCode = "AG0099",
            ExecutionTick = 53
        };
        string url = launcher.BuildUrl(ctx);

        Assert.That(url, Does.Contain("action=openruntimeerror"));
        Assert.That(url, Does.Contain("graph=DrugMetabolism"));
        Assert.That(url, Does.Contain("node=7F3A"));
        Assert.That(url, Does.Contain("error=AG0099"));
        Assert.That(url, Does.Contain("tick=53"));
    }

    [Test]
    public void InGameLauncher_ThrowsWhenBridgeNotRunning()
    {
        var nonceMgr = new SessionNonceManager();
        var security = new BridgeSecurityPolicy();

        using var bridge = new AstraLocalBridge(
            nonceMgr, security, new NullAssetProvider(),
            (ctx, ct) => Task.CompletedTask);

        var reporter = new AstraRuntimeStatusReporter(bridge);
        var launcher = new AstraInGameLauncher(bridge, reporter);

        var ctx = new StudioDeepLinkContext { Action = StudioAction.OpenStudio };
        Assert.Throws<InvalidOperationException>(() => launcher.BuildUrl(ctx));
    }

    [Test]
    public async Task InGameLauncher_EachBuildUrl_GeneratesUniqueNonce()
    {
        var nonceMgr = new SessionNonceManager();
        var security = new BridgeSecurityPolicy();

        await using var bridge = new AstraLocalBridge(
            nonceMgr, security, new NullAssetProvider(),
            (ctx, ct) => Task.CompletedTask);

        await bridge.StartAsync();

        var reporter = new AstraRuntimeStatusReporter(bridge);
        var launcher = new AstraInGameLauncher(bridge, reporter);
        var ctx = new StudioDeepLinkContext { Action = StudioAction.OpenStudio };

        string url1 = launcher.BuildUrl(ctx);
        string url2 = launcher.BuildUrl(ctx);

        // Different nonce each time (one-time use)
        Assert.That(url1, Is.Not.EqualTo(url2));
    }
}

file sealed class NullAssetProvider : IWebAssetProvider
{
    public WebAsset? TryGetAsset(string path) => null;
}
