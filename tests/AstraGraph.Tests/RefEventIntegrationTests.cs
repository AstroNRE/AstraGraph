#if NET10_0_OR_GREATER
using AstraGraph.Core;
using AstraGraph.Robust.Shared;
using AstraGraph.Runtime;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.UnitTesting.Server;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class RefEventIntegrationTests
{
    [Test]
    public void RobustEventBus_RefDispatch_MutatesDamageAndHandled()
    {
        var simulation = RobustServerSimulation.NewSimulation()
            .RegisterEntitySystems(factory => factory.LoadExtraSystemType<DamageGraphSystem>())
            .InitializeInstance();

        var ev = new DamageProbeEvent { Damage = 10, Handled = false };
        simulation.Resolve<IEntityManager>().EventBus.RaiseEvent(EventSource.Local, ref ev);

        Assert.That(ev.Damage, Is.EqualTo(30));
        Assert.That(ev.Handled, Is.True);
    }

    [Test]
    public void RobustEventBus_RefDispatch_LeavesEventWhenGraphDoesNotMutate()
    {
        var simulation = RobustServerSimulation.NewSimulation()
            .RegisterEntitySystems(factory => factory.LoadExtraSystemType<PassiveGraphSystem>())
            .InitializeInstance();

        var ev = new DamageProbeEvent { Damage = 10, Handled = false };
        simulation.Resolve<IEntityManager>().EventBus.RaiseEvent(EventSource.Local, ref ev);

        Assert.That(ev.Damage, Is.EqualTo(10));
        Assert.That(ev.Handled, Is.False);
    }

    [ByRefEvent]
    public struct DamageProbeEvent
    {
        public int Damage { get; set; }
        public bool Handled { get; set; }
    }

    public sealed class DamageGraphSystem : EntitySystem
    {
        public override void Initialize()
        {
            base.Initialize();
            var host = new AstraGraphHost();
            var graphId = GraphId.New();
            host.EventRouter.SubscribeRef<DamageProbeEvent>(graphId, "OnDamage", (ref DamageProbeEvent ev) =>
            {
                ev.Damage = 30;
                ev.Handled = true;
            });
            new RobustEventBusSubscriptionAdapter(host.EventRouter)
                .RegisterBroadcastSubscription<DamageProbeEvent>(this, graphId, "OnDamage");
        }
    }

    public sealed class PassiveGraphSystem : EntitySystem
    {
        public override void Initialize()
        {
            base.Initialize();
            var host = new AstraGraphHost();
            new RobustEventBusSubscriptionAdapter(host.EventRouter)
                .RegisterBroadcastSubscription<DamageProbeEvent>(this, GraphId.New(), "OnDamage");
        }
    }
}
#endif
