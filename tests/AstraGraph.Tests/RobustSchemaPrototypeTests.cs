#if NET10_0_OR_GREATER
using AstraGraph.Core;
using AstraGraph.Robust.Shared;
using AstraGraph.State;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.UnitTesting.Server;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class RobustSchemaPrototypeTests
{
    [Test]
    public void PrototypeYaml_SpawnsSchemaProxy_WithoutClrComponent()
    {
        var schema = SchemaDocuments.ToSchema(new ComponentSchemaDocument
        {
            Name = "MothroachStrike",
            Kind = "Component",
            Fields =
            [
                new ComponentFieldDocument { Name = "spawn", TypeName = "EntProtoId<EntityPrototype>", DefaultValue = "MobMothroach" },
                new ComponentFieldDocument { Name = "count", TypeName = "int32", DefaultValue = "1" }
            ]
        }, TypeRegistry.CreateDefault());
        var registry = new AstraSchemaRegistry();
        registry.RegisterSchema(schema);
        AstraSchemaRuntime.Registry = registry;
        AstraSchemaRuntime.PrototypeName = "WeaponFoo";

        try
        {
            var simulation = RobustServerSimulation
                .NewSimulation()
                .RegisterComponents(factory => AstraSchemaComponentBridge.RegisterProxy(factory, schema))
                .RegisterPrototypes(prototypes =>
                {
                    var serialization = IoCManager.Resolve<ISerializationManager>();
                    var proxy = IoCManager.Resolve<IComponentFactory>().GetRegistration("MothroachStrike").Type;
                    AstraSchemaComponentBridge.RegisterSerializer(serialization, proxy);
                    prototypes.LoadString("""
                        - type: entity
                          id: WeaponFoo
                          components:
                          - type: MothroachStrike
                            spawn: TestMothroach
                            count: 3
                        """);
                })
                .InitializeInstance();

            var factory = simulation.Resolve<IComponentFactory>();
            Assert.That(factory.TryGetRegistration("MothroachStrike", out var registration), Is.True);
            Assert.That(registration!.Type.Name, Is.EqualTo("MothroachStrikeComponent"));
            Assert.That(typeof(AstraSchemaComponentProxy).IsAssignableFrom(registration.Type), Is.True);

            var spawned = simulation.SpawnEntity("WeaponFoo", MapCoordinates.Nullspace);
            var entities = simulation.Resolve<IEntityManager>();
            var proxy = entities.GetComponents(spawned).OfType<AstraSchemaComponentProxy>().Single();
            Assert.That(proxy.Fields["spawn"], Is.EqualTo("TestMothroach"));
            Assert.That(proxy.Fields["count"], Is.EqualTo("3"));

            var source = new SchemaComponentSource(new DynamicComponentStore(), registry);
            Assert.That(AstraSchemaComponentBridge.ApplyProxy(source, spawned.Id, proxy, "WeaponFoo"), Is.True);
            var component = source.TryGet(spawned.Id, "MothroachStrike");
            Assert.That(component.Found, Is.True);
            Assert.That(component.Read("spawn").AsString(), Is.EqualTo("TestMothroach"));
            Assert.That(component.Read("count").AsInt64(), Is.EqualTo(3));

            entities.DeleteEntity(spawned);
        }
        finally
        {
            AstraSchemaRuntime.Registry = null;
            AstraSchemaRuntime.PrototypeName = null;
        }
    }
}
#endif
