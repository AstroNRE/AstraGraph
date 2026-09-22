using AstraGraph.Binding;
using AstraGraph.Core;
using AstraGraph.HotReload;
using AstraGraph.Persistence;
using AstraGraph.Persistence.Discovery;
using AstraGraph.Runtime;
using AstraGraph.State;
using AstraGraph.Tests.Probes;
using AstraGraph.VM;
using NUnit.Framework;

#pragma warning disable CA1822

namespace AstraGraph.Tests;

[TestFixture]
public sealed class GraphDefinedComponentTests
{
    [Test]
    public void Schema_DefaultsUnknownFieldAndInvalidYaml_AreReported()
    {
        var schema = MothroachSchema(out var spawn, out var count);
        var registry = new AstraSchemaRegistry();
        registry.RegisterSchema(schema);

        var defaults = SchemaYamlBinder.Bind(schema, new Dictionary<string, object?>(), "WeaponFoo");
        Assert.That(defaults.Success, Is.True);
        Assert.That(defaults.Values[Index(schema, spawn)].AsString(), Is.EqualTo("MobMothroach"));
        Assert.That(defaults.Values[Index(schema, count)].AsInt64(), Is.EqualTo(1));

        var unknown = SchemaYamlBinder.Bind(schema, new Dictionary<string, object?> { ["extra"] = "x" }, "WeaponFoo");
        Assert.That(unknown.Success, Is.False);
        Assert.That(unknown.Diagnostics[0].Code, Is.EqualTo(DiagnosticCodes.UnknownField));
        Assert.That(unknown.Diagnostics[0].Message, Does.Contain("Field extra"));

        var invalid = SchemaYamlBinder.Bind(schema, new Dictionary<string, object?> { ["count"] = "banana" }, "WeaponFoo");
        Assert.That(invalid.Success, Is.False);
        Assert.That(invalid.Diagnostics[0].Code, Is.EqualTo(DiagnosticCodes.InvalidYamlField));
        Assert.That(invalid.Diagnostics[0].Message, Does.Contain("Expected: Int32"));
        Assert.That(invalid.Diagnostics[0].Message, Does.Contain("Received: string"));
        Assert.That(registry.TryGetSchema("MothroachStrike", out var found), Is.True);
        Assert.That(found!.FindField(count)!.Name, Is.EqualTo("count"));
    }

    [Test]
    public void Rename_PreservesFieldById()
    {
        var schemaId = SchemaId.New();
        var fieldId = FieldId.New();
        var before = new SchemaType(schemaId, "MothroachStrike", true, [new SchemaField(fieldId, "count", PrimitiveType.Int32, "1")]);
        var after = new SchemaType(schemaId, "MothroachStrike", true, [new SchemaField(fieldId, "amount", PrimitiveType.Int32, "1")]);
        var plan = StateMigrationPlanner.CreatePlan(before, after);
        var step = plan.Steps.Single(item => item.Action == FieldMigrationAction.Preserve);
        Assert.That(step.SourceFieldName, Is.EqualTo("count"));
        Assert.That(step.TargetFieldName, Is.EqualTo("amount"));
        Assert.That(step.TargetFieldId, Is.EqualTo(fieldId));
    }

    [Test]
    public void Component_AttachReadsFields_AndDeleteClearsStore()
    {
        var schema = MothroachSchema(out _, out var count);
        var registry = new AstraSchemaRegistry();
        registry.RegisterSchema(schema);
        var source = new SchemaComponentSource(new DynamicComponentStore(), registry);
        Assert.That(source.ApplyInitial(7, schema, new Dictionary<string, string> { ["spawn"] = "MobMothroach", ["count"] = "3" }, "WeaponFoo", out var error), Is.True);
        Assert.That(error, Is.Null);
        Assert.That(source.Has(7, "MothroachStrike"), Is.True);
        Assert.That(source.TryGet(7, "MothroachStrike").Read("count").AsInt64(), Is.EqualTo(3));
        Assert.That(source.TryGet(7, "MothroachStrike").Read("spawn").AsString(), Is.EqualTo("MobMothroach"));

        source.Remove(7, "MothroachStrike");
        Assert.That(source.Has(7, "MothroachStrike"), Is.False);
        Assert.That(source.TryGet(7, "MothroachStrike").Found, Is.False);
    }

