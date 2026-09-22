using AstraGraph.Binding;
using AstraGraph.Core;
using AstraGraph.Runtime;
using AstraGraph.Runtime.Security;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class AdminPermissionResolverTests
{
    private AstraAdminPermissionResolver _resolver = null!;

    [SetUp]
    public void SetUp()
    {
        _resolver = new AstraAdminPermissionResolver();
    }

    [Test]
    public void UserWithoutAdminFlag_IsRejected()
    {
        var result = _resolver.ResolveSession(
            userId: "user-1",
            userName: "Alice",
            userAdminFlags: 0u); // No flags

        Assert.That(result.IsAllowed, Is.False);
        Assert.That(result.RejectionReason, Does.Contain("AdminFlags.AstraGraph"));
        Assert.That(result.User, Is.Null);
    }

    [Test]
    public void HeadAdmin_GetsFullAdminAndEngineProfile()
    {
        var result = _resolver.ResolveSession(
            userId: "user-admin",
            userName: "Bob",
            userAdminFlags: SS14AdminFlagsConstants.AdminFlagAstraGraph,
            adminRank: "HeadAdmin");

        Assert.That(result.IsAllowed, Is.True);
        Assert.That(result.User, Is.Not.Null);
        Assert.That(AstraAuthorizationService.HasPermission(result.User!, AstraPermission.Admin), Is.True);
        Assert.That(result.User!.Profile, Is.EqualTo(SecurityProfile.Engine));
    }

    [Test]
    public void ContentDeveloper_GetsStandardPermissions_AndDefaultProfile()
    {
        var result = _resolver.ResolveSession(
            userId: "user-dev",
            userName: "Charlie",
            userAdminFlags: SS14AdminFlagsConstants.AdminFlagAstraGraph,
            adminRank: "ContentDev");

        Assert.That(result.IsAllowed, Is.True);
        var user = result.User!;
        Assert.That(AstraAuthorizationService.CanEditDraft(user), Is.True);
        Assert.That(AstraAuthorizationService.CanCompile(user), Is.True);
        Assert.That(AstraAuthorizationService.CanPublish(user, GraphSide.Server, SecurityProfile.Gameplay), Is.False);
        Assert.That(user.Profile, Is.EqualTo(SecurityProfile.Gameplay));

        // Content Dev should NOT be able to publish Engine profile graphs
        Assert.That(AstraAuthorizationService.CanPublish(user, GraphSide.Server, SecurityProfile.Engine), Is.False);
    }

    [Test]
    public void Publisher_GetsPublish_ContentDeveloperDoesNot()
    {
        var publisher = _resolver.ResolveSession(
            "user-pub",
            "Eve",
            SS14AdminFlagsConstants.AdminFlagAstraGraph,
            adminRank: "Publisher");

        Assert.That(AstraAuthorizationService.CanPublish(publisher.User!, GraphSide.Server, SecurityProfile.Gameplay), Is.True);
    }

    [Test]
    public void PlayerSandbox_AllowedWithoutAdminFlags_WithRestrictedPermissions()
    {
        var result = _resolver.ResolveSession(
            userId: "player-1",
            userName: "Dan",
            userAdminFlags: 0u,
            isPlayerSandbox: true,
            sandboxEntityId: "entity-turret-42");

        Assert.That(result.IsAllowed, Is.True);
        var user = result.User!;
        Assert.That(AstraAuthorizationService.CanEditDraft(user), Is.True);
        Assert.That(AstraAuthorizationService.CanCompile(user), Is.True);
        // Cannot publish to server/shared globally
        Assert.That(AstraAuthorizationService.CanPublish(user, GraphSide.Server, SecurityProfile.Gameplay), Is.False);
    }

    public sealed class TestPlayerDiedEvent { public string Reason { get; set; } = "Unknown"; }

    [Test]
    public void EventSubscriptionAdapter_DispatchesSuccessfully()
    {
        var router = new GraphEventRouter();
        var adapter = new AstraEventSubscriptionAdapter(router);

        string? receivedReason = null;
        var graphId = GraphId.New();

        adapter.RegisterSubscription<object, TestPlayerDiedEvent>(
            graphId,
            "OnPlayerDied",
            (_, ev) => receivedReason = ev.Reason);

        adapter.Dispatch(new TestPlayerDiedEvent { Reason = "Explosion" });

        Assert.That(receivedReason, Is.EqualTo("Explosion"));
    }
}
