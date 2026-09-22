using AstraGraph.Binding;
using AstraGraph.Core;
using AstraGraph.HotReload;
using AstraGraph.Runtime;
using AstraGraph.Runtime.Security;
using AstraGraph.State;
using NUnit.Framework;

#if NET10_0_OR_GREATER
using AstraGraph.Robust.Client;
using AstraGraph.Robust.Server;
using AstraGraph.Robust.Shared;
using Robust.Client.UserInterface.Controls;
#endif

namespace AstraGraph.Tests;

public struct TestRefValueEvent
{
    public bool Handled { get; set; }
    public int Damage { get; set; }
}

[TestFixture]
public sealed class RobustIntegrationTests
{
    [Test]
    public void HotReload_PublishWithStateMigration_PreservesAndExpandsComponentData()
    {
        var componentStore = new DynamicComponentStore();
        var host = new AstraGraphHost(components: componentStore);
        var hotReload = new HotReloadManager(host);

        var schemaId = SchemaId.New();
        var countFieldId = FieldId.New();
        var bonusFieldId = FieldId.New();

        // Schema V1: Count (Int64)
        var countField = new SchemaField(countFieldId, "Count", PrimitiveType.Int64);
        var schemaV1 = new SchemaType(schemaId, "CounterComp", true, [countField]);
        hotReload.RegisterActiveSchema(schemaV1);

        // Add instance on Entity 42
        var storage = componentStore.AddComponent(42, schemaV1, [AstraValue.FromInt64(100L)]);
        Assert.That(storage.GetField(countFieldId).AsInt64(), Is.EqualTo(100L));

        // Schema V2: Count (Int64 preserved) + Bonus (Int64 default 50)
        var bonusField = new SchemaField(bonusFieldId, "Bonus", PrimitiveType.Int64, DefaultValue: "50");
        var schemaV2 = new SchemaType(schemaId, "CounterComp", true, [countField, bonusField]);

        // Publish new graph revision with schema V2
        var draft = new GraphDocument
        {
            Id = GraphId.New(),
            Name = "CounterSystem",
            Kind = GraphKind.System,
            Side = GraphSide.Server,
            Nodes = [],
            Connections = []
        };

        var publishResult = hotReload.Publish(draft, author: "Admin", message: "Evolve Counter to V2", declaredSchema: schemaV2);

        Assert.That(publishResult.Success, Is.True);

        // Verify entity 42 now has migrated storage with preserved Count and default Bonus!
        var migrated = componentStore.GetComponent(42, schemaId);
        Assert.That(migrated.GetField(countFieldId).AsInt64(), Is.EqualTo(100L));
        Assert.That(migrated.GetField(bonusFieldId).AsInt64(), Is.EqualTo(50L));
    }

#if NET10_0_OR_GREATER
    [Test]
    public void RobustAdminPermissionProvider_ResolvesSS14AdminFlagsProperly()
    {
        var provider = new RobustAdminPermissionProvider();

        // Mock session with HeadAdmin rank and AdminFlags.AstraGraph bit set (1u << 24)
        var headAdminSession = new MockAdminSession("admin-1", SS14AdminFlagsConstants.AdminFlagAstraGraph, "HeadAdmin");
        var headAdminUser = provider.ResolveUser(headAdminSession);

        Assert.That(headAdminUser, Is.Not.Null);
        Assert.That(headAdminUser!.Profile, Is.EqualTo(SecurityProfile.Engine));
        Assert.That(AstraAuthorizationService.HasPermission(headAdminUser, AstraPermission.PublishServer), Is.True);
        Assert.That(AstraAuthorizationService.HasPermission(headAdminUser, AstraPermission.UseEngineProfile), Is.True);

        // Session with ContentDev rank
        var devSession = new MockAdminSession("dev-1", SS14AdminFlagsConstants.AdminFlagAstraGraph, "ContentDev");
        var devUser = provider.ResolveUser(devSession);

        Assert.That(devUser, Is.Not.Null);
        Assert.That(devUser!.Profile, Is.EqualTo(SecurityProfile.Gameplay));
        Assert.That(AstraAuthorizationService.HasPermission(devUser, AstraPermission.UseEngineProfile), Is.False);

        // Session without AdminFlags.AstraGraph
        var regularSession = new MockAdminSession("player-1", 0u, null);
        var regularUser = provider.ResolveUser(regularSession);

        Assert.That(regularUser, Is.Null);
        Assert.That(provider.HasAccess(regularSession), Is.False);
    }

    [Test]
    public void RobustUiControlFactory_CreatesAndBindsRealRobustControls()
    {
        var factory = new RobustUiControlFactory();

        // 1. Create a Button control
        var buttonWrapper = factory.CreateControl(
            "btn_test",
            AstraGraph.UI.Model.UiElementType.Button,
            "SubmitButton",
            100,
            30,
            AstraGraph.UI.Model.UiOrientation.Horizontal);

        Assert.That(buttonWrapper, Is.Not.Null);

        // Test property binding
        buttonWrapper.Text = "Execute";
        Assert.That(buttonWrapper.Text, Is.EqualTo("Execute"));

        var pressedFired = false;
        buttonWrapper.OnEventTriggered += (name, payload) =>
        {
            if (name == "OnPressed") pressedFired = true;
        };

        buttonWrapper.TriggerEvent("OnPressed");
        Assert.That(pressedFired, Is.True);

        // 2. Create a LineEdit control
        var inputWrapper = factory.CreateControl(
            "input_test",
            AstraGraph.UI.Model.UiElementType.LineEdit,
            "SearchInput",
            200,
            24,
            AstraGraph.UI.Model.UiOrientation.Horizontal);

        Assert.That(inputWrapper, Is.Not.Null);
        inputWrapper.Text = "Query string";
        Assert.That(inputWrapper.Text, Is.EqualTo("Query string"));
    }

    [Test]
    public void RobustEventBusSubscriptionAdapter_RefEventDispatch_MutatesOriginalStructInPlace()
    {
        var router = new GraphEventRouter();
        var adapter = new RobustEventBusSubscriptionAdapter(router);
        var graphId = GraphId.New();

        router.SubscribeRef<TestRefValueEvent>(graphId, "OnDamage", (ref TestRefValueEvent evArgs) =>
        {
            evArgs.Damage *= 3;
            evArgs.Handled = true;
        });

        var ev = new TestRefValueEvent { Handled = false, Damage = 10 };

        router.DispatchRefEvent(ref ev);

        Assert.That(ev.Damage, Is.EqualTo(30));
        Assert.That(ev.Handled, Is.True);
    }

    private sealed class MockAdminSession : IAstraAdminFacts
    {
        public MockAdminSession(string userId, uint adminFlags, string? rank)
        {
            UserId = userId;
            AdminFlags = adminFlags;
            Rank = rank;
        }

        public string UserId { get; }
        public string Name => UserId;
        public uint AdminFlags { get; }
        public string? Rank { get; }
        public bool IsPlayerSandbox => false;
    }
#endif
}