    [Test]
    public void EventContext_IsInvocationLocal_AndDoesNotCopyIntoVariables()
    {
        var host = new DefaultVmHostServices();
        var seen = new List<string>();
        host.RegisterNativeMethod("Probe.See", args =>
        {
            seen.Add(args[0].AsString() ?? "null");
            return AstraValue.Null;
        });

        var graph = new BuiltGraph();
        var entry = graph.Node("Event.MeleeHit", "Melee", null,
            graph.ExecOut("Out"),
            graph.DataOut("Entity", "EntityUid"),
            graph.DataOut("Event", "object"));
        var member = graph.Node("Native.GetMember", "User", new Dictionary<string, string> { ["Member"] = "User" },
            graph.DataIn("Target", "object"),
            graph.DataOut("Value", "object"));
        var call = graph.Node("Native.Call", "See", new Dictionary<string, string> { ["Method"] = "Probe.See" },
            graph.ExecIn("In"),
            graph.DataIn("Value", "string"));
        var variable = graph.Node("Core.VariableRead", "UserVar", new Dictionary<string, string> { ["VariableName"] = "User" },
            graph.DataOut("Value", "string"));
        graph.Variables.Add(new GraphVariableDocument { Name = "User", TypeName = "string" });
        graph.Wire(entry, "Out", call, "In");
        graph.Wire(entry, "Event", member, "Target");
        graph.Wire(member, "Value", call, "Value");
        _ = variable;

        var program = Compile(graph.Document("Probe"));
        host.PushEventContext(new AstraEventInvocationContext(AstraValue.FromEntityUid(7), null, new UserProbe { User = "from-event" }));
        var inner = new AstraEventInvocationContext(AstraValue.FromEntityUid(8), null, new UserProbe { User = "inner" });
        host.PushEventContext(inner);
        Assert.That(host.PeekEventContext(), Is.SameAs(inner));
        host.PopEventContext();
        Execute(host, program, "Melee");
        host.PopEventContext();
        Assert.That(host.PeekEventContext(), Is.Null);
        Assert.That(seen, Is.EqualTo(new[] { "from-event" }));
    }

    [Test]
    public void Catalog_IndexesPublicProperties()
    {
        var catalog = new BindingCatalog(TypeRegistry.CreateDefault());
        catalog.IndexEventType(typeof(UserProbe));
        Assert.That(catalog.TryGetEvent(typeof(UserProbe).FullName!, out var eventType), Is.True);
        Assert.That(eventType, Is.EqualTo(typeof(UserProbe)));
        Assert.That(catalog.SearchMembers("User").Any(member => member.DeclaringTypeName == nameof(UserProbe) && member.Name == "User"), Is.True);
    }

    [Test]
    public void For_RepeatsBody_AndForEachReadsItems()
    {
        var host = new DefaultVmHostServices();
        var ticks = 0;
        var seen = new List<int>();
        host.RegisterNativeMethod("Probe.Tick", _ =>
        {
            ticks++;
            return AstraValue.Null;
        });
        host.RegisterNativeMethod("Probe.Item", args =>
        {
            seen.Add(args[0].AsEntityUid());
            return AstraValue.Null;
        });

        var loop = new BuiltGraph();
        loop.Variables.Add(new GraphVariableDocument { Name = "Count", TypeName = "int32" });
        var entry = loop.Node("Function.Repeat", "Repeat", null, loop.ExecOut("Out"));
        var count = loop.Node("Core.VariableRead", "Count", new Dictionary<string, string> { ["VariableName"] = "Count" }, loop.DataOut("Value", "int32"));
        var forNode = loop.Node("Flow.For", "For", null,
            loop.ExecIn("In"), loop.ExecOut("Out"), loop.ExecOut("Body"),
            loop.DataIn("Start", "int32", "0"), loop.DataIn("Count", "int32"), loop.DataOut("Index", "int32"));
        var tick = loop.Node("Native.Call", "Tick", new Dictionary<string, string> { ["Method"] = "Probe.Tick" }, loop.ExecIn("In"));
        loop.Wire(entry, "Out", forNode, "In");
        loop.Wire(count, "Value", forNode, "Count");
        loop.Wire(forNode, "Body", tick, "In");
        host.SetVariableDirect("Count", AstraValue.FromInt64(3));
        Execute(host, Compile(loop.Document("Repeat")), "Repeat");
        Assert.That(ticks, Is.EqualTo(3));

        var each = new BuiltGraph();
        var eachEntry = each.Node("Event.Batch", "Batch", null, each.ExecOut("Out"), each.DataOut("Event", "object"));
        var hits = each.Node("Native.GetMember", "Hits", new Dictionary<string, string> { ["Member"] = "HitEntities" },
            each.DataIn("Target", "object"), each.DataOut("Value", "List<EntityUid>"));
        var forEach = each.Node("Flow.ForEach", "Each", null,
            each.ExecIn("In"), each.ExecOut("Out"), each.ExecOut("Body"),
            each.DataIn("Collection", "List<EntityUid>"), each.DataOut("Current", "EntityUid"));
        var item = each.Node("Native.Call", "Item", new Dictionary<string, string> { ["Method"] = "Probe.Item" },
            each.ExecIn("In"), each.DataIn("Entity", "EntityUid"));
        each.Wire(eachEntry, "Out", forEach, "In");
        each.Wire(eachEntry, "Event", hits, "Target");
        each.Wire(hits, "Value", forEach, "Collection");
        each.Wire(forEach, "Body", item, "In");
        each.Wire(forEach, "Current", item, "Entity");
        host.PushEventContext(new AstraEventInvocationContext(
            AstraValue.FromEntityUid(1),
            null,
            new HitListProbe { HitEntities = [new EntityUid(4), new EntityUid(5)] }));
        Execute(host, Compile(each.Document("Batch")), "Batch");
        Assert.That(seen, Is.EqualTo(new[] { 4, 5 }));
    }

