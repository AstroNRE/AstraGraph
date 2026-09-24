#if NET10_0_OR_GREATER
using System.Reflection;
using System.Runtime.CompilerServices;
using AstraGraph.Binding;
using AstraGraph.Core;
using AstraGraph.Robust.Shared;
using NUnit.Framework;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
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

        catalog.FindMethod("Entity.Delete")!.Invoker([AstraValue.FromInt32(id)]);
        Assert.That(entities.EntityExists(new EntityUid(id)), Is.False);
    }

    [Test]
    public void Container_InsertsFindsAndRemovesByName()
    {
        var simulation = RobustServerSimulation.NewSimulation().InitializeInstance();
        var entities = simulation.Resolve<IEntityManager>();
        var catalog = new BindingCatalog();
        GameplayBindings.Index(catalog, entities);
        var containers = entities.EntitySysManager.GetEntitySystem<SharedContainerSystem>();

        var box = (int)catalog.FindMethod("Entity.Spawn")!.Invoker([]).AsInt64();
        var item = (int)catalog.FindMethod("Entity.Spawn")!.Invoker([]).AsInt64();
        containers.EnsureContainer<Container>(new EntityUid(box), "storage");
        SetPrototype(entities, item, "Wrench");

        Assert.That(Call(catalog, "Container.Has", box, "storage").AsBool(), Is.True);
        Assert.That(Call(catalog, "Container.Insert", box, "storage", item).AsBool(), Is.True);
        Assert.That(Call(catalog, "Container.Insert", box, "missing", item).AsBool(), Is.False);
        var contents = (int[])Call(catalog, "Container.Contents", box, "storage").AsObject()!;
        Assert.That(contents, Is.EqualTo(new[] { item }));
        Assert.That(Call(catalog, "Inventory.Contains", box, item).AsBool(), Is.True);
        Assert.That(Call(catalog, "Inventory.Find", box, "Wrench").AsInt32(), Is.EqualTo(item));
        Assert.That(Call(catalog, "Inventory.Find", box, "Barrel").AsInt32(), Is.EqualTo(0));
        Assert.That(Call(catalog, "Entity.GetHeldItem", box, "storage").AsInt32(), Is.EqualTo(item));
        Assert.That(Call(catalog, "Inventory.TryRemove", box, item).AsBool(), Is.True);
        Assert.That(Call(catalog, "Inventory.Contains", box, item).AsBool(), Is.False);
        Assert.That(Call(catalog, "Container.Has", box, "storage").AsBool(), Is.True);
    }

    [Test]
    public void BuiSet_WritesAStringStateAndSkipsAnEntityWithoutUi()
    {
        var simulation = RobustServerSimulation.NewSimulation().InitializeInstance();
        var entities = simulation.Resolve<IEntityManager>();
        var catalog = new BindingCatalog();
        GameplayBindings.Index(catalog, entities);

        var owner = (int)catalog.FindMethod("Entity.Spawn")!.Invoker([]).AsInt64();
        Assert.That(Call(catalog, "Bui.Set", owner, "Parts", "[]").AsBool(), Is.False);

        var state = GameplayBindings.AssignState(null, "Parts", "[{\"id\":\"barrel-1\"}]");
        Assert.That(state.Values["Parts"], Is.EqualTo("[{\"id\":\"barrel-1\"}]"));
        Assert.That(state.TypedValues["Parts"], Is.EqualTo("string:[{\"id\":\"barrel-1\"}]"));
        Assert.That(state.Revision, Is.EqualTo(1));

        var next = GameplayBindings.AssignState(state, "Reason", "occupied");
        Assert.That(next.Revision, Is.EqualTo(2));
        Assert.That(next.Values["Parts"], Is.EqualTo("[{\"id\":\"barrel-1\"}]"));
        Assert.That(next.Values["Reason"], Is.EqualTo("occupied"));
        Assert.That(state.Values.ContainsKey("Reason"), Is.False);
    }

    private static AstraValue Call(BindingCatalog catalog, string name, params object[] args)
    {
        var values = args.Select(arg => arg switch
        {
            int number => AstraValue.FromInt32(number),
            string text => AstraValue.FromString(text),
            _ => throw new InvalidOperationException("Unsupported test argument.")
        }).ToArray();
        return catalog.FindMethod(name)!.Invoker(values);
    }

    private static void SetPrototype(IEntityManager entities, int item, string prototypeId)
    {
        var prototype = (EntityPrototype)RuntimeHelpers.GetUninitializedObject(typeof(EntityPrototype));
        typeof(EntityPrototype).GetProperty(nameof(EntityPrototype.ID))!.SetValue(prototype, prototypeId);
        var metadata = entities.GetComponent<MetaDataComponent>(new EntityUid(item));
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MetaDataComponent).GetField("_entityName", flags)!.SetValue(metadata, prototypeId);
        typeof(MetaDataComponent).GetField("_entityDescription", flags)!.SetValue(metadata, string.Empty);
        typeof(MetaDataComponent).GetField("_entityPrototype", flags)!.SetValue(metadata, prototype);
    }
}
#endif
