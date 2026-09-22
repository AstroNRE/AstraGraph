#if NET10_0_OR_GREATER
using AstraGraph.Binding;
using AstraGraph.Robust.Shared;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.UnitTesting.Server;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class RobustGameplayTests
{
    [Test]
    public void SpawnAndDelete_UsesEntityManager()
    {
        var simulation = RobustServerSimulation.NewSimulation().InitializeInstance();
        var entities = simulation.Resolve<IEntityManager>();
        var catalog = new BindingCatalog();
        GameplayBindings.Index(catalog, entities);

        var spawned = catalog.FindMethod("Entity.Spawn")!.Invoker([]);
        var id = (int)spawned.AsInt64();
        Assert.That(id, Is.GreaterThan(0));
        Assert.That(entities.EntityExists(new EntityUid(id)), Is.True);

        catalog.FindMethod("Entity.Delete")!.Invoker([AstraGraph.Core.AstraValue.FromInt32(id)]);
        Assert.That(entities.EntityExists(new EntityUid(id)), Is.False);
    }
}
#endif