    [Test]
    public void Nullable_SkipsMissingWeapon_AndUnwrapsPresentWeapon()
    {
        var host = new DefaultVmHostServices();
        var seen = new List<int>();
        host.RegisterNativeMethod("Probe.Weapon", args =>
        {
            seen.Add(args[0].AsEntityUid());
            return AstraValue.Null;
        });
        var program = Compile(NullableWeaponGraph().Document("Projectile"));
        host.PushEventContext(new AstraEventInvocationContext(AstraValue.Null, null, new WeaponProbe { Weapon = null }));
        Execute(host, program, "Projectile");
        Assert.That(seen, Is.Empty);

        host.PushEventContext(new AstraEventInvocationContext(AstraValue.Null, null, new WeaponProbe { Weapon = new EntityUid(12) }));
        Execute(host, program, "Projectile");
        Assert.That(seen, Is.EqualTo(new[] { 12 }));
    }

    [Test]
    public void Scheduler_RunsUpdateAndStartup_AndSkipsNativeEvents()
    {
        var services = new DefaultVmHostServices();
        var counts = new Dictionary<string, int> { ["start"] = 0, ["update"] = 0, ["tick"] = 0 };
        services.RegisterNativeMethod("Probe.Start", _ => Count(counts, "start"));
        services.RegisterNativeMethod("Probe.Update", _ => Count(counts, "update"));
        services.RegisterNativeMethod("Probe.Tick", _ => Count(counts, "tick"));

        var graph = new BuiltGraph();
        var start = graph.Node("Event.Start", "Start", null, graph.ExecOut("Out"));
        var update = graph.Node("System.Update", "Update", null, graph.ExecOut("Out"));
        var tick = graph.Node("Event.Tick", "Tick", null, graph.ExecOut("Out"));
        graph.Wire(start, "Out", graph.Node("Native.Call", "MarkStart", new Dictionary<string, string> { ["Method"] = "Probe.Start" }, graph.ExecIn("In")), "In");
        graph.Wire(update, "Out", graph.Node("Native.Call", "MarkUpdate", new Dictionary<string, string> { ["Method"] = "Probe.Update" }, graph.ExecIn("In")), "In");
        graph.Wire(tick, "Out", graph.Node("Native.Call", "MarkTick", new Dictionary<string, string> { ["Method"] = "Probe.Tick" }, graph.ExecIn("In")), "In");

        var root = Path.Combine(Path.GetTempPath(), "agraph-sched-" + Guid.NewGuid().ToString("N"));
        var loader = new BootstrapLoader(new StorageLayout(Path.Combine(root, "project"), Path.Combine(root, "data")));
        loader.SaveLive(graph.Document("Scheduler", GraphKind.System), "scheduler.agraph");
        var host = new AstraGraphHost(hostServices: services);
        var activated = new AstraBootstrapService(host, new HotReloadManager(host), loader).Activate();
        Assert.That(activated, Is.EqualTo(1));
        host.Update(0, 1);
        host.Update(0, 2);
        Assert.That(counts["start"], Is.EqualTo(1));
        Assert.That(counts["update"], Is.EqualTo(2));
        Assert.That(counts["tick"], Is.EqualTo(0));
    }

