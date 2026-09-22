#if NET10_0_OR_GREATER
using AstraGraph.Core;
using AstraGraph.HotReload;
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
    public void RobustEventBus_PublishedGraph_MutatesDamageAndHandled()
    {
        var host = new AstraGraphHost();
        var document = DamageGraph(assign: true);
        var published = new HotReloadManager(host).Publish(document, author: "dev", message: "mutate");
        Assert.That(published.Success, Is.True, string.Join("; ", published.Diagnostics.Select(item => item.Message)));

        PublishedDamageSystem.Host = host;
        var simulation = RobustServerSimulation.NewSimulation()
            .RegisterEntitySystems(factory => factory.LoadExtraSystemType<PublishedDamageSystem>())
            .InitializeInstance();

        var ev = new DamageProbeEvent { Damage = 10, Handled = false };
        simulation.Resolve<IEntityManager>().EventBus.RaiseEvent(EventSource.Local, ref ev);

        Assert.That(host.ExecutedEntryPoints, Is.EqualTo(1));
        Assert.That(ev.Damage, Is.EqualTo(30));
        Assert.That(ev.Handled, Is.True);
    }

    [Test]
    public void RobustEventBus_PublishedGraph_LeavesEventWhenGraphDoesNotMutate()
    {
        var host = new AstraGraphHost();
        var document = DamageGraph(assign: false);
        Assert.That(new HotReloadManager(host).Publish(document, author: "dev", message: "passive").Success, Is.True);

        PublishedDamageSystem.Host = host;
        var simulation = RobustServerSimulation.NewSimulation()
            .RegisterEntitySystems(factory => factory.LoadExtraSystemType<PublishedDamageSystem>())
            .InitializeInstance();

        var ev = new DamageProbeEvent { Damage = 10, Handled = false };
        simulation.Resolve<IEntityManager>().EventBus.RaiseEvent(EventSource.Local, ref ev);

        Assert.That(host.ExecutedEntryPoints, Is.EqualTo(1));
        Assert.That(ev.Damage, Is.EqualTo(10));
        Assert.That(ev.Handled, Is.False);
    }

    private static GraphDocument DamageGraph(bool assign)
    {
        var graphId = GraphId.New();
        var entryId = NodeId.New();
        var entryOut = PinId.New();
        var nodes = new List<NodeDocument>
        {
            new()
            {
                Id = entryId,
                Name = "OnDamage",
                NodeType = "Event.Damage",
                Properties = new Dictionary<string, string> { ["eventType"] = typeof(DamageProbeEvent).AssemblyQualifiedName! },
                Pins = [new PinDocument { Id = entryOut, Name = "Out", Direction = PinDirection.Output, Kind = PinKind.Execution }]
            }
        };
        var connections = new List<ConnectionDocument>();
        if (assign)
        {
            var damageId = NodeId.New();
            var damageIn = PinId.New();
            var damageOut = PinId.New();
            var handledId = NodeId.New();
            var handledIn = PinId.New();
            nodes.Add(new NodeDocument
            {
                Id = damageId,
                Name = "SetDamage",
                NodeType = "Core.VariableAssign",
                Properties = new Dictionary<string, string> { ["VariableName"] = "Damage" },
                Pins =
                [
                    new PinDocument { Id = damageIn, Name = "In", Direction = PinDirection.Input, Kind = PinKind.Execution },
                    new PinDocument { Id = damageOut, Name = "Out", Direction = PinDirection.Output, Kind = PinKind.Execution },
                    new PinDocument { Id = PinId.New(), Name = "Value", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "int", DefaultValue = "30" }
                ]
            });
            nodes.Add(new NodeDocument
            {
                Id = handledId,
                Name = "SetHandled",
                NodeType = "Core.VariableAssign",
                Properties = new Dictionary<string, string> { ["VariableName"] = "Handled" },
                Pins =
                [
                    new PinDocument { Id = handledIn, Name = "In", Direction = PinDirection.Input, Kind = PinKind.Execution },
                    new PinDocument { Id = PinId.New(), Name = "Value", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "bool", DefaultValue = "true" }
                ]
            });
            connections.Add(new ConnectionDocument { FromNode = entryId, FromPin = entryOut, ToNode = damageId, ToPin = damageIn });
            connections.Add(new ConnectionDocument { FromNode = damageId, FromPin = damageOut, ToNode = handledId, ToPin = handledIn });
        }

        return new GraphDocument
        {
            Id = graphId,
            Name = assign ? "MutatingDamage" : "PassiveDamage",
            Variables = assign
                ?
                [
                    new GraphVariableDocument { Name = "Damage", TypeName = "int" },
                    new GraphVariableDocument { Name = "Handled", TypeName = "bool" }
                ]
                : [],
            Nodes = nodes,
            Connections = connections
        };
    }

    [ByRefEvent]
    public struct DamageProbeEvent
    {
        public int Damage { get; set; }
        public bool Handled { get; set; }
    }

    public sealed class PublishedDamageSystem : EntitySystem
    {
        public static AstraGraphHost Host { get; set; } = null!;

        public override void Initialize()
        {
            base.Initialize();
            new RobustEventBusSubscriptionAdapter(Host.EventRouter)
                .RegisterBroadcastSubscription<DamageProbeEvent>(this, GraphId.Empty, "OnDamage");
        }
    }
}
#endif
