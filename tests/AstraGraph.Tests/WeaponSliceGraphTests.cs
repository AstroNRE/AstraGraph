using System.Reflection;
using AstraGraph.Core;
using AstraGraph.State;
using AstraGraph.VM;
using NUnit.Framework;

namespace AstraGraph.Tests;

[TestFixture]
public sealed class WeaponSliceGraphTests
{
    [Test]
    public void WeaponProfile_Analyzes()
    {
        var path = Path.Combine(NightCity(), "Resources", "AstraGraph", "Systems", "Weapons", "Modular", "WeaponProfile.agraph");
        if (!File.Exists(path))
        {
            Assert.Ignore("Night City checkout is not beside this library.");
        }

        var document = GraphSerializer.Deserialize(File.ReadAllText(path));
        var analyzed = new SemanticAnalyzer(TypeRegistry.CreateDefault()).Analyze(document);
        Assert.That(analyzed.Success, Is.True, analyzed.Diagnostics.ToString());
    }

    [Test]
    public void WeaponProfile_CopiesAssemblyRateOntoTheRefreshEvent()
    {
        var path = Path.Combine(NightCity(), "Resources", "AstraGraph", "Systems", "Weapons", "Modular", "WeaponProfile.agraph");
        if (!File.Exists(path))
        {
            Assert.Ignore("Night City checkout is not beside this library.");
        }

        var document = GraphSerializer.Deserialize(File.ReadAllText(path));
        var registry = TypeRegistry.CreateDefault();
        var analyzed = new SemanticAnalyzer(registry).Analyze(document);
        Assert.That(analyzed.Success, Is.True, analyzed.Diagnostics.ToString());
        var program = IrToBytecodeCompiler.Compile(AstToIrCompiler.Compile(analyzed.Program!), RevisionId.New(), "profile");
        var schema = SchemaDocuments.ToSchema(document.Schemas.Single(item => item.Name == "WeaponAssembly"), registry);
        var schemas = new AstraSchemaRegistry();
        schemas.RegisterSchema(schema);
        var source = new SchemaComponentSource(new DynamicComponentStore(), schemas);
        Assert.That(source.ApplyInitial(4, schema, new Dictionary<string, string>
        {
            ["Barrel"] = "WeaponBarrelHeavy",
            ["FireRate"] = "2",
            ["ProjectileSpeed"] = "55"
        }, "WeaponAstraPistol", out _), Is.True);

        var services = new DefaultVmHostServices();
        services.UseSchemaComponents(source);
        var ev = new ProfileRateEvent(6f, 40f);
        object box = ev;
        services.ClearVariables();
        services.PushEventContext(new AstraEventInvocationContext(AstraValue.FromEntityUid(4), null, box));
        var result = new AstraVm().Execute(program, program.FindEntryPoint("Refresh")!, hostServices: services);
        services.PopEventContext();
        Assert.That(result.Status, Is.EqualTo(VmExecutionStatus.Completed), result.Exception?.ToString());

        foreach (var property in box.GetType().GetProperties())
        {
            if (property.GetIndexParameters().Length != 0 || !property.CanRead)
            {
                continue;
            }

            var value = services.GetVariable(SymbolId.Empty, property.Name);
            if (value.Type != AstraValueType.Null)
            {
                AstraValueBox.WriteMember(box, property.Name, value);
            }
        }

        ev = (ProfileRateEvent)box;
        Assert.That(ev.FireRate, Is.EqualTo(2f));
        Assert.That(ev.ProjectileSpeed, Is.EqualTo(55f));
    }

    private readonly record struct ProfileRateEvent(float FireRate, float ProjectileSpeed);

    [Test]
    public void HostVariables_NestedClearRestoresTheCaller()
    {
        var services = new DefaultVmHostServices();
        services.SetVariable(SymbolId.Empty, "Rows", AstraValue.FromString("barrels"));
        services.PushVariables();
        services.ClearVariables();
        services.SetVariable(SymbolId.Empty, "FireRate", AstraValue.FromDouble(2));
        Assert.That(services.GetVariable(SymbolId.Empty, "Rows").Type, Is.EqualTo(AstraValueType.Null));
        services.PopVariables();
        Assert.That(services.GetVariable(SymbolId.Empty, "Rows").AsString(), Is.EqualTo("barrels"));
        Assert.That(services.GetVariable(SymbolId.Empty, "FireRate").Type, Is.EqualTo(AstraValueType.Null));
    }

    [Test]
    public void WeaponBench_EmitsTheGunRateWrite()
    {
        var document = WeaponBench();
        var analyzed = new SemanticAnalyzer(TypeRegistry.CreateDefault()).Analyze(document);
        Assert.That(analyzed.Success, Is.True, analyzed.Diagnostics.ToString());
        var program = IrToBytecodeCompiler.Compile(AstToIrCompiler.Compile(analyzed.Program!), RevisionId.New(), "bench");
        var calls = new HashSet<string>(StringComparer.Ordinal);
        foreach (var function in program.EntryPoints)
        {
            foreach (var instruction in function.Instructions)
            {
                if (instruction.AsOpCode != IrOpCode.CallNative)
                {
                    continue;
                }

                if (program.Constants[instruction.Op1].Value is string name)
                {
                    calls.Add(name);
                }
            }
        }

        Assert.That(calls, Does.Contain("Component.SetField"));
        Assert.That(calls, Does.Contain("System.Invoke"));
        Assert.That(calls, Does.Contain("Meta.SetDescription"));
        Assert.That(calls, Does.Contain("Text.WithNumber"));
        var opcodes = new HashSet<IrOpCode>();
        foreach (var function in program.EntryPoints)
        {
            foreach (var instruction in function.Instructions)
            {
                opcodes.Add(instruction.AsOpCode);
            }
        }

        Assert.That(opcodes, Does.Not.Contain(IrOpCode.CollectionIntersects));
        Assert.That(opcodes, Does.Not.Contain(IrOpCode.Select));
        Assert.That(opcodes, Does.Contain(IrOpCode.Or));
        Assert.That(opcodes, Does.Contain(IrOpCode.And));
    }

    [Test]
    public void ListMembership_MatchesTags()
    {
        var accepts = AstraValue.FromObject(new AstraList([AstraValue.FromString("pistol-barrel"), AstraValue.FromString("pistol-bolt")]));
        var provides = AstraValue.FromObject(new AstraList([AstraValue.FromString("pistol-barrel")]));
        var adapts = AstraValue.FromObject(new AstraList([AstraValue.FromString("pistol-muzzle")]));
        Assert.That(AstraValues.CollectionIntersects(provides, accepts), Is.True);
        Assert.That(AstraValues.CollectionIntersects(adapts, accepts), Is.False);
        Assert.That(AstraValues.CollectionContainsAll(accepts, provides), Is.True);
    }

    [Test]
    public void SchemaYaml_BindsAStringList()
    {
        var schema = SchemaDocuments.ToSchema(new ComponentSchemaDocument
        {
            Name = "WeaponAssembly",
            Kind = "Component",
            Fields = [new ComponentFieldDocument { Name = "Accepts", TypeName = "List<string>" }]
        }, TypeRegistry.CreateDefault());
        var bound = SchemaYamlBinder.Bind(schema, new Dictionary<string, object?>
        {
            ["Accepts"] = "pistol-barrel\u001fpistol-bolt"
        });
        Assert.That(bound.Success, Is.True, string.Join("; ", bound.Diagnostics));
        Assert.That(AstraValues.CollectionContains(bound.Values[0], AstraValue.FromString("pistol-bolt")), Is.True);
    }