    [Test]
    public void HotReload_ReplacesGraphLogic_WithoutStaticEventState()
    {
        var services = new DefaultVmHostServices();
        var marks = new List<string>();
        services.RegisterNativeMethod("Probe.Mark", args =>
        {
            marks.Add(args[0].AsString() ?? "");
            return AstraValue.Null;
        });
        var host = new AstraGraphHost(hostServices: services);
        var reload = new HotReloadManager(host);
        var id = GraphId.New();
        var first = MarkGraph(id, "v1");
        Assert.That(reload.Publish(first, "Admin", "v1").Success, Is.True);
        ExecuteHost(host, id, "Marker");
        var second = MarkGraph(id, "v2");
        Assert.That(reload.Publish(second, "Admin", "v2").Success, Is.True);
        ExecuteHost(host, id, "Marker");
        Assert.That(marks, Is.EqualTo(new[] { "v1", "v2" }));

        var restarted = new AstraGraphHost(hostServices: services);
        Assert.That(new HotReloadManager(restarted).Publish(second, "Admin", "restart").Success, Is.True);
        ExecuteHost(restarted, id, "Marker");
        Assert.That(marks, Is.EqualTo(new[] { "v1", "v2", "v2" }));
    }

    [Test]
    public void WeaponMothroach_MeleeProjectileAndHitscan_SpawnCountAndDeleteTarget()
    {
        var schema = MothroachSchema(out _, out _);
        var registry = new AstraSchemaRegistry();
        registry.RegisterSchema(schema);
        var source = new SchemaComponentSource(new DynamicComponentStore(), registry);
        Assert.That(source.ApplyInitial(7, schema, new Dictionary<string, string> { ["spawn"] = "MobMothroach", ["count"] = "3" }, "WeaponFoo", out _), Is.True);

        var host = new DefaultVmHostServices();
        var spawned = new List<(string? Prototype, int Target)>();
        var deleted = new List<int>();
        host.UseSchemaComponents(source);
        host.RegisterNativeMethod("Entity.GetCoordinates", args => AstraValue.FromInt64(args[0].AsEntityUid()));
        host.RegisterNativeMethod("Entity.SpawnAt", args =>
        {
            spawned.Add((args[0].AsString(), (int)args[1].AsInt64()));
            return AstraValue.FromEntityUid(100 + spawned.Count);
        });
        host.RegisterNativeMethod("Entity.QueueDelete", args =>
        {
            deleted.Add(args[0].AsEntityUid());
            return AstraValue.Null;
        });
        host.RegisterNativeMethod("Entity.Exists", args => AstraValue.FromBool(args[0].AsEntityUid() != 0));
        var replace = Compile(ReplaceEntityGraph().Document("ReplaceEntity", GraphKind.Function));
        host.RegisterGraphFunction("ReplaceEntity", replace, replace.FindEntryPoint("Replace")!);

        var weapon = Compile(WeaponGraph(schema).Document("WeaponMothroach"));
        host.PushEventContext(new AstraEventInvocationContext(
            AstraValue.FromEntityUid(7),
            null,
            new MeleeProbe
            {
                User = new EntityUid(1),
                HitEntities = [new EntityUid(1), new EntityUid(7), new EntityUid(20)]
            }));
        Execute(host, weapon, "Melee");
        Assert.That(spawned, Is.EqualTo(new[] { ("MobMothroach", 20), ("MobMothroach", 20), ("MobMothroach", 20) }));
        Assert.That(deleted, Is.EqualTo(new[] { 20 }));

        spawned.Clear();
        deleted.Clear();
        host.PushEventContext(new AstraEventInvocationContext(
            AstraValue.Null,
            null,
            new ProjectileProbe { Weapon = new EntityUid(7), Target = new EntityUid(30) }));
        Execute(host, weapon, "Projectile");
        Assert.That(spawned.Count, Is.EqualTo(3));
        Assert.That(deleted, Is.EqualTo(new[] { 30 }));

        spawned.Clear();
        deleted.Clear();
        host.PushEventContext(new AstraEventInvocationContext(
            AstraValue.Null,
            null,
            new HitscanProbe { Data = new HitscanData { Gun = new EntityUid(7), HitEntity = new EntityUid(40) } }));
        Execute(host, weapon, "Hitscan");
        Assert.That(spawned.Count, Is.EqualTo(3));
        Assert.That(spawned.All(item => item.Prototype == "MobMothroach" && item.Target == 40), Is.True);
        Assert.That(deleted, Is.EqualTo(new[] { 40 }));
    }

