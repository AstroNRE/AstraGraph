using System.Reflection;
using AstraGraph.Core;
using AstraGraph.Runtime;
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
        var path = WriteProfile();
        if (path == null)
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
        var path = WriteProfile();
        if (path == null)
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
            ["ProjectileSpeed"] = "55",
            ["Heat"] = "40"
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
    public void WeaponCondition_ShotAddsHeatFoulingAndWear()
    {
        var path = WriteCondition();
        if (path == null)
        {
            Assert.Ignore("Night City checkout is not beside this library.");
        }

        var document = GraphSerializer.Deserialize(File.ReadAllText(path));
        var registry = TypeRegistry.CreateDefault();
        var analyzed = new SemanticAnalyzer(registry).Analyze(document);
        Assert.That(analyzed.Success, Is.True, analyzed.Diagnostics.ToString());
        var program = IrToBytecodeCompiler.Compile(AstToIrCompiler.Compile(analyzed.Program!), RevisionId.New(), "condition");
        var schema = SchemaDocuments.ToSchema(document.Schemas.Single(item => item.Name == "WeaponAssembly"), registry);
        var schemas = new AstraSchemaRegistry();
        schemas.RegisterSchema(schema);
        var source = new SchemaComponentSource(new DynamicComponentStore(), schemas);
        Assert.That(source.ApplyInitial(4, schema, new Dictionary<string, string>
        {
            ["FireRate"] = "6",
            ["ProjectileSpeed"] = "40"
        }, "WeaponAstraPistol", out _), Is.True);
        var services = new DefaultVmHostServices();
        services.UseSchemaComponents(source);
        services.PushEventContext(new AstraEventInvocationContext(AstraValue.FromEntityUid(4), null, new object()));
        var result = new AstraVm().Execute(program, program.FindEntryPoint("Shot")!, hostServices: services);
        services.PopEventContext();
        var stored = source.TryGet(4, "WeaponAssembly");
        Assert.That(stored.Read("Heat").AsDouble(), Is.EqualTo(4));
        Assert.That(stored.Read("Fouling").AsDouble(), Is.EqualTo(1));
        Assert.That(stored.Read("Wear").AsDouble(), Is.EqualTo(0.2).Within(0.0001));
        Assert.That(stored.Read("Malfunction").AsString(), Is.EqualTo("Clear"));
        Assert.That(stored.Read("Seed").AsDouble(), Is.EqualTo(0.51).Within(0.001));
        Assert.That(stored.Read("Risk").AsDouble(), Is.EqualTo(0.07).Within(0.001));
        Assert.That(result.Status, Is.EqualTo(VmExecutionStatus.Faulted));
    }

    [Test]
    public void WeaponProfile_StopsTheGunWhileItIsJammed()
    {
        var path = WriteProfile();
        if (path == null)
        {
            Assert.Ignore("Night City checkout is not beside this library.");
        }

        var document = GraphSerializer.Deserialize(File.ReadAllText(path));
        var registry = TypeRegistry.CreateDefault();
        var analyzed = new SemanticAnalyzer(registry).Analyze(document);
        Assert.That(analyzed.Success, Is.True, analyzed.Diagnostics.ToString());
        var program = IrToBytecodeCompiler.Compile(AstToIrCompiler.Compile(analyzed.Program!), RevisionId.New(), "profile-jam");
        var schema = SchemaDocuments.ToSchema(document.Schemas.Single(item => item.Name == "WeaponAssembly"), registry);
        var schemas = new AstraSchemaRegistry();
        schemas.RegisterSchema(schema);
        var source = new SchemaComponentSource(new DynamicComponentStore(), schemas);
        Assert.That(source.ApplyInitial(4, schema, new Dictionary<string, string>
        {
            ["FireRate"] = "8",
            ["ProjectileSpeed"] = "25",
            ["Malfunction"] = "Overheated bolt"
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
        Assert.That(ev.FireRate, Is.EqualTo(0f));
        Assert.That(ev.ProjectileSpeed, Is.EqualTo(25f));
    }

    [Test]
    public void WeaponCondition_HighHeatJamsTheBolt()
    {
        var stored = Shoot(new Dictionary<string, string>
        {
            ["FireRate"] = "8",
            ["Heat"] = "200"
        });
        Assert.That(stored.Read("Heat").AsDouble(), Is.EqualTo(204).Within(0.001));
        Assert.That(stored.Read("Malfunction").AsString(), Is.EqualTo("Overheated bolt"));
        Assert.That(stored.Read("Seed").AsDouble(), Is.EqualTo(0.51).Within(0.001));
        Assert.That(stored.Read("Risk").AsDouble(), Is.GreaterThan(1));
    }

    [Test]
    public void WeaponCondition_AnExistingJamKeepsItsReason()
    {
        var stored = Shoot(new Dictionary<string, string>
        {
            ["Malfunction"] = "Worn mechanism",
            ["Seed"] = "0.4"
        });
        Assert.That(stored.Read("Malfunction").AsString(), Is.EqualTo("Worn mechanism"));
        Assert.That(stored.Read("Seed").AsDouble(), Is.EqualTo(0.4).Within(0.001));
        Assert.That(stored.Read("Heat").AsDouble(), Is.EqualTo(4));
    }

    private static string? WriteProfile()
    {
        var root = NightCity();
        if (!Directory.Exists(root))
        {
            return null;
        }

        var graph = new BenchGraph();
        var refresh = graph.Event("Refresh", "GunRefreshModifiersEvent", "GunComponent", 40, 80);
        var assembly = graph.Component("Assembly", "WeaponAssembly", 360);
        var gate = graph.Branch("HasAssembly", 680);
        var fault = graph.Field("ReadFault", "WeaponAssembly", "Malfunction", "string", 1000);
        var isClear = graph.Equal("IsClear", "Clear", 1220);
        var clearGate = graph.Branch("Clear?", 1440);
        var rate = graph.Field("ReadRate", "WeaponAssembly", "FireRate", "float32", 1660);
        var useRate = graph.Assign("UseRate", "FireRate", 1880, "float32");
        var speed = graph.Field("ReadSpeed", "WeaponAssembly", "ProjectileSpeed", "float32", 2100);
        var useSpeed = graph.Assign("UseSpeed", "ProjectileSpeed", 2320, "float32");
        graph.UseRow(420);
        var zero = graph.Literal("ZeroRate", "float32", "0", 1660);
        var useZero = graph.Assign("UseZero", "FireRate", 1880, "float32");
        var useSpeedJam = graph.Assign("UseSpeedJam", "ProjectileSpeed", 2320, "float32");
        graph.Exec(refresh, gate);
        graph.Exec(gate, "True", clearGate);
        graph.Exec(clearGate, "True", useRate);
        graph.Exec(useRate, useSpeed);
        graph.Exec(clearGate, "False", useZero);
        graph.Exec(useZero, useSpeedJam);
        graph.DataWire(refresh, "Entity", assembly, "Entity");
        graph.DataWire(assembly, "Found", gate, "Condition");
        graph.DataWire(assembly, "Component", fault, "Component");
        graph.DataWire(fault, "Value", isClear, "A");
        graph.DataWire(isClear, "Result", clearGate, "Condition");
        graph.DataWire(assembly, "Component", rate, "Component");
        graph.DataWire(assembly, "Component", speed, "Component");
        graph.DataWire(rate, "Value", useRate, "Value");
        graph.DataWire(speed, "Value", useSpeed, "Value");
        graph.DataWire(zero, "Value", useZero, "Value");
        graph.DataWire(speed, "Value", useSpeedJam, "Value");
        return WriteGraph(graph.Document(
            "a1000000-0000-4000-8000-0000000000a1",
            "WeaponProfile",
            "When a gun refreshes, copy the assembly profile onto the shot. A malfunction stops the cycle.",
            [
                new GraphVariableDocument { Name = "FireRate", TypeName = "float32" },
                new GraphVariableDocument { Name = "ProjectileSpeed", TypeName = "float32" }
            ]), "WeaponProfile.agraph");
    }

    private static string? WriteCondition()
    {
        var root = NightCity();
        if (!Directory.Exists(root))
        {
            return null;
        }

        var graph = new BenchGraph();
        // GunComponent + GunShotEvent is already taken by the city perception system.
        // Robust allows one handler per component and event, so the shot is heard on the chamber the pistol already has.
        var shot = graph.Event("Shot", "GunShotEvent", "ChamberMagazineAmmoProviderComponent", 40, 80);
        var assembly = graph.Component("Assembly", "WeaponAssembly", 360);
        var gate = graph.Branch("Assembly?", 680);
        var heat = graph.Field("Heat", "WeaponAssembly", "Heat", "float32", 1000);
        var heatStep = graph.Literal("HeatStep", "float32", "4", 1220);
        var heatSum = graph.Add("HeatSum", 1440, "float32");
        var storeHeat = graph.Assign("StoreHeat", "HeatNow", 1660, "float32");
        var storedHeat = graph.Read("StoredHeat", "HeatNow", 1880, "float32");
        var setHeat = graph.Set("SetHeat", "WeaponAssembly", "Heat", 2100, null, "float32");
        var fouling = graph.Field("Fouling", "WeaponAssembly", "Fouling", "float32", 1880);
        var foulingStep = graph.Literal("FoulingStep", "float32", "1", 2100);
        var foulingSum = graph.Add("FoulingSum", 2320, "float32");
        var setFouling = graph.Set("SetFouling", "WeaponAssembly", "Fouling", 2540, null, "float32");
        var wear = graph.Field("Wear", "WeaponAssembly", "Wear", "float32", 2760);
        var wearStep = graph.Literal("WearStep", "float32", "0.2", 2980);
        var wearSum = graph.Add("WearSum", 3200, "float32");
        var setWear = graph.Set("SetWear", "WeaponAssembly", "Wear", 3420, null, "float32");
        var rate = graph.Field("Rate", "WeaponAssembly", "FireRate", "float32", 16000);
        var rateText = graph.Call("Text.WithNumber", "RateText", 16220, ("Label", "string", "Fire rate"), ("Number", "float64", null), ("Text", "string", null));
        var heatText = graph.JoinStat("HeatText", 16440, rateText, "Text", assembly, "Heat", "Heat");
        var foulingText = graph.JoinStat("FoulingText", 17640, heatText, "Result", assembly, "Fouling", "Fouling");
        var wearText = graph.JoinStat("WearText", 18840, foulingText, "Result", assembly, "Wear", "Wear");
        var calibrationText = graph.JoinStat("CalibrationText", 20040, wearText, "Result", assembly, "Calibration", "Calibration");
        var faultText = graph.JoinText("FaultText", 21240, calibrationText, "Result", assembly, "Malfunction", "Malfunction: ");
        var fault = graph.Field("Fault", "WeaponAssembly", "Malfunction", "string", 3640);
        var already = graph.NotEqual("Already", "Clear", 3860);
        var alreadyGate = graph.Branch("Already?", 4080);
        var seed = graph.Field("Seed", "WeaponAssembly", "Seed", "float32", 4300);
        var half = graph.Literal("Half", "float32", "0.5", 4520);
        var seedHalf = graph.Mul("SeedHalf", 4740);
        var bump = graph.Literal("Bump", "float32", "0.31", 4960);
        var seedNext = graph.Add("SeedNext", 5180, "float32");
        var storeSeed = graph.Assign("StoreSeed", "SeedNow", 5400, "float32");
        var storedSeed = graph.Read("StoredSeed", "SeedNow", 5620, "float32");
        var setSeed = graph.Set("SetSeed", "WeaponAssembly", "Seed", 5840, null, "float32");
        var heatScale = graph.Literal("HeatScale", "float32", "0.005", 6060);
        var heatRisk = graph.Mul("HeatRisk", 6280);
        var foulScale = graph.Literal("FoulScale", "float32", "0.04", 6500);
        var foulRisk = graph.Mul("FoulRisk", 6720);
        var wearScale = graph.Literal("WearScale", "float32", "0.05", 6940);
        var wearRisk = graph.Mul("WearRisk", 7160);
        var one = graph.Literal("One", "float32", "1", 7380);
        var calibration = graph.Field("Calibration", "WeaponAssembly", "Calibration", "float32", 7600);
        var calGap = graph.Sub("CalGap", 7820);
        var calScale = graph.Literal("CalScale", "float32", "0.3", 8040);
        var calRisk = graph.Mul("CalRisk", 8260);
        var riskHeatFoul = graph.Add("RiskHeatFoul", 8480, "float32");
        var riskWear = graph.Add("RiskWear", 8700, "float32");
        var riskSum = graph.Add("RiskSum", 8920, "float32");
        var storeRisk = graph.Assign("StoreRisk", "RiskNow", 9140, "float32");
        var storedRisk = graph.Read("StoredRisk", "RiskNow", 9360, "float32");
        var setRisk = graph.Set("SetRisk", "WeaponAssembly", "Risk", 9580, null, "float32");
        var roll = graph.Less("Roll", 9800);
        var rollGate = graph.Branch("Roll?", 10020);
        var foulLimit = graph.Literal("FoulLimit", "float32", "6", 10240);
        var foulHot = graph.Greater("FoulHot", 10460);
        var foulGate = graph.Branch("Foul?", 10680);
        var heatLimit = graph.Literal("HeatLimit", "float32", "30", 10900);
        var heatHot = graph.Greater("HeatHot", 11120);
        var heatGate = graph.Branch("Heat?", 11340);
        var wearLimit = graph.Literal("WearLimit", "float32", "2", 11560);
        var wearHot = graph.Greater("WearHot", 11780);
        var wearGate = graph.Branch("Wear?", 12000);
        var calLimit = graph.Literal("CalLimit", "float32", "1", 12220);
        var calOff = graph.Less("CalOff", 12440);
        var calGate = graph.Branch("Cal?", 12660);
        var openRate = graph.Call("Component.SetField", "WriteRate", 12880, ("Entity", "EntityUid", null), ("Component", "string", "GunComponent"), ("Field", "string", "FireRate"), ("Value", "float64", null), ("Success", "bool", null));
        var openModified = graph.Call("Component.SetField", "WriteModified", 13100, ("Entity", "EntityUid", null), ("Component", "string", "GunComponent"), ("Field", "string", "FireRateModified"), ("Value", "float64", null), ("Success", "bool", null));
        var openDescribe = graph.Call("Meta.SetDescription", "Describe", 13320, ("Entity", "EntityUid", null), ("Text", "string", null), ("Success", "bool", null));
        var openRefresh = graph.Call("System.Invoke", "Refresh", 13540, ("System", "string", "SharedGunSystem"), ("Method", "string", "RefreshModifiers"), ("Entity", "EntityUid", null), ("Success", "bool", null));
        graph.Exec(shot, gate);
        graph.Exec(gate, "True", storeHeat);
        graph.Exec(storeHeat, setHeat);
        graph.Exec(setHeat, setFouling);
        graph.Exec(setFouling, setWear);
        graph.Exec(setWear, alreadyGate);
        graph.Exec(alreadyGate, "True", Stop("Stay", 4300));
        graph.Exec(alreadyGate, "False", storeSeed);
        graph.Exec(storeSeed, setSeed);
        graph.Exec(setSeed, storeRisk);
        graph.Exec(storeRisk, setRisk);
        graph.Exec(setRisk, rollGate);
        graph.Exec(rollGate, "False", openRate);
        graph.Exec(openRate, openModified);
        graph.Exec(openModified, openDescribe);
        graph.Exec(openDescribe, openRefresh);
        graph.Exec(rollGate, "True", foulGate);
        graph.Exec(foulGate, "True", Blame("Fouling", "Fouling in the feed", 10900));
        graph.Exec(foulGate, "False", heatGate);
        graph.Exec(heatGate, "True", Blame("Overheat", "Overheated bolt", 11560));
        graph.Exec(heatGate, "False", wearGate);
        graph.Exec(wearGate, "True", Blame("Wear", "Worn mechanism", 12220));
        graph.Exec(wearGate, "False", calGate);
        graph.Exec(calGate, "True", Blame("Zero", "Unzeroed optic", 12880));
        graph.Exec(calGate, "False", Blame("Feed", "Failure to feed", 13540));
        graph.DataWire(shot, "Entity", assembly, "Entity");
        graph.DataWire(assembly, "Found", gate, "Condition");
        graph.DataWire(assembly, "Component", heat, "Component");
        graph.DataWire(assembly, "Component", fouling, "Component");
        graph.DataWire(assembly, "Component", wear, "Component");
        graph.DataWire(assembly, "Component", rate, "Component");
        graph.DataWire(assembly, "Component", fault, "Component");
        graph.DataWire(fault, "Value", already, "A");
        graph.DataWire(already, "Result", alreadyGate, "Condition");
        graph.DataWire(assembly, "Component", seed, "Component");
        graph.DataWire(seed, "Value", seedHalf, "A");
        graph.DataWire(half, "Value", seedHalf, "B");
        graph.DataWire(seedHalf, "Result", seedNext, "A");
        graph.DataWire(bump, "Value", seedNext, "B");
        graph.DataWire(seedNext, "Result", storeSeed, "Value");
        graph.DataWire(storedSeed, "Value", setSeed, "Value");
        graph.DataWire(setWear, "Result", setSeed, "Target");
        graph.DataWire(heat, "Value", heatRisk, "A");
        graph.DataWire(heatScale, "Value", heatRisk, "B");
        graph.DataWire(fouling, "Value", foulRisk, "A");
        graph.DataWire(foulScale, "Value", foulRisk, "B");
        graph.DataWire(wear, "Value", wearRisk, "A");
        graph.DataWire(wearScale, "Value", wearRisk, "B");
        graph.DataWire(one, "Value", calGap, "A");
        graph.DataWire(calibration, "Value", calGap, "B");
        graph.DataWire(assembly, "Component", calibration, "Component");
        graph.DataWire(calGap, "Result", calRisk, "A");
        graph.DataWire(calScale, "Value", calRisk, "B");
        graph.DataWire(heatRisk, "Result", riskHeatFoul, "A");
        graph.DataWire(foulRisk, "Result", riskHeatFoul, "B");
        graph.DataWire(riskHeatFoul, "Result", riskWear, "A");
        graph.DataWire(wearRisk, "Result", riskWear, "B");
        graph.DataWire(riskWear, "Result", riskSum, "A");
        graph.DataWire(calRisk, "Result", riskSum, "B");
        graph.DataWire(riskSum, "Result", storeRisk, "Value");
        graph.DataWire(storedRisk, "Value", setRisk, "Value");
        graph.DataWire(setSeed, "Result", setRisk, "Target");
        graph.DataWire(storedSeed, "Value", roll, "A");
        graph.DataWire(storedRisk, "Value", roll, "B");
        graph.DataWire(roll, "Result", rollGate, "Condition");
        graph.DataWire(fouling, "Value", foulHot, "A");
        graph.DataWire(foulLimit, "Value", foulHot, "B");
        graph.DataWire(foulHot, "Result", foulGate, "Condition");
        graph.DataWire(heat, "Value", heatHot, "A");
        graph.DataWire(heatLimit, "Value", heatHot, "B");
        graph.DataWire(heatHot, "Result", heatGate, "Condition");
        graph.DataWire(wear, "Value", wearHot, "A");
        graph.DataWire(wearLimit, "Value", wearHot, "B");
        graph.DataWire(wearHot, "Result", wearGate, "Condition");
        graph.DataWire(calibration, "Value", calOff, "A");
        graph.DataWire(calLimit, "Value", calOff, "B");
        graph.DataWire(calOff, "Result", calGate, "Condition");
        graph.DataWire(shot, "Entity", openRate, "Entity");
        graph.DataWire(rate, "Value", openRate, "Value");
        graph.DataWire(shot, "Entity", openModified, "Entity");
        graph.DataWire(rate, "Value", openModified, "Value");
        graph.DataWire(rate, "Value", rateText, "Number");
        graph.DataWire(faultText, "Result", openDescribe, "Text");
        graph.DataWire(shot, "Entity", openDescribe, "Entity");
        graph.DataWire(shot, "Entity", openRefresh, "Entity");
        graph.DataWire(assembly, "Component", setHeat, "Target");
        graph.DataWire(heat, "Value", heatSum, "A");
        graph.DataWire(heatStep, "Value", heatSum, "B");
        graph.DataWire(heatSum, "Result", storeHeat, "Value");
        graph.DataWire(storedHeat, "Value", setHeat, "Value");
        graph.DataWire(setHeat, "Result", setFouling, "Target");
        graph.DataWire(fouling, "Value", foulingSum, "A");
        graph.DataWire(foulingStep, "Value", foulingSum, "B");
        graph.DataWire(foulingSum, "Result", setFouling, "Value");
        graph.DataWire(setFouling, "Result", setWear, "Target");
        graph.DataWire(wear, "Value", wearSum, "A");
        graph.DataWire(wearStep, "Value", wearSum, "B");
        graph.DataWire(wearSum, "Result", setWear, "Value");
        return WriteGraph(graph.Document(
            "e1000000-0000-4000-8000-0000000000e1",
            "WeaponCondition",
            "Each shot adds heat, fouling, and wear, then may jam. A clear gun keeps the part fire rate.",
            [
                new GraphVariableDocument { Name = "HeatNow", TypeName = "float32" },
                new GraphVariableDocument { Name = "SeedNow", TypeName = "float32" },
                new GraphVariableDocument { Name = "RiskNow", TypeName = "float32" }
            ]), "WeaponCondition.agraph");

        NodeDocument Stop(string name, int x)
        {
            graph.UseRow(760);
            var writeRate = graph.Call("Component.SetField", name + "Rate", x, ("Entity", "EntityUid", null), ("Component", "string", "GunComponent"), ("Field", "string", "FireRate"), ("Value", "float64", "0"), ("Success", "bool", null));
            var writeModified = graph.Call("Component.SetField", name + "Modified", x + 220, ("Entity", "EntityUid", null), ("Component", "string", "GunComponent"), ("Field", "string", "FireRateModified"), ("Value", "float64", "0"), ("Success", "bool", null));
            var describe = graph.Call("Meta.SetDescription", name + "Describe", x + 440, ("Entity", "EntityUid", null), ("Text", "string", null), ("Success", "bool", null));
            var refreshGun = graph.Call("System.Invoke", name + "Refresh", x + 660, ("System", "string", "SharedGunSystem"), ("Method", "string", "RefreshModifiers"), ("Entity", "EntityUid", null), ("Success", "bool", null));
            graph.Exec(writeRate, writeModified);
            graph.Exec(writeModified, describe);
            graph.Exec(describe, refreshGun);
            graph.DataWire(shot, "Entity", writeRate, "Entity");
            graph.DataWire(shot, "Entity", writeModified, "Entity");
            graph.DataWire(faultText, "Result", describe, "Text");
            graph.DataWire(shot, "Entity", describe, "Entity");
            graph.DataWire(shot, "Entity", refreshGun, "Entity");
            return writeRate;
        }

        NodeDocument Blame(string name, string text, int x)
        {
            graph.UseRow(760);
            var set = graph.Set(name, "WeaponAssembly", "Malfunction", x, text, "string");
            graph.DataWire(setRisk, "Result", set, "Target");
            graph.Exec(set, Stop(name + "Stop", x + 220));
            return set;
        }
    }

    private static SchemaComponentValue Shoot(Dictionary<string, string> fields)
    {
        var path = WriteCondition();
        Assert.That(path, Is.Not.Null, "Night City checkout is not beside this library.");
        var document = GraphSerializer.Deserialize(File.ReadAllText(path!));
        var registry = TypeRegistry.CreateDefault();
        var analyzed = new SemanticAnalyzer(registry).Analyze(document);
        Assert.That(analyzed.Success, Is.True, analyzed.Diagnostics.ToString());
        var program = IrToBytecodeCompiler.Compile(AstToIrCompiler.Compile(analyzed.Program!), RevisionId.New(), "condition");
        var schema = SchemaDocuments.ToSchema(document.Schemas.Single(item => item.Name == "WeaponAssembly"), registry);
        var schemas = new AstraSchemaRegistry();
        schemas.RegisterSchema(schema);
        var source = new SchemaComponentSource(new DynamicComponentStore(), schemas);
        Assert.That(source.ApplyInitial(4, schema, fields, "WeaponAstraPistol", out _), Is.True);
        var services = new DefaultVmHostServices();
        services.UseSchemaComponents(source);
        services.PushEventContext(new AstraEventInvocationContext(AstraValue.FromEntityUid(4), null, new object()));
        var result = new AstraVm().Execute(program, program.FindEntryPoint("Shot")!, hostServices: services);
        services.PopEventContext();
        Assert.That(result.Status, Is.EqualTo(VmExecutionStatus.Faulted));
        return source.TryGet(4, "WeaponAssembly");
    }

    [Test]
    public void Host_ResumesDoAfterWithTheWaitingVariables()
    {
        var pool = new ConstantPool();
        var entryName = pool.GetOrAddString("Wait");
        var marker = pool.GetOrAddString("Marker");
        var note = pool.GetOrAddString("Note");
        var function = new BytecodeFunction(entryName, 4, 0,
        [
            new BytecodeInstruction((byte)IrOpCode.LoadVariable, 1, marker, 0, 0),
            new BytecodeInstruction((byte)IrOpCode.CallNative, BytecodeInstruction.NoRegister, note, 1, 1)
        ]);
        var graphId = GraphId.FromString("f1000000-0000-4000-8000-0000000000f1");
        var program = new BytecodeProgram(graphId, RevisionId.New(), "wait", pool);
        program.EntryPoints.Add(function);
        var host = new AstraGraphHost();
        var seen = "";
        ((DefaultVmHostServices)host.HostServices).RegisterNativeMethod("Note", args =>
        {
            seen = args[0].AsString() ?? "";
            return AstraValue.Null;
        });
        host.HostServices.SetVariable(SymbolId.Empty, "Marker", AstraValue.FromString("fitting"));
        var snapshot = host.HostServices.SnapshotVariables();
        host.HostServices.SetVariable(SymbolId.Empty, "Marker", AstraValue.FromString("outer"));
        var registers = new AstraValue[4];
        registers[0] = AstraValue.FromDouble(1);
        host.ScheduleLatent(
            graphId,
            program,
            function,
            new ContinuationState(ContinuationKind.DoAfter, Guid.NewGuid(), 0, registers, 1),
            null,
            snapshot);
        host.Update(10, 1);
        Assert.That(host.PendingLatent, Is.EqualTo(1));
        Assert.That(seen, Is.Empty);
        host.Update(11, 2);
        Assert.That(host.PendingLatent, Is.EqualTo(0));
        Assert.That(seen, Is.EqualTo("fitting"));
        Assert.That(host.HostServices.GetVariable(SymbolId.Empty, "Marker").AsString(), Is.EqualTo("outer"));
    }

    private static string WriteGraph(GraphDocument document, string fileName)
    {
        var path = Path.Combine(NightCity(), "Resources", "AstraGraph", "Systems", "Weapons", "Modular", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, GraphSerializer.Serialize(document));
        return path;
    }

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
        Assert.That(opcodes, Does.Contain(IrOpCode.YieldContinuation));
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
        graph.UseRow(1280);
        var installWait = graph.Call("Bui.Set", "InstallWait", 6820, ("Owner", "EntityUid", null), ("Name", "string", "Reason"), ("Value", "string", "Fitting the part."), ("Success", "bool", null));
        var installAfter = graph.DoAfter("InstallAfter", 7040, "2");
        graph.UseRow(900);
        var insert = graph.Call("Container.Insert", "Insert", 7260, ("Owner", "int64", null), ("Container", "string", null), ("Item", "int64", null), ("Success", "bool", null));
        var inserted = graph.Branch("Inserted?", 7480);
        var installedMsg = graph.Call("Bui.Set", "InstalledMsg", 7700, ("Owner", "EntityUid", null), ("Name", "string", "Reason"), ("Value", "string", "Installed."), ("Success", "bool", null));
        var unsetCal = graph.Set("UnsetCal", "WeaponAssembly", "Calibration", 7920, "0", "float32");
        var rejectPrefix = graph.Literal("RejectPrefix", "string", "Did not fit into ", 6960);
        var rejectText = graph.Add("RejectText", 7180, "string");
        var rejected = graph.Call("Bui.Set", "Rejected", 7400, ("Owner", "EntityUid", null), ("Name", "string", "Reason"), ("Value", "string", null), ("Success", "bool", null));
        graph.UseRow(0);
        var afterInstall = graph.Refresh("Done", 4640);
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
        graph.UseRow(1860);
        var removeWait = graph.Call("Bui.Set", "RemoveWait", 3100, ("Owner", "EntityUid", null), ("Name", "string", "Reason"), ("Value", "string", "Removing the part."), ("Success", "bool", null));
        var removeAfter = graph.DoAfter("RemoveAfter", 3320, "1.5");
        graph.UseRow(1480);
        var takePart = graph.Call("Container.Remove", "TakePart", 3540, ("Owner", "int64", null), ("Container", "string", null), ("Item", "int64", null), ("Success", "bool", null));
        var returnPart = graph.Call("Container.Insert", "ReturnPart", 3760, ("Owner", "EntityUid", null), ("Container", "string", "storagebase"), ("Item", "int64", null), ("Success", "bool", null));
        var unsetCalRemove = graph.Set("UnsetCalRemove", "WeaponAssembly", "Calibration", 3980, "0", "float32");
        var removed = graph.Call("Bui.Set", "Removed", 4200, ("Owner", "EntityUid", null), ("Name", "string", "Reason"), ("Value", "string", "Part returned to the bench"), ("Success", "bool", null));
        var afterRemove = graph.Refresh("Off", 4420);
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
        graph.Exec(slotFullGate, "False", installWait);
        graph.Exec(installWait, installAfter);
        graph.Exec(installAfter, insert);
        graph.Exec(insert, inserted);
        graph.Exec(inserted, "True", installedMsg);
        graph.Exec(installedMsg, afterInstall);
        graph.Exec(graph.TailEnd(afterInstall), unsetCal);
        graph.Exec(inserted, "False", rejected);
        graph.Exec(installGate, "False", removeGate);
        graph.Exec(removeGate, "True", removeGunGate);
        graph.Exec(removeGunGate, "False", noRemoveGun);
        graph.Exec(removeGunGate, "True", removePartGate);
        graph.Exec(removePartGate, "False", noPartInstalled);
        graph.Exec(removePartGate, "True", removeCompGate);
        graph.Exec(removeCompGate, "True", removeAssemblyGate);
        graph.Exec(removeAssemblyGate, "True", removeWait);
        graph.Exec(removeWait, removeAfter);
        graph.Exec(removeAfter, takePart);
        graph.Exec(takePart, returnPart);
        graph.Exec(returnPart, removed);
        graph.Exec(removed, afterRemove);
        graph.Exec(graph.TailEnd(afterRemove), unsetCalRemove);
        var cleanGate = graph.Tune("Clean", "Clean", "Fouling", "0", "Fouling cleared.", 3600, action, message, "float32", "3", "Cleaning the weapon.");
        var calibrateGate = graph.Tune("Calibrate", "Calibrate", "Calibration", "1", "Calibrated.", 5600, action, message, "float32", "2.5", "Zeroing the optic.");
        var clearGate = graph.Tune("Clear", "Clear", "Malfunction", "Clear", "Malfunction cleared.", 7600, action, message, "string", "2", "Clearing the malfunction.");
        graph.Exec(removeGate, "False", cleanGate);
        graph.Exec(cleanGate, "False", calibrateGate);
        graph.Exec(calibrateGate, "False", clearGate);
        graph.Exec(clearGate, "False", selectGate);
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
        graph.DataWire(message, "Entity", installWait, "Owner");
        graph.DataWire(message, "Entity", removeWait, "Owner");
        graph.DataWire(message, "Entity", slotFullReason, "Owner");
        graph.DataWire(gun, "Item", slotHeld, "Holder");
        graph.DataWire(slot, "Value", slotHeld, "Container");
        graph.DataWire(slotHeld, "Item", slotFull, "A");
        graph.DataWire(slotFull, "Result", slotFullGate, "Condition");
        graph.DataWire(message, "Entity", installedMsg, "Owner");
        graph.DataWire(assembly, "Component", unsetCal, "Target");
        graph.DataWire(removeAssembly, "Component", unsetCalRemove, "Target");
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

        public NodeDocument Refresh(string prefix, int x)
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
            var rateBase = Literal(prefix + "RateBase", "float32", "6", x + 3180);
            var speedBase = Literal(prefix + "SpeedBase", "float32", "40", x + 3180);
            var rate0 = Assign(prefix + "Rate0", "Rate", x + 3400, "float32");
            var speed0 = Assign(prefix + "Speed0", "Speed", x + 3400, "float32");
            var slotList = Data("List.Create", prefix + "SlotList", x + 3620, ("List", "List<EntityUid>", null));
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
            var partRate = Field(prefix + "Rate", "WeaponPart", "RateDelta", "float32", x + 6260);
            var partSpeed = Field(prefix + "Speed", "WeaponPart", "SpeedDelta", "float32", x + 6260);
            var readRate = Read(prefix + "ReadRate", "Rate", x + 6480, "float32");
            var readSpeed = Read(prefix + "ReadSpeed", "Speed", x + 6480, "float32");
            var sumRate = Add(prefix + "SumRate", x + 6700, "float32");
            var sumSpeed = Add(prefix + "SumSpeed", x + 6700, "float32");
            var storeRate = Assign(prefix + "StoreRate", "Rate", x + 6920, "float32");
            var storeSpeed = Assign(prefix + "StoreSpeed", "Speed", x + 6920, "float32");
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
            Exec(assemblyGate, "True", rate0);
            Exec(rate0, speed0);
            Exec(speed0, addBarrel.Add);
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
            Exec(installedStore, storeRate);
            Exec(storeRate, storeSpeed);
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
            DataWire(rateBase, "Value", rate0, "Value");
            DataWire(speedBase, "Value", speed0, "Value");
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
            DataWire(installedComp, "Component", partRate, "Component");
            DataWire(installedComp, "Component", partSpeed, "Component");
            DataWire(readRate, "Value", sumRate, "A");
            DataWire(partRate, "Value", sumRate, "B");
            DataWire(readSpeed, "Value", sumSpeed, "A");
            DataWire(partSpeed, "Value", sumSpeed, "B");
            DataWire(sumRate, "Result", storeRate, "Value");
            DataWire(sumSpeed, "Result", storeSpeed, "Value");
            DataWire(installedBuilt, "Value", installedJson, "List");
            DataWire(installedJson, "Rows", installedPublish, "Value");
            _refreshes[clear] = (contents, publish, gun, installedPublish);
            return clear;

            NodeDocument GunTail(string name, int tailX)
            {
                var totalRate = Read(name + "TotalRate", "Rate", tailX, "float32");
                var totalSpeed = Read(name + "TotalSpeed", "Speed", tailX, "float32");
                var writeAsmRate = Set(name + "AsmRate", "WeaponAssembly", "FireRate", tailX, null, "float32");
                var writeAsmSpeed = Set(name + "AsmSpeed", "WeaponAssembly", "ProjectileSpeed", tailX + 220, null, "float32");
                var writeRate = Call("Component.SetField", name + "WriteRate", tailX + 440, ("Entity", "int64", null), ("Component", "string", "GunComponent"), ("Field", "string", "FireRate"), ("Value", "float64", null), ("Success", "bool", null));
                var writeModified = Call("Component.SetField", name + "WriteModified", tailX + 660, ("Entity", "int64", null), ("Component", "string", "GunComponent"), ("Field", "string", "FireRateModified"), ("Value", "float64", null), ("Success", "bool", null));
                var rateText = Call("Text.WithNumber", name + "RateText", tailX + 880, ("Label", "string", "Fire rate"), ("Number", "float64", null), ("Text", "string", null));
                var heatText = JoinStat(name + "Heat", tailX + 1100, rateText, "Text", assembly, "Heat", "Heat");
                var foulingText = JoinStat(name + "Fouling", tailX + 2100, heatText, "Result", assembly, "Fouling", "Fouling");
                var wearText = JoinStat(name + "Wear", tailX + 3100, foulingText, "Result", assembly, "Wear", "Wear");
                var calibrationText = JoinStat(name + "Calibration", tailX + 4100, wearText, "Result", assembly, "Calibration", "Calibration");
                var describe = Call("Meta.SetDescription", name + "Describe", tailX + 5100, ("Entity", "int64", null), ("Text", "string", null), ("Success", "bool", null));
                var refreshGun = Call("System.Invoke", name + "RefreshGun", tailX + 5320, ("System", "string", "SharedGunSystem"), ("Method", "string", "RefreshModifiers"), ("Entity", "int64", null), ("Success", "bool", null));
                Exec(writeAsmRate, writeAsmSpeed);
                Exec(writeAsmSpeed, writeRate);
                Exec(writeRate, writeModified);
                Exec(writeModified, refreshGun);
                Exec(refreshGun, describe);
                DataWire(assembly, "Component", writeAsmRate, "Target");
                DataWire(totalRate, "Value", writeAsmRate, "Value");
                DataWire(writeAsmRate, "Result", writeAsmSpeed, "Target");
                DataWire(totalSpeed, "Value", writeAsmSpeed, "Value");
                DataWire(gun, "Item", writeRate, "Entity");
                DataWire(totalRate, "Value", writeRate, "Value");
                DataWire(gun, "Item", writeModified, "Entity");
                DataWire(totalRate, "Value", writeModified, "Value");
                DataWire(totalRate, "Value", rateText, "Number");
                DataWire(calibrationText, "Result", describe, "Text");
                DataWire(gun, "Item", describe, "Entity");
                DataWire(gun, "Item", refreshGun, "Entity");
                _tails[clear] = describe;
                return writeAsmRate;
            }

            (NodeDocument Held, NodeDocument Add) Held(string container, int at)
            {
                var held = Call("Entity.GetHeldItem", prefix + container + "Held", at, ("Holder", "int64", null), ("Container", "string", container), ("Item", "EntityUid", null));
                var add = Call("List.Add", prefix + container + "Add", at, ("List", "List<EntityUid>", null), ("Item", "EntityUid", null), ("ListOut", "List<EntityUid>", null));
                return (held, add);
            }
        }

        public NodeDocument TailEnd(NodeDocument clear) => _tails[clear];

        public void WireRefresh(NodeDocument clear, NodeDocument bench)
        {
            var (contents, publish, gun, installedPublish) = _refreshes[clear];
            DataWire(bench, "Entity", contents, "Owner");
            DataWire(bench, "Entity", publish, "Owner");
            DataWire(bench, "Entity", gun, "Holder");
            DataWire(bench, "Entity", installedPublish, "Owner");
        }

        private readonly Dictionary<NodeDocument, (NodeDocument Contents, NodeDocument Publish, NodeDocument Gun, NodeDocument InstalledPublish)> _refreshes = [];
        private readonly Dictionary<NodeDocument, NodeDocument> _tails = [];

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

        public GraphDocument Document(
            string id = "c1000000-0000-4000-8000-0000000000c1",
            string name = "WeaponBench",
            string description = "Bench lists parts, shows whether a part fits the pistol, installs a direct fit, and returns that part to storage. The client only sends the action.",
            GraphVariableDocument[]? variables = null) => new()
        {
            Id = GraphId.FromString(id),
            Name = name,
            Kind = GraphKind.System,
            Side = GraphSide.Server,
            Metadata = new GraphMetadata { Description = description },
            Variables = variables?.ToList() ??
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
                    ("11aa11aa-11aa-41aa-81aa-11aa11aa11b7", "MuzzleAccepts", "string", ""),
                    ("11aa11aa-11aa-41aa-81aa-11aa11aa11b8", "Heat", "float32", "0"),
                    ("11aa11aa-11aa-41aa-81aa-11aa11aa11b9", "Fouling", "float32", "0"),
                    ("11aa11aa-11aa-41aa-81aa-11aa11aa11ba", "Wear", "float32", "0"),
                    ("11aa11aa-11aa-41aa-81aa-11aa11aa11bb", "Calibration", "float32", "1"),
                    ("11aa11aa-11aa-41aa-81aa-11aa11aa11bc", "Malfunction", "string", "Clear"),
                    ("11aa11aa-11aa-41aa-81aa-11aa11aa11bd", "Seed", "float32", "0.4"),
                    ("11aa11aa-11aa-41aa-81aa-11aa11aa11be", "Risk", "float32", "0")),
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

        public NodeDocument Mul(string name, int x) =>
            Place(Node(name, "math.multiply", null, Data("A", "float32", true), Data("B", "float32", true), Data("Result", "float32", false)), x, Y(300));

        public NodeDocument Sub(string name, int x) =>
            Place(Node(name, "math.subtract", null, Data("A", "float32", true), Data("B", "float32", true), Data("Result", "float32", false)), x, Y(300));

        public NodeDocument Less(string name, int x) =>
            Place(Node(name, "cmp.lessthan", null, Data("A", "float32", true), Data("B", "float32", true), Data("Result", "bool", false)), x, Y(300));

        public NodeDocument Greater(string name, int x) =>
            Place(Node(name, "cmp.greaterthan", null, Data("A", "float32", true), Data("B", "float32", true), Data("Result", "bool", false)), x, Y(300));

        public NodeDocument NotEqual(string name, string literal, int x) =>
            Place(Node(name, "cmp.notequal", null, Data("A", "string", true), Data("B", "string", true, literal), Data("Result", "bool", false)), x, Y(300));

        public NodeDocument DoAfter(string name, int x, string seconds) =>
            Place(Node(name, "Flow.DoAfter", null, Exec("In", true), Exec("Out", false), Data("Delay", "float32", true, seconds)), x, Y(80));

        public NodeDocument JoinStat(string name, int x, NodeDocument left, string leftPin, NodeDocument assembly, string field, string label)
        {
            var sep = Literal(name + "Sep", "string", " · ", x);
            var value = Field(name + "Field", "WeaponAssembly", field, "float32", x + 220);
            var line = Call("Text.WithNumber", name + "Line", x + 440, ("Label", "string", label), ("Number", "float64", null), ("Text", "string", null));
            var withSep = Add(name + "Sepd", x + 660, "string");
            var joined = Add(name + "Joined", x + 880, "string");
            DataWire(left, leftPin, withSep, "A");
            DataWire(sep, "Value", withSep, "B");
            DataWire(withSep, "Result", joined, "A");
            DataWire(line, "Text", joined, "B");
            DataWire(assembly, "Component", value, "Component");
            DataWire(value, "Value", line, "Number");
            return joined;
        }

        public NodeDocument JoinText(string name, int x, NodeDocument left, string leftPin, NodeDocument assembly, string field, string prefix)
        {
            var sep = Literal(name + "Sep", "string", " · ", x);
            var label = Literal(name + "Label", "string", prefix, x + 220);
            var value = Field(name + "Field", "WeaponAssembly", field, "string", x + 440);
            var labeled = Add(name + "Labeled", x + 660, "string");
            var withSep = Add(name + "Sepd", x + 880, "string");
            var joined = Add(name + "Joined", x + 1100, "string");
            DataWire(label, "Value", labeled, "A");
            DataWire(value, "Value", labeled, "B");
            DataWire(left, leftPin, withSep, "A");
            DataWire(sep, "Value", withSep, "B");
            DataWire(withSep, "Result", joined, "A");
            DataWire(labeled, "Result", joined, "B");
            DataWire(assembly, "Component", value, "Component");
            return joined;
        }

        public NodeDocument Tune(string name, string action, string field, string value, string message, int row, NodeDocument actionNode, NodeDocument owner, string valueType = "float32", string seconds = "2", string working = "Working.")
        {
            UseRow(row);
            var isAction = Equal("Is" + name, action, 680);
            var gate = Branch(name + "?", 900);
            var held = Call("Entity.GetHeldItem", name + "Gun", 1120, ("Holder", "EntityUid", null), ("Container", "string", "gun"), ("Item", "int64", null));
            var has = NotZero(name + "HasGun", 1340);
            var gunGate = Branch(name + "Gun?", 1560);
            var noGun = Call("Bui.Set", name + "NoGun", 1780, ("Owner", "EntityUid", null), ("Name", "string", "Reason"), ("Value", "string", "Put the pistol in the gun slot"), ("Success", "bool", null));
            var assembly = Component(name + "Assembly", "WeaponAssembly", 2000, "int64");
            var asmGate = Branch(name + "Assembly?", 2220);
            var wait = Call("Bui.Set", name + "Wait", 2440, ("Owner", "EntityUid", null), ("Name", "string", "Reason"), ("Value", "string", working), ("Success", "bool", null));
            var after = DoAfter(name + "After", 2660, seconds);
            var set = Set(name + "Set", "WeaponAssembly", field, 2880, value, valueType);
            var done = Call("Bui.Set", name + "Done", 3100, ("Owner", "EntityUid", null), ("Name", "string", "Reason"), ("Value", "string", message), ("Success", "bool", null));
            var refresh = Refresh(name, 3320);
            Exec(gate, "True", gunGate);
            Exec(gunGate, "False", noGun);
            Exec(gunGate, "True", asmGate);
            Exec(asmGate, "True", wait);
            Exec(wait, after);
            Exec(after, set);
            Exec(set, done);
            Exec(done, refresh);
            DataWire(actionNode, "Value", isAction, "A");
            DataWire(isAction, "Result", gate, "Condition");
            DataWire(owner, "Entity", held, "Holder");
            DataWire(owner, "Entity", noGun, "Owner");
            DataWire(owner, "Entity", wait, "Owner");
            DataWire(owner, "Entity", done, "Owner");
            DataWire(held, "Item", has, "A");
            DataWire(has, "Result", gunGate, "Condition");
            DataWire(held, "Item", assembly, "Entity");
            DataWire(assembly, "Found", asmGate, "Condition");
            DataWire(assembly, "Component", set, "Target");
            WireRefresh(refresh, owner);
            return gate;
        }

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