    [Test]
    public void WeaponBench_AnalyzesAndIsStoredLeftToRight()
    {
        var document = WeaponBench();
        var analyzed = new SemanticAnalyzer(TypeRegistry.CreateDefault()).Analyze(document);
        Assert.That(analyzed.Success, Is.True, analyzed.Diagnostics.ToString());

        var root = NightCity();
        if (!Directory.Exists(root))
        {
            return;
        }

        var path = Path.Combine(root, "Resources", "AstraGraph", "Systems", "Weapons", "Modular", "WeaponBench.agraph");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, GraphSerializer.Serialize(document));
    }

    private static GraphDocument WeaponBench()
    {
        var graph = new BenchGraph();
        var open = graph.Event("Open", "BoundUIOpenedEvent", "AstraHtmlUiComponent", 40, 640);
        var message = graph.Event("Message", "AstraBuiUiMessage", "AstraHtmlUiComponent", 40, 80);
        var action = graph.Member("Action", "Action", "string", 360);
        var isInstall = graph.Equal("IsInstall", "InstallPart", 520);
        var installGate = graph.Branch("Install?", 680);
        var payload = graph.Member("Payload", "Payload", "string", 820);
        var partId = graph.Call("Bui.Field", "PartId", 1040, ("Payload", "string", null), ("Field", "string", "id"), ("Value", "string", null));
        var part = graph.Call("Inventory.Find", "Part", 1260, ("Owner", "EntityUid", null), ("Prototype", "string", null), ("Item", "int64", null));
        var hasPart = graph.NotZero("HasPart", 1480);
        var partGate = graph.Branch("Part?", 1480);
        var noPart = graph.Call("Bui.Set", "NoPart", 1680, ("Owner", "EntityUid", null), ("Name", "string", "Reason"), ("Value", "string", "Select a part in the list"), ("Success", "bool", null));
        var gun = graph.Call("Entity.GetHeldItem", "Gun", 1900, ("Holder", "EntityUid", null), ("Container", "string", "gun"), ("Item", "int64", null));
        var hasGun = graph.NotZero("HasGun", 2120);
        var gunGate = graph.Branch("Gun?", 2120);
        var noGun = graph.Call("Bui.Set", "NoGun", 2320, ("Owner", "EntityUid", null), ("Name", "string", "Reason"), ("Value", "string", "Put the pistol in the gun slot"), ("Success", "bool", null));
        var partComp = graph.Component("PartData", "WeaponPart", 2400, "int64");
        var partDataGate = graph.Branch("PartData?", 2400);
        var slot = graph.Field("Slot", "WeaponPart", "Slot", "string", 2540);
        var assembly = graph.Component("Assembly", "WeaponAssembly", 2780, "int64");
        var assemblyGate = graph.Branch("Assembly?", 2780);
        graph.UseRow(900);
        var installFit = graph.Fit("Install", 3000, partComp, assembly);
        var fitLabel = graph.Field("FitLabel", "WeaponPart", "Label", "string", 5200);
        var fitArrow = graph.Literal("FitArrow", "string", " -> ", 5420);
        var fitLeft = graph.Add("FitLeft", 5640, "string");
        var fitWhere = graph.Add("FitWhere", 5860, "string");
        var fitting = graph.Call("Bui.Set", "Fitting", 6080, ("Owner", "EntityUid", null), ("Name", "string", "Reason"), ("Value", "string", null), ("Success", "bool", null));
        var slotHeld = graph.Call("Entity.GetHeldItem", "SlotHeld", 6300, ("Holder", "int64", null), ("Container", "string", null), ("Item", "int64", null));
        var slotFull = graph.NotZero("SlotFull", 6400);
        var slotFullGate = graph.Branch("SlotFull?", 6400);
        var slotFullReason = graph.Call("Bui.Set", "SlotFullReason", 6600, ("Owner", "EntityUid", null), ("Name", "string", "Reason"), ("Value", "string", "Remove the fitted part first"), ("Success", "bool", null));
        var insert = graph.Call("Container.Insert", "Insert", 6820, ("Owner", "int64", null), ("Container", "string", null), ("Item", "int64", null), ("Success", "bool", null));
        var inserted = graph.Branch("Inserted?", 6740);
        var installedMsg = graph.Call("Bui.Set", "InstalledMsg", 6960, ("Owner", "EntityUid", null), ("Name", "string", "Reason"), ("Value", "string", "Installed."), ("Success", "bool", null));
        var rejectPrefix = graph.Literal("RejectPrefix", "string", "Did not fit into ", 6960);
        var rejectText = graph.Add("RejectText", 7180, "string");
        var rejected = graph.Call("Bui.Set", "Rejected", 7400, ("Owner", "EntityUid", null), ("Name", "string", "Reason"), ("Value", "string", null), ("Success", "bool", null));
        graph.UseRow(0);
        var afterInstall = graph.Refresh("Done", 4640, partComp);
        graph.UseRow(560);
        var onOpen = graph.Refresh("Open", 360);
        graph.UseRow(1480);
        var isRemove = graph.Equal("IsRemove", "RemovePart", 680);
        var removeGate = graph.Branch("Remove?", 900);
        var removeGun = graph.Call("Entity.GetHeldItem", "RemoveGun", 1120, ("Holder", "EntityUid", null), ("Container", "string", "gun"), ("Item", "int64", null));
        var hasRemoveGun = graph.NotZero("HasRemoveGun", 1340);
        var removeGunGate = graph.Branch("RemoveGun?", 1560);
        var noRemoveGun = graph.Call("Bui.Set", "NoRemoveGun", 1780, ("Owner", "EntityUid", null), ("Name", "string", "Reason"), ("Value", "string", "Put the pistol in the gun slot"), ("Success", "bool", null));
        var removePart = graph.Call("Inventory.Find", "RemovePartItem", 1780, ("Owner", "int64", null), ("Prototype", "string", null), ("Item", "int64", null));
        var hasRemovePart = graph.NotZero("HasRemovePart", 2000);
        var removePartGate = graph.Branch("RemovePart?", 2000);
        var noPartInstalled = graph.Call("Bui.Set", "NoPartInstalled", 2220, ("Owner", "EntityUid", null), ("Name", "string", "Reason"), ("Value", "string", "Select an installed part"), ("Success", "bool", null));
        var removeComp = graph.Component("RemovePartData", "WeaponPart", 2220, "int64");
        var removeCompGate = graph.Branch("RemovePartData?", 2440);
        var removeSlot = graph.Field("RemoveSlot", "WeaponPart", "Slot", "string", 2660);
        var removeAssembly = graph.Component("RemoveAssembly", "WeaponAssembly", 2660, "int64");
        var removeAssemblyGate = graph.Branch("RemoveAssembly?", 2880);
        var takePart = graph.Call("Container.Remove", "TakePart", 3100, ("Owner", "int64", null), ("Container", "string", null), ("Item", "int64", null), ("Success", "bool", null));
        var returnPart = graph.Call("Container.Insert", "ReturnPart", 3320, ("Owner", "EntityUid", null), ("Container", "string", "storagebase"), ("Item", "int64", null), ("Success", "bool", null));
        var cleared = graph.ApplyDeltas("Off", 3540, false, removeComp, removeAssembly);
        var removed = graph.Call("Bui.Set", "Removed", 3540, ("Owner", "EntityUid", null), ("Name", "string", "Reason"), ("Value", "string", "Part returned to the bench"), ("Success", "bool", null));
        var afterRemove = graph.Refresh("Off", 3760);
        graph.UseRow(2200);
        var isSelect = graph.Equal("IsSelect", "SelectPart", 680);
        var selectGate = graph.Branch("Select?", 900);
        var selectGun = graph.Call("Entity.GetHeldItem", "SelectGun", 1120, ("Holder", "EntityUid", null), ("Container", "string", "gun"), ("Item", "int64", null));
        var hasSelectGun = graph.NotZero("HasSelectGun", 1340);
        var selectGunGate = graph.Branch("SelectGun?", 1340);
        var selectNoGun = graph.Call("Bui.Set", "SelectNoGun", 1560, ("Owner", "EntityUid", null), ("Name", "string", "Reason"), ("Value", "string", "Put the pistol in the gun slot"), ("Success", "bool", null));
        var selectBench = graph.Call("Inventory.Find", "SelectBench", 1560, ("Owner", "EntityUid", null), ("Prototype", "string", null), ("Item", "int64", null));
        var selectOnGun = graph.Call("Inventory.Find", "SelectOnGun", 1780, ("Owner", "int64", null), ("Prototype", "string", null), ("Item", "int64", null));
        var selectHasBench = graph.NotZero("SelectHasBench", 2000);
        var selectBenchGate = graph.Branch("SelectBench?", 2000);
        var selectHasGunItem = graph.NotZero("SelectHasGunItem", 2220);
        var selectGunItemGate = graph.Branch("SelectGunItem?", 2220);
        var selectNoPart = graph.Call("Bui.Set", "SelectNoPart", 2440, ("Owner", "EntityUid", null), ("Name", "string", "Reason"), ("Value", "string", "Select a part in the list"), ("Success", "bool", null));
        var selectComp = graph.Component("SelectPartData", "WeaponPart", 2440, "int64");
        var selectCompGate = graph.Branch("SelectPartData?", 2660);
        var selectAssembly = graph.Component("SelectAssembly", "WeaponAssembly", 2660, "int64");
        var selectFit = graph.Fit("Select", 2880, selectComp, selectAssembly);
        var selectGunComp = graph.Component("SelectGunPart", "WeaponPart", 2440, "int64");
        var selectGunCompGate = graph.Branch("SelectGunPart?", 2660);
        var selectGunFit = graph.Fit("SelectGun", 2880, selectGunComp, selectAssembly);

        graph.Exec(message, installGate);
        graph.Exec(installGate, "True", partGate);
        graph.Exec(partGate, "False", noPart);
        graph.Exec(partGate, "True", gunGate);
        graph.Exec(gunGate, "False", noGun);
        graph.Exec(gunGate, "True", partDataGate);
        graph.Exec(partDataGate, "True", assemblyGate);
        graph.Exec(assemblyGate, "True", installFit.Entry);
        graph.Exec(installFit.Direct, fitting);
        graph.Exec(fitting, slotFullGate);
        graph.Exec(slotFullGate, "True", slotFullReason);
        graph.Exec(slotFullGate, "False", insert);
        graph.Exec(insert, inserted);
        graph.Exec(inserted, "True", installedMsg);
        graph.Exec(installedMsg, afterInstall);
        graph.Exec(inserted, "False", rejected);
        graph.Exec(installGate, "False", removeGate);
        graph.Exec(removeGate, "False", selectGate);
        graph.Exec(removeGate, "True", removeGunGate);
        graph.Exec(removeGunGate, "False", noRemoveGun);
        graph.Exec(removeGunGate, "True", removePartGate);
        graph.Exec(removePartGate, "False", noPartInstalled);
        graph.Exec(removePartGate, "True", removeCompGate);
        graph.Exec(removeCompGate, "True", removeAssemblyGate);
        graph.Exec(removeAssemblyGate, "True", takePart);
        graph.Exec(takePart, returnPart);
        graph.Exec(returnPart, cleared.Start);
        graph.Exec(cleared.End, removed);
        graph.Exec(removed, afterRemove);
        graph.Exec(selectGate, "True", selectGunGate);
        graph.Exec(selectGunGate, "False", selectNoGun);
        graph.Exec(selectGunGate, "True", selectBenchGate);
        graph.Exec(selectBenchGate, "True", selectCompGate);
        graph.Exec(selectCompGate, "True", selectFit.Entry);
        graph.Exec(selectBenchGate, "False", selectGunItemGate);
        graph.Exec(selectGunItemGate, "False", selectNoPart);
        graph.Exec(selectGunItemGate, "True", selectGunCompGate);
        graph.Exec(selectGunCompGate, "True", selectGunFit.Entry);
        graph.Exec(open, onOpen);

        graph.DataWire(message, "Event", action, "Target");
        graph.DataWire(action, "Value", isInstall, "A");
        graph.DataWire(isInstall, "Result", installGate, "Condition");
        graph.DataWire(action, "Value", isRemove, "A");
        graph.DataWire(isRemove, "Result", removeGate, "Condition");
        graph.DataWire(action, "Value", isSelect, "A");
        graph.DataWire(isSelect, "Result", selectGate, "Condition");
        graph.DataWire(message, "Entity", removeGun, "Holder");
        graph.DataWire(message, "Entity", noRemoveGun, "Owner");
        graph.DataWire(message, "Entity", noPartInstalled, "Owner");
        graph.DataWire(message, "Entity", returnPart, "Owner");
        graph.DataWire(message, "Entity", removed, "Owner");
        graph.DataWire(message, "Entity", fitting, "Owner");
        graph.DataWire(message, "Entity", slotFullReason, "Owner");
        graph.DataWire(gun, "Item", slotHeld, "Holder");
        graph.DataWire(slot, "Value", slotHeld, "Container");
        graph.DataWire(slotHeld, "Item", slotFull, "A");
        graph.DataWire(slotFull, "Result", slotFullGate, "Condition");
        graph.DataWire(message, "Entity", installedMsg, "Owner");
        graph.DataWire(message, "Entity", rejected, "Owner");
        graph.DataWire(message, "Entity", selectGun, "Holder");
        graph.DataWire(message, "Entity", selectNoGun, "Owner");
        graph.DataWire(message, "Entity", selectBench, "Owner");
        graph.DataWire(message, "Entity", selectNoPart, "Owner");
        graph.DataWire(removeGun, "Item", hasRemoveGun, "A");
        graph.DataWire(hasRemoveGun, "Result", removeGunGate, "Condition");
        graph.DataWire(removeGun, "Item", removePart, "Owner");
        graph.DataWire(partId, "Value", removePart, "Prototype");
        graph.DataWire(removePart, "Item", hasRemovePart, "A");
        graph.DataWire(hasRemovePart, "Result", removePartGate, "Condition");
        graph.DataWire(removePart, "Item", removeComp, "Entity");
        graph.DataWire(removeComp, "Found", removeCompGate, "Condition");
        graph.DataWire(removeComp, "Component", removeSlot, "Component");
        graph.DataWire(removeGun, "Item", removeAssembly, "Entity");
        graph.DataWire(removeAssembly, "Found", removeAssemblyGate, "Condition");
        graph.DataWire(removeGun, "Item", takePart, "Owner");
        graph.DataWire(removeSlot, "Value", takePart, "Container");
        graph.DataWire(removePart, "Item", takePart, "Item");
        graph.DataWire(removePart, "Item", returnPart, "Item");
        graph.DataWire(selectGun, "Item", hasSelectGun, "A");
        graph.DataWire(hasSelectGun, "Result", selectGunGate, "Condition");
        graph.DataWire(selectGun, "Item", selectOnGun, "Owner");
        graph.DataWire(selectGun, "Item", selectAssembly, "Entity");
        graph.DataWire(partId, "Value", selectBench, "Prototype");
        graph.DataWire(partId, "Value", selectOnGun, "Prototype");
        graph.DataWire(selectBench, "Item", selectHasBench, "A");
        graph.DataWire(selectHasBench, "Result", selectBenchGate, "Condition");
        graph.DataWire(selectBench, "Item", selectComp, "Entity");
        graph.DataWire(selectComp, "Found", selectCompGate, "Condition");
        graph.DataWire(selectOnGun, "Item", selectHasGunItem, "A");
        graph.DataWire(selectHasGunItem, "Result", selectGunItemGate, "Condition");
        graph.DataWire(selectOnGun, "Item", selectGunComp, "Entity");
        graph.DataWire(selectGunComp, "Found", selectGunCompGate, "Condition");
        graph.WireStatus(message);
        graph.DataWire(message, "Event", payload, "Target");
        graph.DataWire(payload, "Value", partId, "Payload");
        graph.DataWire(message, "Entity", part, "Owner");
        graph.DataWire(message, "Entity", noPart, "Owner");
        graph.DataWire(message, "Entity", noGun, "Owner");
        graph.DataWire(partId, "Value", part, "Prototype");
        graph.DataWire(part, "Item", hasPart, "A");
        graph.DataWire(hasPart, "Result", partGate, "Condition");
        graph.DataWire(message, "Entity", gun, "Holder");
        graph.DataWire(gun, "Item", hasGun, "A");
        graph.DataWire(hasGun, "Result", gunGate, "Condition");
        graph.DataWire(part, "Item", partComp, "Entity");
        graph.DataWire(partComp, "Found", partDataGate, "Condition");
        graph.DataWire(partComp, "Component", slot, "Component");
        graph.DataWire(gun, "Item", assembly, "Entity");
        graph.DataWire(assembly, "Found", assemblyGate, "Condition");
        graph.DataWire(partComp, "Component", fitLabel, "Component");
        graph.DataWire(fitLabel, "Value", fitLeft, "A");
        graph.DataWire(fitArrow, "Value", fitLeft, "B");
        graph.DataWire(fitLeft, "Result", fitWhere, "A");
        graph.DataWire(slot, "Value", fitWhere, "B");
        graph.DataWire(fitWhere, "Result", fitting, "Value");
        graph.DataWire(gun, "Item", insert, "Owner");
        graph.DataWire(slot, "Value", insert, "Container");
        graph.DataWire(part, "Item", insert, "Item");
        graph.DataWire(insert, "Success", inserted, "Condition");
        graph.DataWire(rejectPrefix, "Value", rejectText, "A");
        graph.DataWire(slot, "Value", rejectText, "B");
        graph.DataWire(rejectText, "Result", rejected, "Value");
        graph.WireRefresh(afterInstall, open);
        graph.WireRefresh(onOpen, open);
        graph.WireRefresh(afterRemove, message);

        return graph.Document();
    }

    private static string NightCity()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "..", "night-city");
            if (Directory.Exists(Path.Combine(candidate, "RobustToolbox")))
            {
                return Path.GetFullPath(candidate);
            }

            if (Directory.Exists(Path.Combine(dir.FullName, "RobustToolbox")) &&
                Directory.Exists(Path.Combine(dir.FullName, "Resources")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return @"D:\projects\night-city";
    }

    private sealed class BenchGraph
    {
        private readonly List<NodeDocument> _nodes = [];
        private readonly List<ConnectionDocument> _connections = [];
        private readonly Dictionary<string, NodePosition> _positions = [];
        private readonly HashSet<(int X, int Y)> _used = [];
        private int _row;

        public void UseRow(int y) => _row = y;

        public NodeDocument Refresh(string prefix, int x, NodeDocument? deltaPart = null)
        {
            var created = Data("List.Create", prefix + "Empty", x, ("List", "List<PartRow>", null));
            var clear = Assign(prefix + "Clear", "Rows", x, "List<PartRow>");
            var contents = Call("Container.Contents", prefix + "Contents", x + 280, ("Owner", "EntityUid", null), ("Container", "string", "storagebase"), ("Contents", "List<EntityUid>", null));
            var each = ForEach(prefix + "Each", x + 280);
            var rowComp = Component(prefix + "Row", "WeaponPart", x + 560);
            var rowGate = Branch(prefix + "Row?", x + 560);
            var made = Data("Schema.Make", prefix + "Make", x + 840, ("Value", "PartRow", null));
            made.Properties["Schema"] = "PartRow";
            var setId = Set(prefix + "Id", "PartRow", "Id", x + 840);
            var rowProto = Field(prefix + "Prototype", "WeaponPart", "Prototype", "string", x + 1120);
            var setText = Set(prefix + "Text", "PartRow", "Text", x + 1120);
            var label = Field(prefix + "Label", "WeaponPart", "Label", "string", x + 1400);
            var setOff = Set(prefix + "Off", "PartRow", "Disabled", x + 1400, "false", "bool");
            var readRows = Read(prefix + "Read", "Rows", x + 1680, "List<PartRow>");
            var add = Call("List.Add", prefix + "Add", x + 1680, ("List", "List<PartRow>", null), ("Item", "PartRow", null), ("ListOut", "List<PartRow>", null));
            var store = Assign(prefix + "Store", "Rows", x + 1960, "List<PartRow>");
            var readBuilt = Read(prefix + "Built", "Rows", x + 2100, "List<PartRow>");
            var rows = Call("Ui.Rows", prefix + "Json", x + 2380, ("List", "List<PartRow>", null), ("IdField", "string", "Id"), ("TextField", "string", "Text"), ("DisabledField", "string", "Disabled"), ("Rows", "string", null));
            var publish = Call("Bui.Set", prefix + "Publish", x + 2520, ("Owner", "EntityUid", null), ("Name", "string", "Parts"), ("Value", "string", null), ("Success", "bool", null));
            var gun = Call("Entity.GetHeldItem", prefix + "Gun", x + 2740, ("Holder", "EntityUid", null), ("Container", "string", "gun"), ("Item", "int64", null));
            var assembly = Component(prefix + "Assembly", "WeaponAssembly", x + 2960, "int64");
            var assemblyGate = Branch(prefix + "Assembly?", x + 2960);
            var assemblyRate = Field(prefix + "AssemblyRate", "WeaponAssembly", "FireRate", "float32", x + 3180);
            var slotList = Data("List.Create", prefix + "SlotList", x + 3400, ("List", "List<EntityUid>", null));
            var addBarrel = Held("barrel", x + 3620);
            var addBolt = Held("bolt", x + 3840);
            var addMagazine = Held("magazine", x + 4060);
            var addOptic = Held("optic", x + 4280);
            var addMuzzle = Held("muzzle", x + 4500);
            var slotEach = ForEach(prefix + "SlotEach", x + 4720);
            var installedComp = Component(prefix + "Installed", "WeaponPart", x + 4940);
            var installedGate = Branch(prefix + "InstalledPart?", x + 4720);
            var installedMade = Data("Schema.Make", prefix + "InstalledMake", x + 4940, ("Value", "PartRow", null));
            installedMade.Properties["Schema"] = "PartRow";
            var installedId = Set(prefix + "InstalledId", "PartRow", "Id", x + 5160);
            var installedProto = Field(prefix + "InstalledPrototype", "WeaponPart", "Prototype", "string", x + 5380);
            var installedText = Set(prefix + "InstalledText", "PartRow", "Text", x + 5380);
            var installedLabel = Field(prefix + "InstalledLabel", "WeaponPart", "Label", "string", x + 5600);
            var installedMark = Add(prefix + "InstalledMark", x + 5600, "string");
            var installedSuffix = Literal(prefix + "InstalledSuffix", "string", " · installed", x + 5600);
            var installedOff = Set(prefix + "InstalledOff", "PartRow", "Disabled", x + 5820, "false", "bool");
            var installedRead = Read(prefix + "InstalledRead", "Rows", x + 5820, "List<PartRow>");
            var installedAdd = Call("List.Add", prefix + "InstalledAdd", x + 5820, ("List", "List<PartRow>", null), ("Item", "PartRow", null), ("ListOut", "List<PartRow>", null));
            var installedStore = Assign(prefix + "InstalledStore", "Rows", x + 6040, "List<PartRow>");
            var installedBuilt = Read(prefix + "InstalledBuilt", "Rows", x + 6260, "List<PartRow>");
            var installedJson = Call("Ui.Rows", prefix + "InstalledJson", x + 6480, ("List", "List<PartRow>", null), ("IdField", "string", "Id"), ("TextField", "string", "Text"), ("DisabledField", "string", "Disabled"), ("Rows", "string", null));
            var installedPublish = Call("Bui.Set", prefix + "InstalledPublish", x + 6700, ("Owner", "EntityUid", null), ("Name", "string", "Parts"), ("Value", "string", null), ("Success", "bool", null));
            var fitted = GunTail(prefix + "Fit", x + 7140);

            Exec(clear, each);
            Exec(each, "Body", rowGate);
            Exec(rowGate, "True", setId);
            Exec(setId, setText);
            Exec(setText, setOff);
            Exec(setOff, add);
            Exec(add, store);
            Exec(each, "Out", publish);
            Exec(publish, assemblyGate);
            Exec(assemblyGate, "True", addBarrel.Add);
            Exec(addBarrel.Add, addBolt.Add);
            Exec(addBolt.Add, addMagazine.Add);
            Exec(addMagazine.Add, addOptic.Add);
            Exec(addOptic.Add, addMuzzle.Add);
            Exec(addMuzzle.Add, slotEach);
            Exec(slotEach, "Body", installedGate);
            Exec(installedGate, "True", installedId);
            Exec(installedId, installedText);
            Exec(installedText, installedOff);
            Exec(installedOff, installedAdd);
            Exec(installedAdd, installedStore);
            Exec(slotEach, "Out", installedPublish);
            Exec(installedPublish, fitted);

            DataWire(created, "List", clear, "Value");
            DataWire(contents, "Contents", each, "Collection");
            DataWire(each, "Current", rowComp, "Entity");
            DataWire(rowComp, "Found", rowGate, "Condition");
            DataWire(rowComp, "Component", label, "Component");
            DataWire(rowComp, "Component", rowProto, "Component");
            DataWire(made, "Value", setId, "Target");
            DataWire(rowProto, "Value", setId, "Value");
            DataWire(setId, "Result", setText, "Target");
            DataWire(label, "Value", setText, "Value");
            DataWire(setText, "Result", setOff, "Target");
            DataWire(readRows, "Value", add, "List");
            DataWire(setOff, "Result", add, "Item");
            DataWire(add, "List", store, "Value");
            DataWire(readBuilt, "Value", rows, "List");
            DataWire(rows, "Rows", publish, "Value");
            DataWire(gun, "Item", assembly, "Entity");
            DataWire(assembly, "Found", assemblyGate, "Condition");
            DataWire(assembly, "Component", assemblyRate, "Component");
            DataWire(gun, "Item", addBarrel.Held, "Holder");
            DataWire(gun, "Item", addBolt.Held, "Holder");
            DataWire(gun, "Item", addMagazine.Held, "Holder");
            DataWire(gun, "Item", addOptic.Held, "Holder");
            DataWire(gun, "Item", addMuzzle.Held, "Holder");
            DataWire(slotList, "List", addBarrel.Add, "List");
            DataWire(addBarrel.Held, "Item", addBarrel.Add, "Item");
            DataWire(addBarrel.Add, "List", addBolt.Add, "List");
            DataWire(addBolt.Held, "Item", addBolt.Add, "Item");
            DataWire(addBolt.Add, "List", addMagazine.Add, "List");
            DataWire(addMagazine.Held, "Item", addMagazine.Add, "Item");
            DataWire(addMagazine.Add, "List", addOptic.Add, "List");
            DataWire(addOptic.Held, "Item", addOptic.Add, "Item");
            DataWire(addOptic.Add, "List", addMuzzle.Add, "List");
            DataWire(addMuzzle.Held, "Item", addMuzzle.Add, "Item");
            DataWire(addMuzzle.Add, "List", slotEach, "Collection");
            DataWire(slotEach, "Current", installedComp, "Entity");
            DataWire(installedComp, "Found", installedGate, "Condition");
            DataWire(installedComp, "Component", installedProto, "Component");
            DataWire(installedComp, "Component", installedLabel, "Component");
            DataWire(installedMade, "Value", installedId, "Target");
            DataWire(installedProto, "Value", installedId, "Value");
            DataWire(installedId, "Result", installedText, "Target");
            DataWire(installedLabel, "Value", installedMark, "A");
            DataWire(installedSuffix, "Value", installedMark, "B");
            DataWire(installedMark, "Result", installedText, "Value");
            DataWire(installedText, "Result", installedOff, "Target");
            DataWire(installedRead, "Value", installedAdd, "List");
            DataWire(installedOff, "Result", installedAdd, "Item");
            DataWire(installedAdd, "List", installedStore, "Value");
            DataWire(installedBuilt, "Value", installedJson, "List");
            DataWire(installedJson, "Rows", installedPublish, "Value");
            _refreshes[clear] = (contents, publish, gun, installedPublish);
            return clear;

            NodeDocument GunTail(string name, int tailX)
            {
                (NodeDocument Start, NodeDocument End)? adjusted = deltaPart == null
                    ? null
                    : ApplyDeltas(name + "Delta", tailX, true, deltaPart, assembly);
                if (adjusted != null)
                {
                    tailX += 1760;
                }

                var writeRate = Call("Component.SetField", name + "WriteRate", tailX, ("Entity", "int64", null), ("Component", "string", "GunComponent"), ("Field", "string", "FireRate"), ("Value", "float64", null), ("Success", "bool", null));
                var writeModified = Call("Component.SetField", name + "WriteModified", tailX + 220, ("Entity", "int64", null), ("Component", "string", "GunComponent"), ("Field", "string", "FireRateModified"), ("Value", "float64", null), ("Success", "bool", null));
                var rateText = Call("Text.WithNumber", name + "RateText", tailX + 440, ("Label", "string", "Fire rate"), ("Number", "float64", null), ("Text", "string", null));
                var describe = Call("Meta.SetDescription", name + "Describe", tailX + 440, ("Entity", "int64", null), ("Text", "string", null), ("Success", "bool", null));
                var refreshGun = Call("System.Invoke", name + "RefreshGun", tailX + 660, ("System", "string", "SharedGunSystem"), ("Method", "string", "RefreshModifiers"), ("Entity", "int64", null), ("Success", "bool", null));
                if (adjusted != null)
                {
                    Exec(adjusted.Value.End, writeRate);
                }

                Exec(writeRate, writeModified);
                Exec(writeModified, describe);
                Exec(describe, refreshGun);
                DataWire(gun, "Item", writeRate, "Entity");
                DataWire(assemblyRate, "Value", writeRate, "Value");
                DataWire(gun, "Item", writeModified, "Entity");
                DataWire(assemblyRate, "Value", writeModified, "Value");
                DataWire(assemblyRate, "Value", rateText, "Number");
                DataWire(rateText, "Text", describe, "Text");
                DataWire(gun, "Item", describe, "Entity");
                DataWire(gun, "Item", refreshGun, "Entity");
                return adjusted?.Start ?? writeRate;
            }

            (NodeDocument Held, NodeDocument Add) Held(string container, int at)
            {
                var held = Call("Entity.GetHeldItem", prefix + container + "Held", at, ("Holder", "int64", null), ("Container", "string", container), ("Item", "EntityUid", null));
                var add = Call("List.Add", prefix + container + "Add", at, ("List", "List<EntityUid>", null), ("Item", "EntityUid", null), ("ListOut", "List<EntityUid>", null));
                return (held, add);
            }
        }

        public void WireRefresh(NodeDocument clear, NodeDocument bench)
        {
            var (contents, publish, gun, installedPublish) = _refreshes[clear];
            DataWire(bench, "Entity", contents, "Owner");
            DataWire(bench, "Entity", publish, "Owner");
            DataWire(bench, "Entity", gun, "Holder");
            DataWire(bench, "Entity", installedPublish, "Owner");
        }

        private readonly Dictionary<NodeDocument, (NodeDocument Contents, NodeDocument Publish, NodeDocument Gun, NodeDocument InstalledPublish)> _refreshes = [];

        public NodeDocument Event(string name, string eventType, string component, int x, int y) =>
            Place(Node(name, "Event." + name, new Dictionary<string, string> { ["eventType"] = eventType, ["componentType"] = component },
                Exec("Out", false), Data("Entity", "EntityUid", false), Data("Event", "object", false)), x, y);

        public NodeDocument Branch(string name, int x) =>
            Place(Node(name, "Core.Branch", null, Exec("In", true), Exec("True", false), Exec("False", false), Data("Condition", "bool", true)), x, Y(80));

        public NodeDocument Assign(string name, string variable, int x, string valueType = "object") =>
            Place(Node(name, "Core.VariableAssign", new Dictionary<string, string> { ["VariableName"] = variable },
                Exec("In", true), Exec("Out", false), Data("Value", valueType, true)), x, Y(80));

        public NodeDocument Read(string name, string variable, int x, string valueType = "object") =>
            Place(Node(name, "Core.VariableRead", new Dictionary<string, string> { ["VariableName"] = variable },
                Data("Value", valueType, false)), x, Y(300));

        public NodeDocument ForEach(string name, int x) =>
            Place(Node(name, "Flow.ForEach", null, Exec("In", true), Exec("Out", false), Exec("Body", false), Data("Collection", "List<EntityUid>", true), Data("Current", "EntityUid", false)), x, Y(80));

        public NodeDocument Component(string name, string type, int x, string entityType = "EntityUid") =>
            Place(Node(name, "Entity.TryGetComponent", new Dictionary<string, string> { ["ComponentType"] = type },
                Data("Entity", entityType, true), Data("Found", "bool", false), Data("Component", "component", false)), x, Y(300));

        public NodeDocument Field(string name, string schema, string field, string type, int x) =>
            Place(Node(name, "Schema.GetField", new Dictionary<string, string> { ["Schema"] = schema, ["Field"] = field },
                Data("Component", "component", true), Data("Value", type, false)), x, Y(300));

        public NodeDocument Set(string name, string schema, string field, int x, string? valueDefault = null, string valueType = "object") =>
            Place(Node(name, "Schema.SetField", new Dictionary<string, string> { ["Schema"] = schema, ["Field"] = field },
                Exec("In", true), Exec("Out", false), Data("Target", "object", true), Data("Value", valueType, true, valueDefault), Data("Result", "object", false)), x, Y(80));

        public NodeDocument Member(string name, string member, string type, int x) =>
            Place(Node(name, "Native.GetMember", new Dictionary<string, string> { ["Member"] = member },
                Data("Target", "object", true), Data("Value", type, false)), x, Y(300));

        public NodeDocument Literal(string name, string type, string value, int x) =>
            Place(Node(name, "Core.Literal", new Dictionary<string, string> { ["Value"] = value, ["Type"] = type },
                Data("Value", type, false)), x, Y(300));

        public NodeDocument Equal(string name, string literal, int x)
        {
            var node = Node(name, "cmp.equal", null, Data("A", "string", true), Data("B", "string", true, literal), Data("Result", "bool", false));
            return Place(node, x, Y(300));
        }

        public NodeDocument NotZero(string name, int x)
        {
            var node = Node(name, "cmp.notequal", null, Data("A", "int64", true), Data("B", "int64", true, "0"), Data("Result", "bool", false));
            return Place(node, x, Y(300));
        }

        public NodeDocument Data(string type, string name, int x, params (string Name, string Type, string? Default)[] pins)
        {
            var specs = pins.Select(pin => Data(pin.Name, pin.Type, false, pin.Default)).ToArray();
            return Place(Node(name, type, null, specs), x, Y(300));
        }

        private int Y(int y) => y + _row;

        public NodeDocument Call(string type, string name, int x, params (string Name, string Type, string? Default)[] pins)
        {
            var specs = new List<PinDocument>();
            if (type is "Container.Insert" or "Container.Remove" or "Bui.Set" or "List.Add" or "System.Invoke" or "Component.SetField" or "Meta.SetDescription")
            {
                specs.Add(Exec("In", true));
                specs.Add(Exec("Out", false));
            }

            var outputs = type switch
            {
                "Inventory.Find" or "Entity.GetHeldItem" => new[] { "Item" },
                "Container.Contents" => new[] { "Contents" },
                "Bui.Field" => new[] { "Value" },
                "Ui.Rows" => new[] { "Rows" },
                "Container.Insert" or "Container.Remove" or "Bui.Set" or "System.Invoke" or "Component.SetField" or "Meta.SetDescription" => new[] { "Success" },
                "Text.WithNumber" => new[] { "Text" },
                "List.Add" => new[] { "ListOut" },
                _ => Array.Empty<string>()
            };
            foreach (var pin in pins)
            {
                if (pin.Name == "ListOut")
                {
                    specs.Add(new PinDocument { Name = "List", Direction = PinDirection.Output, Kind = PinKind.Data, DataType = pin.Type });
                    continue;
                }

                var output = outputs.Contains(pin.Name);
                specs.Add(output
                    ? Data(pin.Name, pin.Type, false)
                    : Data(pin.Name, pin.Type, true, pin.Default));
            }

            var execution = type is "Container.Insert" or "Container.Remove" or "Bui.Set" or "List.Add" or "System.Invoke" or "Component.SetField" or "Meta.SetDescription";
            return Place(Node(name, type, null, [.. specs]), x, Y(execution ? 80 : 300));
        }

        public void Exec(NodeDocument from, NodeDocument to) => Exec(from, from.Pins.First(pin => pin.Kind == PinKind.Execution && pin.Direction == PinDirection.Output).Name, to);

        public void Exec(NodeDocument from, string fromPin, NodeDocument to) =>
            Wire(from, fromPin, to, "In");

        public void DataWire(NodeDocument from, string fromPin, NodeDocument to, string toPin) =>
            Wire(from, fromPin, to, toPin);

        public GraphDocument Document() => new()
        {
            Id = GraphId.FromString("c1000000-0000-4000-8000-0000000000c1"),
            Name = "WeaponBench",
            Kind = GraphKind.System,
            Side = GraphSide.Server,
            Metadata = new GraphMetadata { Description = "Bench lists parts, shows whether a part fits the pistol, installs a direct fit, and returns that part to storage. The client only sends the action." },
            Variables =
            [
                new GraphVariableDocument { Name = "Rows", TypeName = "List<PartRow>" },
                new GraphVariableDocument { Name = "Rate", TypeName = "float32" },
                new GraphVariableDocument { Name = "Speed", TypeName = "float32" }
            ],
            Nodes = _nodes,
            Connections = _connections,
            EditorLayout = new EditorLayoutDocument { NodePositions = _positions },
            Schemas =
            [
                Schema("11aa11aa-11aa-41aa-81aa-11aa11aa11aa", "WeaponAssembly", true,
                    ("11aa11aa-11aa-41aa-81aa-11aa11aa11ab", "Barrel", "string", ""),
                    ("11aa11aa-11aa-41aa-81aa-11aa11aa11ac", "FireRate", "float32", "6"),
                    ("11aa11aa-11aa-41aa-81aa-11aa11aa11ad", "ProjectileSpeed", "float32", "40"),
                    ("11aa11aa-11aa-41aa-81aa-11aa11aa11ae", "Bolt", "string", ""),
                    ("11aa11aa-11aa-41aa-81aa-11aa11aa11af", "Magazine", "string", ""),
                    ("11aa11aa-11aa-41aa-81aa-11aa11aa11b0", "Optic", "string", ""),
                    ("11aa11aa-11aa-41aa-81aa-11aa11aa11b1", "Muzzle", "string", ""),
                    ("11aa11aa-11aa-41aa-81aa-11aa11aa11b3", "BarrelAccepts", "string", ""),
                    ("11aa11aa-11aa-41aa-81aa-11aa11aa11b4", "BoltAccepts", "string", ""),
                    ("11aa11aa-11aa-41aa-81aa-11aa11aa11b5", "FeedAccepts", "string", ""),
                    ("11aa11aa-11aa-41aa-81aa-11aa11aa11b6", "OpticAccepts", "string", ""),
                    ("11aa11aa-11aa-41aa-81aa-11aa11aa11b7", "MuzzleAccepts", "string", "")),
                Schema("22bb22bb-22bb-42bb-82bb-22bb22bb22bb", "WeaponPart", true,
                    ("22bb22bb-22bb-42bb-82bb-22bb22bb22b1", "Prototype", "string", ""),
                    ("22bb22bb-22bb-42bb-82bb-22bb22bb22b2", "Slot", "string", "barrel"),
                    ("22bb22bb-22bb-42bb-82bb-22bb22bb22b3", "Label", "string", ""),
                    ("22bb22bb-22bb-42bb-82bb-22bb22bb22b4", "FireRate", "float32", "6"),
                    ("22bb22bb-22bb-42bb-82bb-22bb22bb22b5", "ProjectileSpeed", "float32", "40"),
                    ("22bb22bb-22bb-42bb-82bb-22bb22bb22b9", "RateDelta", "float32", "0"),
                    ("22bb22bb-22bb-42bb-82bb-22bb22bb22ba", "SpeedDelta", "float32", "0"),
                    ("22bb22bb-22bb-42bb-82bb-22bb22bb22bd", "Interface", "string", ""),
                    ("22bb22bb-22bb-42bb-82bb-22bb22bb22be", "Offer", "string", "direct")),
                Schema("33cc33cc-33cc-43cc-83cc-33cc33cc33cc", "PartRow", false,
                    ("33cc33cc-33cc-43cc-83cc-33cc33cc33c1", "Id", "string", ""),
                    ("33cc33cc-33cc-43cc-83cc-33cc33cc33c2", "Text", "string", ""),
                    ("33cc33cc-33cc-43cc-83cc-33cc33cc33c3", "Disabled", "bool", "false"))
            ]
        };

        private static readonly (string Container, string Field)[] Slots =
        [
            ("barrel", "Barrel"),
            ("bolt", "Bolt"),
            ("magazine", "Magazine"),
            ("optic", "Optic"),
            ("muzzle", "Muzzle")
        ];

        private readonly List<NodeDocument> _status = [];
        private readonly Dictionary<NodeDocument, (List<NodeDocument> Holders, List<NodeDocument> Targets)> _derives = [];

        public (NodeDocument Entry, NodeDocument Direct) Fit(string prefix, int x, NodeDocument part, NodeDocument assembly)
        {
            var iface = Field(prefix + "Interface", "WeaponPart", "Interface", "string", x);
            var offer = Field(prefix + "Offer", "WeaponPart", "Offer", "string", x + 220);
            var label = Field(prefix + "Label", "WeaponPart", "Label", "string", x + 440);
            DataWire(part, "Component", iface, "Component");
            DataWire(part, "Component", offer, "Component");
            DataWire(part, "Component", label, "Component");
            NodeDocument? any = null;
            var cursor = x + 660;
            foreach (var name in new[] { "BarrelAccepts", "BoltAccepts", "FeedAccepts", "OpticAccepts", "MuzzleAccepts" })
            {
                var field = Field(prefix + name, "WeaponAssembly", name, "string", cursor);
                var eq = Compare(prefix + name + "Eq", cursor);
                DataWire(assembly, "Component", field, "Component");
                DataWire(iface, "Value", eq, "A");
                DataWire(field, "Value", eq, "B");
                if (any == null)
                {
                    any = eq;
                }
                else
                {
                    var or = Logic("logic.or", prefix + name + "Or", cursor);
                    DataWire(any, "Result", or, "A");
                    DataWire(eq, "Result", or, "B");
                    any = or;
                }

                cursor += 220;
            }

            var directOffer = Equal(prefix + "OfferDirect", "direct", cursor);
            var adapterOffer = Equal(prefix + "OfferAdapter", "adapter", cursor + 220);
            var machineOffer = Equal(prefix + "OfferMachine", "machining", cursor + 440);
            var directHit = Logic("logic.and", prefix + "DirectHit", cursor + 660);
            var adaptHit = Logic("logic.and", prefix + "AdaptHit", cursor + 880);
            var machineHit = Logic("logic.and", prefix + "MachineHit", cursor + 1100);
            DataWire(offer, "Value", directOffer, "A");
            DataWire(offer, "Value", adapterOffer, "A");
            DataWire(offer, "Value", machineOffer, "A");
            var hit = any ?? throw new InvalidOperationException("Fit has no interface.");
            DataWire(directOffer, "Result", directHit, "A");
            DataWire(hit, "Result", directHit, "B");
            DataWire(adapterOffer, "Result", adaptHit, "A");
            DataWire(hit, "Result", adaptHit, "B");
            DataWire(machineOffer, "Result", machineHit, "A");
            DataWire(hit, "Result", machineHit, "B");
            var entry = Branch(prefix + "Direct?", cursor + 1320);
            var adaptGate = Branch(prefix + "Adapter?", cursor + 1540);
            var machineGate = Branch(prefix + "Machining?", cursor + 1760);
            var direct = Reason(prefix + "DirectReason", cursor + 1540, label, ": Direct fit.");
            var adapter = Reason(prefix + "AdapterReason", cursor + 1760, label, ": Needs an adapter.");
            var machine = Reason(prefix + "MachineReason", cursor + 1980, label, ": Needs machining.");
            var reject = Reason(prefix + "RejectReason", cursor + 2200, label, ": Incompatible with this pistol.");
            Exec(entry, "True", direct);
            Exec(entry, "False", adaptGate);
            Exec(adaptGate, "True", adapter);
            Exec(adaptGate, "False", machineGate);
            Exec(machineGate, "True", machine);
            Exec(machineGate, "False", reject);
            DataWire(directHit, "Result", entry, "Condition");
            DataWire(adaptHit, "Result", adaptGate, "Condition");
            DataWire(machineHit, "Result", machineGate, "Condition");
            return (entry, direct);
        }

        public (NodeDocument Start, NodeDocument End) ApplyDeltas(string prefix, int x, bool add, NodeDocument part, NodeDocument assembly)
        {
            var asmRate = Field(prefix + "AsmRate", "WeaponAssembly", "FireRate", "float32", x);
            var asmSpeed = Field(prefix + "AsmSpeed", "WeaponAssembly", "ProjectileSpeed", "float32", x + 220);
            var rateDelta = Field(prefix + "RateDelta", "WeaponPart", "RateDelta", "float32", x + 440);
            var speedDelta = Field(prefix + "SpeedDelta", "WeaponPart", "SpeedDelta", "float32", x + 660);
            var op = add ? "math.add" : "math.subtract";
            var rateMath = Logic(op, prefix + "RateMath", x + 880, "float32");
            var speedMath = Logic(op, prefix + "SpeedMath", x + 1100, "float32");
            var setRate = Set(prefix + "SetRate", "WeaponAssembly", "FireRate", x + 1320, null, "float32");
            var setSpeed = Set(prefix + "SetSpeed", "WeaponAssembly", "ProjectileSpeed", x + 1540, null, "float32");
            Exec(setRate, setSpeed);
            DataWire(assembly, "Component", asmRate, "Component");
            DataWire(assembly, "Component", asmSpeed, "Component");
            DataWire(part, "Component", rateDelta, "Component");
            DataWire(part, "Component", speedDelta, "Component");
            DataWire(asmRate, "Value", rateMath, "A");
            DataWire(rateDelta, "Value", rateMath, "B");
            DataWire(asmSpeed, "Value", speedMath, "A");
            DataWire(speedDelta, "Value", speedMath, "B");
            DataWire(rateMath, "Result", setRate, "Value");
            DataWire(assembly, "Component", setRate, "Target");
            DataWire(setRate, "Result", setSpeed, "Target");
            DataWire(speedMath, "Result", setSpeed, "Value");
            return (setRate, setSpeed);
        }

        public (NodeDocument Start, NodeDocument End) Derive(string prefix)
        {
            var rateBase = Literal(prefix + "RateBase", "float32", "6", 40);
            var speedBase = Literal(prefix + "SpeedBase", "float32", "40", 40);
            var rate0 = Assign(prefix + "Rate0", "Rate", 40, "float32");
            var speed0 = Assign(prefix + "Speed0", "Speed", 260, "float32");
            Exec(rate0, speed0);
            DataWire(rateBase, "Value", rate0, "Value");
            DataWire(speedBase, "Value", speed0, "Value");
            var holders = new List<NodeDocument>();
            var targets = new List<NodeDocument>();
            var previous = speed0;
            var cursor = 480;
            foreach (var slot in Slots)
            {
                var item = Call("Entity.GetHeldItem", prefix + slot.Field + "Item", cursor, ("Holder", "int64", null), ("Container", "string", slot.Container), ("Item", "int64", null));
                var comp = Component(prefix + slot.Field + "Part", "WeaponPart", cursor + 220, "int64");
                var proto = Field(prefix + slot.Field + "Proto", "WeaponPart", "Prototype", "string", cursor + 440);
                var rateDelta = Field(prefix + slot.Field + "Rate", "WeaponPart", "RateDelta", "float32", cursor + 660);
                var speedDelta = Field(prefix + slot.Field + "Speed", "WeaponPart", "SpeedDelta", "float32", cursor + 880);
                var empty = Literal(prefix + slot.Field + "Empty", "string", "", cursor + 1100);
                var zeroRate = Literal(prefix + slot.Field + "ZeroRate", "float32", "0", cursor + 1100);
                var zeroSpeed = Literal(prefix + slot.Field + "ZeroSpeed", "float32", "0", cursor + 1100);
                var chosenProto = Select(prefix + slot.Field + "ProtoPick", cursor + 1320, "string");
                var chosenRate = Select(prefix + slot.Field + "RatePick", cursor + 1540, "float32");
                var chosenSpeed = Select(prefix + slot.Field + "SpeedPick", cursor + 1760, "float32");
                var readRate = Read(prefix + slot.Field + "ReadRate", "Rate", cursor + 1980, "float32");
                var readSpeed = Read(prefix + slot.Field + "ReadSpeed", "Speed", cursor + 1980, "float32");
                var sumRate = Add(prefix + slot.Field + "SumRate", cursor + 2200, "float32");
                var sumSpeed = Add(prefix + slot.Field + "SumSpeed", cursor + 2420, "float32");
                var storeRate = Assign(prefix + slot.Field + "StoreRate", "Rate", cursor + 2640, "float32");
                var storeSpeed = Assign(prefix + slot.Field + "StoreSpeed", "Speed", cursor + 2860, "float32");
                var setSlot = Set(prefix + slot.Field + "Write", "WeaponAssembly", slot.Field, cursor + 3080);
                Exec(previous, storeRate);
                Exec(storeRate, storeSpeed);
                Exec(storeSpeed, setSlot);
                DataWire(item, "Item", comp, "Entity");
                DataWire(comp, "Component", proto, "Component");
                DataWire(comp, "Component", rateDelta, "Component");
                DataWire(comp, "Component", speedDelta, "Component");
                DataWire(comp, "Found", chosenProto, "Condition");
                DataWire(comp, "Found", chosenRate, "Condition");
                DataWire(comp, "Found", chosenSpeed, "Condition");
                DataWire(proto, "Value", chosenProto, "True");
                DataWire(empty, "Value", chosenProto, "False");
                DataWire(rateDelta, "Value", chosenRate, "True");
                DataWire(zeroRate, "Value", chosenRate, "False");
                DataWire(speedDelta, "Value", chosenSpeed, "True");
                DataWire(zeroSpeed, "Value", chosenSpeed, "False");
                DataWire(readRate, "Value", sumRate, "A");
                DataWire(chosenRate, "Result", sumRate, "B");
                DataWire(readSpeed, "Value", sumSpeed, "A");
                DataWire(chosenSpeed, "Result", sumSpeed, "B");
                DataWire(sumRate, "Result", storeRate, "Value");
                DataWire(sumSpeed, "Result", storeSpeed, "Value");
                DataWire(chosenProto, "Result", setSlot, "Value");
                holders.Add(item);
                targets.Add(setSlot);
                previous = setSlot;
                cursor += 3300;
            }

            var finalRate = Read(prefix + "FinalRate", "Rate", cursor, "float32");
            var finalSpeed = Read(prefix + "FinalSpeed", "Speed", cursor, "float32");
            var writeRate = Set(prefix + "WriteRate", "WeaponAssembly", "FireRate", cursor, null, "float32");
            var writeSpeed = Set(prefix + "WriteSpeed", "WeaponAssembly", "ProjectileSpeed", cursor + 220, null, "float32");
            Exec(previous, writeRate);
            Exec(writeRate, writeSpeed);
            DataWire(finalRate, "Value", writeRate, "Value");
            DataWire(finalSpeed, "Value", writeSpeed, "Value");
            targets.Add(writeRate);
            targets.Add(writeSpeed);
            _derives[rate0] = (holders, targets);
            return (rate0, writeSpeed);
        }

        public void WireDerive((NodeDocument Start, NodeDocument End) derived, NodeDocument gun, NodeDocument assembly)
        {
            var (holders, targets) = _derives[derived.Start];
            foreach (var holder in holders)
            {
                DataWire(gun, "Item", holder, "Holder");
            }

            foreach (var target in targets)
            {
                DataWire(assembly, "Component", target, "Target");
            }
        }

        public void WireStatus(NodeDocument bench)
        {
            foreach (var set in _status)
            {
                DataWire(bench, "Entity", set, "Owner");
            }
        }

        public NodeDocument Compare(string name, int x) =>
            Place(Node(name, "cmp.equal", null, Data("A", "string", true), Data("B", "string", true), Data("Result", "bool", false)), x, Y(300));

        public NodeDocument Logic(string type, string name, int x, string valueType = "bool") =>
            Place(Node(name, type, null, Data("A", valueType, true), Data("B", valueType, true), Data("Result", valueType, false)), x, Y(300));

        public NodeDocument Intersects(string name, int x) =>
            Place(Node(name, "List.Intersects", null, Data("List", "List<string>", true), Data("Other", "List<string>", true), Data("Result", "bool", false)), x, Y(300));

        public NodeDocument Select(string name, int x, string type) =>
            Place(Node(name, "Core.Select", null, Data("Condition", "bool", true), Data("True", type, true), Data("False", type, true), Data("Result", type, false)), x, Y(300));

        public NodeDocument Add(string name, int x, string type) =>
            Place(Node(name, "math.add", null, Data("A", type, true), Data("B", type, true), Data("Result", type, false)), x, Y(300));

        private NodeDocument Reason(string name, int x, NodeDocument label, string suffix)
        {
            var tail = Literal(name + "Tail", "string", suffix, x);
            var text = Add(name + "Text", x + 220, "string");
            var set = Call("Bui.Set", name, x + 440, ("Owner", "EntityUid", null), ("Name", "string", "Reason"), ("Value", "string", null), ("Success", "bool", null));
            DataWire(label, "Value", text, "A");
            DataWire(tail, "Value", text, "B");
            DataWire(text, "Result", set, "Value");
            _status.Add(set);
            return set;
        }

        private NodeDocument Place(NodeDocument node, int x, int y)
        {
            while (!_used.Add((x, y)))
            {
                y += 220;
            }

            _positions[node.Id.Value.ToString("D")] = new NodePosition(x, y);
            return node;
        }

        private void Wire(NodeDocument from, string fromPin, NodeDocument to, string toPin)
        {
            _connections.Add(new ConnectionDocument
            {
                FromNode = from.Id,
                FromPin = from.FindPin(fromPin, PinDirection.Output)!.Id,
                ToNode = to.Id,
                ToPin = to.FindPin(toPin, PinDirection.Input)!.Id
            });
        }

        private NodeDocument Node(string name, string type, Dictionary<string, string>? properties, params PinDocument[] pins)
        {
            var node = new NodeDocument
            {
                Name = name,
                NodeType = type,
                Properties = properties ?? [],
                Pins = pins.ToList()
            };
            _nodes.Add(node);
            return node;
        }

        private static PinDocument Exec(string name, bool input) => new()
        {
            Name = name,
            Direction = input ? PinDirection.Input : PinDirection.Output,
            Kind = PinKind.Execution
        };

        private static PinDocument Data(string name, string type, bool input, string? value = null) => new()
        {
            Name = name,
            Direction = input ? PinDirection.Input : PinDirection.Output,
            Kind = PinKind.Data,
            DataType = type,
            DefaultValue = value
        };

        private static ComponentSchemaDocument Schema(string id, string name, bool component, params (string Id, string Name, string Type, string Default)[] fields) => new()
        {
            Id = SchemaId.FromString(id),
            Name = name,
            Kind = component ? "Component" : "Struct",
            Fields = fields.Select(field => new ComponentFieldDocument
            {
                Id = FieldId.FromString(field.Id),
                Name = field.Name,
                TypeName = field.Type,
                DefaultValue = field.Default
            }).ToList()
        };
    }
}