    private static AstraValue Count(Dictionary<string, int> counts, string name)
    {
        counts[name]++;
        return AstraValue.Null;
    }

    private static SchemaType MothroachSchema(out FieldId spawn, out FieldId count)
    {
        spawn = FieldId.New();
        count = FieldId.New();
        return SchemaDocuments.ToSchema(new ComponentSchemaDocument
        {
            Name = "MothroachStrike",
            Kind = "Component",
            Fields =
            [
                new ComponentFieldDocument { Id = spawn, Name = "spawn", TypeName = "EntProtoId<EntityPrototype>", DefaultValue = "MobMothroach" },
                new ComponentFieldDocument { Id = count, Name = "count", TypeName = "int32", DefaultValue = "1" }
            ]
        }, TypeRegistry.CreateDefault());
    }

    private static int Index(SchemaType schema, FieldId field) =>
        schema.Fields.ToList().FindIndex(item => item.Id == field);

    private static BuiltGraph NullableWeaponGraph()
    {
        var graph = new BuiltGraph();
        var entry = graph.Node("Event.Projectile", "Projectile", null, graph.ExecOut("Out"), graph.DataOut("Event", "object"));
        var weapon = graph.Node("Native.GetMember", "Weapon", new Dictionary<string, string> { ["Member"] = "Weapon" },
            graph.DataIn("Target", "object"), graph.DataOut("Value", "EntityUid?"));
        var has = graph.Node("Nullable.HasValue", "Has", null, graph.DataIn("Value", "EntityUid?"), graph.DataOut("HasValue", "bool"));
        var unwrap = graph.Node("Nullable.GetValue", "Unwrap", null, graph.DataIn("Value", "EntityUid?"), graph.DataOut("ValueOut", "EntityUid"));
        var branch = graph.Node("Core.Branch", "IfWeapon", null,
            graph.ExecIn("In"), graph.ExecOut("True"), graph.ExecOut("False"), graph.DataIn("Condition", "bool"));
        var call = graph.Node("Native.Call", "See", new Dictionary<string, string> { ["Method"] = "Probe.Weapon" },
            graph.ExecIn("In"), graph.DataIn("Entity", "EntityUid"));
        graph.Wire(entry, "Out", branch, "In");
        graph.Wire(entry, "Event", weapon, "Target");
        graph.Wire(weapon, "Value", has, "Value");
        graph.Wire(weapon, "Value", unwrap, "Value");
        graph.Wire(has, "HasValue", branch, "Condition");
        graph.Wire(branch, "True", call, "In");
        graph.Wire(unwrap, "ValueOut", call, "Entity");
        return graph;
    }

    private static BuiltGraph ReplaceEntityGraph()
    {
        var graph = new BuiltGraph();
        graph.Variables.Add(new GraphVariableDocument { Name = "Target", TypeName = "EntityUid", IsParameter = true });
        graph.Variables.Add(new GraphVariableDocument { Name = "Prototype", TypeName = "EntProtoId", IsParameter = true });
        graph.Variables.Add(new GraphVariableDocument { Name = "Count", TypeName = "int32", IsParameter = true });
        var entry = graph.Node("Function.ReplaceEntity", "Replace", null, graph.ExecOut("Out"));
        var target = graph.Node("Core.VariableRead", "Target", new Dictionary<string, string> { ["VariableName"] = "Target" }, graph.DataOut("Value", "EntityUid"));
        var prototype = graph.Node("Core.VariableRead", "Prototype", new Dictionary<string, string> { ["VariableName"] = "Prototype" }, graph.DataOut("Value", "EntProtoId"));
        var count = graph.Node("Core.VariableRead", "Count", new Dictionary<string, string> { ["VariableName"] = "Count" }, graph.DataOut("Value", "int32"));
        var coordinates = graph.Node("Native.Call", "Coordinates", new Dictionary<string, string> { ["Method"] = "Entity.GetCoordinates" },
            graph.DataIn("Entity", "EntityUid"), graph.DataOut("Value", "int64"));
        var loop = graph.Node("Flow.For", "SpawnLoop", null,
            graph.ExecIn("In"), graph.ExecOut("Out"), graph.ExecOut("Body"),
            graph.DataIn("Start", "int32", "0"), graph.DataIn("Count", "int32"));
        var spawn = graph.Node("Native.Call", "Spawn", new Dictionary<string, string> { ["Method"] = "Entity.SpawnAt" },
            graph.ExecIn("In"), graph.DataIn("Prototype", "EntProtoId"), graph.DataIn("Coordinates", "int64"));
        var delete = graph.Node("Native.Call", "Delete", new Dictionary<string, string> { ["Method"] = "Entity.QueueDelete" },
            graph.ExecIn("In"), graph.DataIn("Entity", "EntityUid"));
        graph.Wire(entry, "Out", loop, "In");
        graph.Wire(count, "Value", loop, "Count");
        graph.Wire(loop, "Body", spawn, "In");
        graph.Wire(loop, "Out", delete, "In");
        graph.Wire(target, "Value", coordinates, "Entity");
        graph.Wire(prototype, "Value", spawn, "Prototype");
        graph.Wire(coordinates, "Value", spawn, "Coordinates");
        graph.Wire(target, "Value", delete, "Entity");
        return graph;
    }

