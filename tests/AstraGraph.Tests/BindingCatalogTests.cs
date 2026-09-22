using System.Numerics;
using System.Reflection;
using AstraGraph.Binding;
using AstraGraph.Core;
using AstraGraph.HotReload;
using AstraGraph.VM;
using NUnit.Framework;

namespace AstraGraph.Tests;

public static class SampleMathService
{
    [AstraCallable, AstraPure, AstraPredicted]
    public static double AddDoubles(double a, double b) => a + b;

    [AstraCallable, AstraServer]
    public static void DoServerAction(int entityId, string action)
    {
        // Sample side-effect
    }

    [AstraHidden]
    public static void HiddenMethod() { }
}

public sealed class SampleTransformComponent
{
    public Vector2 Position { get; set; } = Vector2.Zero;

    [AstraCallable]
    public void SetPosition(float x, float y)
    {
        Position = new Vector2(x, y);
    }
}

[TestFixture]
public sealed class BindingCatalogTests
{
    [Test]
    public void FastInvoker_ExecutesStaticAndInstanceMethodsWithoutReflectionOverhead()
    {
        // 1. Static method: AddDoubles(double, double) -> double
        var addMethod = typeof(SampleMathService).GetMethod(nameof(SampleMathService.AddDoubles))!;
        var addInvoker = FastInvokerCompiler.Compile(addMethod);

        var addResult = addInvoker([AstraValue.FromDouble(10.5), AstraValue.FromDouble(4.5)]);
        Assert.That(addResult.Type, Is.EqualTo(AstraValueType.Double));
        Assert.That(addResult.AsDouble(), Is.EqualTo(15.0));

        // 2. Instance method: SetPosition(float, float) on SampleTransformComponent
        var setPosMethod = typeof(SampleTransformComponent).GetMethod(nameof(SampleTransformComponent.SetPosition))!;
        var setPosInvoker = FastInvokerCompiler.Compile(setPosMethod);

        var comp = new SampleTransformComponent();
        var compValue = AstraValue.FromObject(comp);

        var voidResult = setPosInvoker([compValue, AstraValue.FromFloat(100.0f), AstraValue.FromFloat(200.0f)]);
        Assert.That(voidResult.Type, Is.EqualTo(AstraValueType.Null));
        Assert.That(comp.Position, Is.EqualTo(new Vector2(100.0f, 200.0f)));
    }

    [Test]
    public void BindingCatalog_IndexesMethodsAndHonorsAttributes()
    {
        var catalog = new BindingCatalog();
        catalog.IndexType(typeof(SampleMathService));

        // Hidden method should NOT be present
        Assert.That(catalog.Search("HiddenMethod").Any(), Is.False);

        // AddDoubles should be indexed
        var methods = catalog.Search("AddDoubles").ToList();
        Assert.That(methods.Count, Is.EqualTo(1));

        var m = methods[0];
        Assert.That(m.Name, Is.EqualTo("AddDoubles"));
        Assert.That(m.IsPure, Is.True);
        Assert.That(m.IsDeterministic, Is.True);
        Assert.That(m.Side, Is.EqualTo(GraphSide.SharedPredicted));

        // Call via descriptor invoker
        var res = m.Invoker([AstraValue.FromDouble(20.0), AstraValue.FromDouble(30.0)]);
        Assert.That(res.AsDouble(), Is.EqualTo(50.0));
    }

    [Test]
    public void AccessPolicy_EnforcesSecurityProfiles()
    {
        Assert.That(AccessPolicy.IsAccessAllowed(SecurityProfile.Gameplay, SecurityProfile.Gameplay), Is.True);
        Assert.That(AccessPolicy.IsAccessAllowed(SecurityProfile.Trusted, SecurityProfile.Gameplay), Is.False);
        Assert.That(AccessPolicy.IsAccessAllowed(SecurityProfile.Engine, SecurityProfile.Gameplay), Is.False);

        Assert.That(AccessPolicy.IsAccessAllowed(SecurityProfile.Gameplay, SecurityProfile.Trusted), Is.True);
        Assert.That(AccessPolicy.IsAccessAllowed(SecurityProfile.Trusted, SecurityProfile.Trusted), Is.True);
        Assert.That(AccessPolicy.IsAccessAllowed(SecurityProfile.Engine, SecurityProfile.Trusted), Is.False);

        Assert.That(AccessPolicy.IsAccessAllowed(SecurityProfile.Gameplay, SecurityProfile.Engine), Is.True);
        Assert.That(AccessPolicy.IsAccessAllowed(SecurityProfile.Trusted, SecurityProfile.Engine), Is.True);
        Assert.That(AccessPolicy.IsAccessAllowed(SecurityProfile.Engine, SecurityProfile.Engine), Is.True);
    }

    [Test]
    public void IndexGameplaySurface_ResolvesEventAndComponentByShortName()
    {
        var catalog = new BindingCatalog();
        catalog.IndexGameplaySurface(typeof(SampleHitEvent).Assembly);

        Assert.That(catalog.TryGetNamedType(nameof(SampleHitEvent), out var hit), Is.True);
        Assert.That(hit, Is.EqualTo(typeof(SampleHitEvent)));
        Assert.That(catalog.TryGetNamedType(nameof(SampleTransformComponent), out var component), Is.True);
        Assert.That(component, Is.EqualTo(typeof(SampleTransformComponent)));

        var resolver = new CatalogEntryPointTypeResolver(catalog);
        Assert.That(resolver.Resolve(nameof(SampleHitEvent)), Is.EqualTo(typeof(SampleHitEvent)));
    }
}

public sealed class SampleHitEvent
{
    public int Id { get; set; }
}