    private static BuiltGraph WeaponGraph(SchemaType schema)
    {
        var graph = new BuiltGraph();
        graph.Schemas.Add(new ComponentSchemaDocument
        {
            Id = schema.Id,
            Name = schema.Name,
            Kind = "Component",
            Fields = schema.Fields.Select(field => new ComponentFieldDocument
            {
                Id = field.Id,
                Name = field.Name,
                TypeName = field.Name == "spawn" ? "EntProtoId<EntityPrototype>" : "int32",
                DefaultValue = field.DefaultValue
            }).ToList()
        });
        var melee = graph.Node("Event.MeleeHit", "Melee", null, graph.ExecOut("Out"), graph.DataOut("Entity", "EntityUid"), graph.DataOut("Event", "object"));
        var projectile = graph.Node("Event.ProjectileHit", "Projectile", null, graph.ExecOut("Out"), graph.DataOut("Event", "object"));
        var hitscan = graph.Node("Event.Hitscan", "Hitscan", null, graph.ExecOut("Out"), graph.DataOut("Event", "object"));
        graph.Wire(melee, "Out", MeleeBody(graph, melee), "In");
        graph.Wire(projectile, "Out", ProjectileBody(graph, projectile), "In");
        graph.Wire(hitscan, "Out", HitscanBody(graph, hitscan), "In");
        return graph;
    }

    private static NodeDocument MeleeBody(BuiltGraph graph, NodeDocument entry)
    {
        var hits = Member(graph, "HitEntities", "List<EntityUid>");
        var user = Member(graph, "User", "EntityUid");
        var each = graph.Node("Flow.ForEach", "MeleeEach", null,
            graph.ExecIn("In"), graph.ExecOut("Body"), graph.DataIn("Collection", "List<EntityUid>"), graph.DataOut("Current", "EntityUid"));
        var notUser = Compare(graph, "NotUser");
        var notWeapon = Compare(graph, "NotWeapon");
        var userGate = Branch(graph, "UserGate");
        var weaponGate = Branch(graph, "WeaponGate");
        var existsGate = Branch(graph, "ExistsGate");
        var foundGate = Branch(graph, "FoundGate");
        var exists = graph.Node("Native.Call", "Exists", new Dictionary<string, string> { ["Method"] = "Entity.Exists" },
            graph.DataIn("Entity", "EntityUid"), graph.DataOut("Value", "bool"));
        var component = TryGet(graph, "MeleeComponent");
        var call = CallReplace(graph, "MeleeReplace", out var prototype, out var count);
        graph.Wire(entry, "Event", hits, "Target");
        graph.Wire(entry, "Event", user, "Target");
        graph.Wire(hits, "Value", each, "Collection");
        graph.Wire(each, "Body", userGate, "In");
        graph.Wire(each, "Current", notUser, "A");
        graph.Wire(user, "Value", notUser, "B");
        graph.Wire(notUser, "Result", userGate, "Condition");
        graph.Wire(userGate, "True", weaponGate, "In");
        graph.Wire(each, "Current", notWeapon, "A");
        graph.Wire(entry, "Entity", notWeapon, "B");
        graph.Wire(notWeapon, "Result", weaponGate, "Condition");
        graph.Wire(weaponGate, "True", existsGate, "In");
        graph.Wire(each, "Current", exists, "Entity");
        graph.Wire(exists, "Value", existsGate, "Condition");
        graph.Wire(existsGate, "True", foundGate, "In");
        graph.Wire(entry, "Entity", component, "Entity");
        graph.Wire(component, "Found", foundGate, "Condition");
        graph.Wire(foundGate, "True", call, "In");
        graph.Wire(each, "Current", call, "Target");
        graph.Wire(component, "Component", prototype, "Component");
        graph.Wire(component, "Component", count, "Component");
        return each;
    }

    private static NodeDocument ProjectileBody(BuiltGraph graph, NodeDocument entry)
    {
        var weapon = Member(graph, "Weapon", "EntityUid?");
        var target = Member(graph, "Target", "EntityUid");
        var has = graph.Node("Nullable.HasValue", "ProjectileHas", null, graph.DataIn("Value", "EntityUid?"), graph.DataOut("HasValue", "bool"));
        var unwrap = graph.Node("Nullable.GetValue", "ProjectileUnwrap", null, graph.DataIn("Value", "EntityUid?"), graph.DataOut("ValueOut", "EntityUid"));
        var gate = Branch(graph, "ProjectileGate");
        var foundGate = Branch(graph, "ProjectileFound");
        var component = TryGet(graph, "ProjectileComponent");
        var call = CallReplace(graph, "ProjectileReplace", out var prototype, out var count);
        graph.Wire(entry, "Event", weapon, "Target");
        graph.Wire(entry, "Event", target, "Target");
        graph.Wire(weapon, "Value", has, "Value");
        graph.Wire(weapon, "Value", unwrap, "Value");
        graph.Wire(has, "HasValue", gate, "Condition");
        graph.Wire(gate, "True", foundGate, "In");
        graph.Wire(unwrap, "ValueOut", component, "Entity");
        graph.Wire(component, "Found", foundGate, "Condition");
        graph.Wire(foundGate, "True", call, "In");
        graph.Wire(target, "Value", call, "Target");
        graph.Wire(component, "Component", prototype, "Component");
        graph.Wire(component, "Component", count, "Component");
        return gate;
    }

    private static NodeDocument HitscanBody(BuiltGraph graph, NodeDocument entry)
    {
        var data = Member(graph, "Data", "object");
        var gun = Member(graph, "Gun", "EntityUid");
        var hit = Member(graph, "HitEntity", "EntityUid");
        var component = TryGet(graph, "HitscanComponent");
        var call = CallReplace(graph, "HitscanReplace", out var prototype, out var count);
        graph.Wire(entry, "Event", data, "Target");
        graph.Wire(data, "Value", gun, "Target");
        graph.Wire(data, "Value", hit, "Target");
        graph.Wire(gun, "Value", component, "Entity");
        graph.Wire(component, "Component", prototype, "Component");
        graph.Wire(component, "Component", count, "Component");
        graph.Wire(hit, "Value", call, "Target");
        return call;
    }

    private static NodeDocument Member(BuiltGraph graph, string member, string type) =>
        graph.Node("Native.GetMember", member, new Dictionary<string, string> { ["Member"] = member },
            graph.DataIn("Target", "object"), graph.DataOut("Value", type));

    private static NodeDocument Compare(BuiltGraph graph, string name) =>
        graph.Node("cmp.notequal", name, null, graph.DataIn("A", "EntityUid"), graph.DataIn("B", "EntityUid"), graph.DataOut("Result", "bool"));

    private static NodeDocument Branch(BuiltGraph graph, string name) =>
        graph.Node("Core.Branch", name, null, graph.ExecIn("In"), graph.ExecOut("True"), graph.ExecOut("False"), graph.DataIn("Condition", "bool"));

    private static NodeDocument TryGet(BuiltGraph graph, string name) =>
        graph.Node("Entity.TryGetComponent", name, new Dictionary<string, string> { ["ComponentType"] = "MothroachStrike" },
            graph.DataIn("Entity", "EntityUid"), graph.DataOut("Found", "bool"), graph.DataOut("Component", "component"));

    private static NodeDocument CallReplace(BuiltGraph graph, string name, out NodeDocument prototype, out NodeDocument count)
    {
        prototype = graph.Node("Schema.GetField", name + "Spawn", new Dictionary<string, string> { ["Schema"] = "MothroachStrike", ["Field"] = "spawn" },
            graph.DataIn("Component", "component"), graph.DataOut("Value", "EntProtoId"));
        count = graph.Node("Schema.GetField", name + "Count", new Dictionary<string, string> { ["Schema"] = "MothroachStrike", ["Field"] = "count" },
            graph.DataIn("Component", "component"), graph.DataOut("Value", "int32"));
        var call = graph.Node("Graph.Call", name, new Dictionary<string, string> { ["Function"] = "ReplaceEntity" },
            graph.ExecIn("In"), graph.DataIn("Target", "EntityUid"), graph.DataIn("Prototype", "EntProtoId"), graph.DataIn("Count", "int32"));
        graph.Wire(prototype, "Value", call, "Prototype");
        graph.Wire(count, "Value", call, "Count");
        return call;
    }

    private static GraphDocument MarkGraph(GraphId id, string value)
    {
        var graph = new BuiltGraph();
        var entry = graph.Node("Event.Mark", "Marker", null, graph.ExecOut("Out"));
        var call = graph.Node("Native.Call", "Mark", new Dictionary<string, string> { ["Method"] = "Probe.Mark" },
            graph.ExecIn("In"), graph.DataIn("Value", "string", value));
        graph.Wire(entry, "Out", call, "In");
        var document = graph.Document("Mark");
        return new GraphDocument
        {
            Id = id,
            Name = document.Name,
            Kind = document.Kind,
            Side = document.Side,
            Nodes = document.Nodes,
            Connections = document.Connections
        };
    }

    private static BytecodeProgram Compile(GraphDocument document)
    {
        var result = new SemanticAnalyzer(TypeRegistry.CreateDefault()).Analyze(document);
        Assert.That(result.Success, Is.True, result.Diagnostics.ToString());
        Assert.That(result.Program, Is.Not.Null);
        return IrToBytecodeCompiler.Compile(AstToIrCompiler.Compile(result.Program!), RevisionId.New(), "test");
    }

    private static void Execute(DefaultVmHostServices host, BytecodeProgram program, string entry)
    {
        var function = program.FindEntryPoint(entry);
        Assert.That(function, Is.Not.Null, entry);
        var result = new AstraVm().Execute(program, function!, hostServices: host);
        Assert.That(result.Status, Is.EqualTo(VmExecutionStatus.Completed), result.Exception?.ToString());
    }

    private static void ExecuteHost(AstraGraphHost host, GraphId id, string entry)
    {
        var program = host.GetProgram(id);
        Assert.That(program, Is.Not.Null);
        var function = program!.FindEntryPoint(entry);
        var result = host.Vm.Execute(program, function!, hostServices: host.HostServices);
        Assert.That(result.Status, Is.EqualTo(VmExecutionStatus.Completed), result.Exception?.ToString());
    }

    private sealed class BuiltGraph
    {
        public List<GraphVariableDocument> Variables { get; } = [];
        public List<ComponentSchemaDocument> Schemas { get; } = [];
        private readonly List<NodeDocument> _nodes = [];
        private readonly List<ConnectionDocument> _connections = [];

        public PinSpec ExecIn(string name) => new(name, PinDirection.Input, PinKind.Execution, "", null);
        public PinSpec ExecOut(string name) => new(name, PinDirection.Output, PinKind.Execution, "", null);
        public PinSpec DataIn(string name, string type, string? value = null) => new(name, PinDirection.Input, PinKind.Data, type, value);
        public PinSpec DataOut(string name, string type) => new(name, PinDirection.Output, PinKind.Data, type, null);

        public NodeDocument Node(string type, string name, Dictionary<string, string>? properties, params PinSpec[] pins)
        {
            var node = new NodeDocument
            {
                Name = name,
                NodeType = type,
                Properties = properties ?? [],
                Pins = pins.Select(pin => new PinDocument
                {
                    Name = pin.Name,
                    Direction = pin.Direction,
                    Kind = pin.Kind,
                    DataType = pin.Type,
                    DefaultValue = pin.DefaultValue
                }).ToList()
            };
            _nodes.Add(node);
            return node;
        }

        public void Wire(NodeDocument from, string fromPin, NodeDocument to, string toPin)
        {
            _connections.Add(new ConnectionDocument
            {
                FromNode = from.Id,
                FromPin = from.FindPin(fromPin, PinDirection.Output)!.Id,
                ToNode = to.Id,
                ToPin = to.FindPin(toPin, PinDirection.Input)!.Id
            });
        }

        public GraphDocument Document(string name, GraphKind kind = GraphKind.System) => new()
        {
            Name = name,
            Kind = kind,
            Side = GraphSide.Server,
            Variables = Variables,
            Schemas = Schemas,
            Nodes = _nodes,
            Connections = _connections
        };
    }

    private readonly record struct PinSpec(string Name, PinDirection Direction, PinKind Kind, string Type, string? DefaultValue);
}
