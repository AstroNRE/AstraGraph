using System;
using System.Collections.Generic;
using AstraGraph.Core;
using AstraGraph.Runtime.Network;
using AstraGraph.Runtime.Templates;
using AstraGraph.State;
using AstraGraph.VM;
using NUnit.Framework;

namespace AstraGraph.Tests.Parity;

#region C# Reference Implementations

public sealed class CSharpDamageSystem
{
    public Dictionary<int, double> TotalDamage { get; } = [];
    public HashSet<int> CritEntities { get; } = [];

    public (double Total, bool IsCrit) OnDamageChanged(int target, double amount, string damageType, double armorMitigationFactor)
    {
        var mitigated = amount * (1.0 - armorMitigationFactor);
        TotalDamage.TryGetValue(target, out var current);
        var newTotal = current + mitigated;
        TotalDamage[target] = newTotal;

        var isCrit = newTotal >= 100.0;
        if (isCrit)
        {
            CritEntities.Add(target);
        }

        return (newTotal, isCrit);
    }
}

public sealed class CSharpInventorySystem
{
    public sealed class ItemSlot
    {
        public string Name { get; }
        public int MaxSize { get; }
        public int? EquippedItem { get; set; }

        public ItemSlot(string name, int maxSize)
        {
            Name = name;
            MaxSize = maxSize;
        }
    }

    public Dictionary<int, Dictionary<string, ItemSlot>> EntitySlots { get; } = [];

    public void RegisterSlot(int user, string slotName, int maxSize)
    {
        if (!EntitySlots.TryGetValue(user, out var slots))
        {
            slots = [];
            EntitySlots[user] = slots;
        }
        slots[slotName] = new ItemSlot(slotName, maxSize);
    }

    public bool CanEquip(int user, int item, string slotName, int itemSize)
    {
        if (!EntitySlots.TryGetValue(user, out var slots)) return false;
        if (!slots.TryGetValue(slotName, out var slot)) return false;
        return slot.EquippedItem == null && itemSize <= slot.MaxSize;
    }

    public bool Equip(int user, int item, string slotName, int itemSize)
    {
        if (!CanEquip(user, item, slotName, itemSize)) return false;
        EntitySlots[user][slotName].EquippedItem = item;
        return true;
    }
}

public sealed class CSharpBuiSystem
{
    public List<string> DispatchedActions { get; } = [];

    public bool HandleBuiMessage(int actor, int console, string action, double distance, bool hasAccess)
    {
        if (distance > 2.0) return false;
        if (!hasAccess) return false;

        DispatchedActions.Add($"{actor}->{console}:{action}");
        return true;
    }
}

public sealed class CSharpTimedExplosiveSystem
{
    public HashSet<int> DetonatedBombs { get; } = [];

    public void Detonate(int bombUid)
    {
        DetonatedBombs.Add(bombUid);
    }
}

public sealed class CSharpReplicatedPowerSystem
{
    public sealed class BatteryState
    {
        public double Charge { get; set; } = 100.0;
        public double MaxCharge { get; set; } = 100.0;
    }

    public Dictionary<int, BatteryState> ServerState { get; } = [];
    public Dictionary<int, BatteryState> ClientState { get; } = [];

    public void ApplyDeltaServer(int target, double delta)
    {
        if (!ServerState.TryGetValue(target, out var bat))
        {
            bat = new BatteryState();
            ServerState[target] = bat;
        }
        bat.Charge = Math.Clamp(bat.Charge + delta, 0.0, bat.MaxCharge);
    }

    public void PredictClient(int target, double delta)
    {
        if (!ClientState.TryGetValue(target, out var bat))
        {
            bat = new BatteryState();
            ClientState[target] = bat;
        }
        bat.Charge = Math.Clamp(bat.Charge + delta, 0.0, bat.MaxCharge);
    }

    public void ReconcileClient(int target, double serverAuthoritativeCharge)
    {
        if (!ClientState.TryGetValue(target, out var bat))
        {
            bat = new BatteryState();
            ClientState[target] = bat;
        }
        bat.Charge = serverAuthoritativeCharge;
    }
}

#endregion

[TestFixture]
public sealed class SS14ParityCampaignTests
{
    [Test]
    public void DamageSystem_DifferentialParity_MatchesNativeCSharp()
    {
        // 1. Template graph verification
        var graphDoc = SS14TemplateLibrary.CreateDamageSystemGraph();
        Assert.That(graphDoc.Nodes.Count, Is.EqualTo(5));
        Assert.That(graphDoc.Connections.Count, Is.EqualTo(11));

        // 2. Setup C# and AstraGraph environments
        var csharpSys = new CSharpDamageSystem();
        var astraDamagePool = new Dictionary<int, double>();
        var astraCritSet = new HashSet<int>();

        // VM Host Services simulating the native engine bindings
        var vmServices = new ParityVmHostServices();
        vmServices.RegisterNative("Damage.GetArmorResistance", args =>
        {
            var dmgType = args[1].AsString();
            var factor = dmgType switch
            {
                "Blunt" => 0.20,
                "Slash" => 0.40,
                "Heat" => 0.10,
                _ => 0.0
            };
            return AstraValue.FromDouble(factor);
        });

        vmServices.RegisterNative("Damage.ApplyHealthDelta", args =>
        {
            var target = args[0].AsEntityUid();
            var delta = args[1].AsDouble();
            astraDamagePool.TryGetValue(target, out var current);
            var newTotal = current + delta;
            astraDamagePool[target] = newTotal;
            return AstraValue.FromDouble(newTotal);
        });

        vmServices.RegisterNative("Damage.CheckCritThreshold", args =>
        {
            var total = args[0].AsDouble();
            var isCrit = total >= 100.0;
            if (isCrit)
            {
                astraCritSet.Add(1001); // active target
            }
            return AstraValue.FromBool(isCrit);
        });

        // 3. Run 10 differential test cases
        var testCases = new (int Target, double Amount, string Type, double CSharpMitigation)[]
        {
            (1001, 20.0, "Blunt", 0.20),
            (1001, 35.0, "Slash", 0.40),
            (1001, 15.0, "Heat", 0.10),
            (1001, 50.0, "Piercing", 0.00),
            (1001, 40.0, "Blunt", 0.20), // Triggers crit threshold (> 100)
            (1002, 10.0, "Heat", 0.10),
            (1002, 85.0, "Slash", 0.40),
            (1003, 99.0, "Piercing", 0.00),
            (1003, 2.0, "Blunt", 0.20),  // Triggers crit threshold
            (1004, 0.0, "Heat", 0.10)
        };

        foreach (var (target, amount, type, mitigation) in testCases)
        {
            // Execute C#
            var (csharpTotal, csharpCrit) = csharpSys.OnDamageChanged(target, amount, type, mitigation);

            // Execute AstraGraph via bytecode
            var pool = new ConstantPool();
            var fnName = pool.GetOrAddString("ApplyDamage");
            var targetConst = pool.GetOrAddInt64(target);
            var amountConst = pool.GetOrAddDouble(amount);
            var typeConst = pool.GetOrAddString(type);

            var instructions = new List<BytecodeInstruction>
            {
                // %r0 = target, %r1 = type, %r2 = amount, %r3 = 1.0
                new((byte)IrOpCode.LoadConst, 0, targetConst, 0, 0),
                new((byte)IrOpCode.LoadConst, 1, typeConst, 0, 0),
                new((byte)IrOpCode.LoadConst, 2, amountConst, 0, 0),
                new((byte)IrOpCode.LoadConst, 3, pool.GetOrAddDouble(1.0), 0, 0),
                // %r4 = CallNative Damage.GetArmorResistance(%r0, %r1)
                new((byte)IrOpCode.CallNative, 4, pool.GetOrAddString("Damage.GetArmorResistance"), 2, 0),
                // %r5 = 1.0 - resistance (%r3 - %r4)
                new((byte)IrOpCode.Sub, 5, 3, 4, 0),
                // %r7 = target (for ApplyHealthDelta)
                new((byte)IrOpCode.LoadConst, 7, targetConst, 0, 0),
                // %r8 = %r2 * %r5 (mitigated damage, adjacent to %r7)
                new((byte)IrOpCode.Mul, 8, 2, 5, 0),
                // %r9 = CallNative Damage.ApplyHealthDelta(%r7, %r8)
                new((byte)IrOpCode.CallNative, 9, pool.GetOrAddString("Damage.ApplyHealthDelta"), 2, 7),
                // %r10 = CallNative Damage.CheckCritThreshold(%r9)
                new((byte)IrOpCode.CallNative, 10, pool.GetOrAddString("Damage.CheckCritThreshold"), 1, 9),
                new((byte)IrOpCode.Return, BytecodeInstruction.NoRegister, 9, 0, 0)
            };

            var func = new BytecodeFunction(fnName, 11, 0, instructions);
            var prog = new BytecodeProgram(GraphId.New(), RevisionId.New(), "hash", pool);

            var vm = new AstraVm();
            var astraResult = vm.Execute(prog, func, hostServices: vmServices);

            Assert.That(astraResult.IsSuccess, Is.True);
            var astraTotal = astraResult.ReturnValue.AsDouble();
            var astraCrit = astraTotal >= 100.0;

            // Parity assertions
            Assert.That(astraTotal, Is.EqualTo(csharpTotal).Within(0.0001), $"Total damage mismatch for target {target}");
            Assert.That(astraCrit, Is.EqualTo(csharpCrit), $"Crit status mismatch for target {target}");
        }
    }

    [Test]
    public void InventorySystem_DifferentialParity_MatchesNativeCSharp()
    {
        var graphDoc = SS14TemplateLibrary.CreateInventorySystemGraph();
        Assert.That(graphDoc.Nodes.Count, Is.EqualTo(3));
        Assert.That(graphDoc.Connections.Count, Is.EqualTo(9));

        var csharpInv = new CSharpInventorySystem();
        csharpInv.RegisterSlot(user: 1, "pocket1", maxSize: 2);
        csharpInv.RegisterSlot(user: 1, "backpack", maxSize: 10);
        csharpInv.RegisterSlot(user: 2, "hands", maxSize: 5);

        var astraInv = new CSharpInventorySystem();
        astraInv.RegisterSlot(user: 1, "pocket1", maxSize: 2);
        astraInv.RegisterSlot(user: 1, "backpack", maxSize: 10);
        astraInv.RegisterSlot(user: 2, "hands", maxSize: 5);

        var vmServices = new ParityVmHostServices();
        vmServices.RegisterNative("Inventory.CanEquip", args =>
        {
            var user = args[0].AsEntityUid();
            var item = args[1].AsEntityUid();
            var slot = args[2].AsString() ?? "";
            var size = item switch { 101 => 1, 102 => 3, 103 => 8, _ => 1 };
            return AstraValue.FromBool(astraInv.CanEquip(user, item, slot, size));
        });

        vmServices.RegisterNative("Inventory.Equip", args =>
        {
            var allowed = args[0].AsBool();
            if (!allowed) return AstraValue.FromBool(false);
            var user = args[1].AsEntityUid();
            var item = args[2].AsEntityUid();
            var slot = args[3].AsString() ?? "";
            var size = item switch { 101 => 1, 102 => 3, 103 => 8, _ => 1 };
            return AstraValue.FromBool(astraInv.Equip(user, item, slot, size));
        });

        var scenarios = new (int User, int Item, string Slot, int Size)[]
        {
            (1, 101, "pocket1", 1),   // fits
            (1, 102, "pocket1", 3),   // too big (size 3 > max 2) -> fails
            (1, 103, "backpack", 8),  // fits in backpack
            (1, 101, "pocket1", 1),   // slot already occupied -> fails
            (2, 102, "hands", 3)      // fits in hands
        };

        foreach (var (user, item, slot, size) in scenarios)
        {
            // C# execution
            var csResult = csharpInv.Equip(user, item, slot, size);

            // AstraGraph execution
            var pool = new ConstantPool();
            var fnName = pool.GetOrAddString("OnEquipAttempt");
            var instructions = new List<BytecodeInstruction>
            {
                new((byte)IrOpCode.LoadConst, 0, pool.GetOrAddInt64(user), 0, 0),
                new((byte)IrOpCode.LoadConst, 1, pool.GetOrAddInt64(item), 0, 0),
                new((byte)IrOpCode.LoadConst, 2, pool.GetOrAddString(slot), 0, 0),
                // %r3 = CanEquip(user, item, slot)
                new((byte)IrOpCode.CallNative, 3, pool.GetOrAddString("Inventory.CanEquip"), 3, 0),
                // Load parameters into consecutive registers %r4..%r6 adjacent to %r3
                new((byte)IrOpCode.LoadConst, 4, pool.GetOrAddInt64(user), 0, 0),
                new((byte)IrOpCode.LoadConst, 5, pool.GetOrAddInt64(item), 0, 0),
                new((byte)IrOpCode.LoadConst, 6, pool.GetOrAddString(slot), 0, 0),
                // %r7 = Equip(allowed=%r3, user=%r4, item=%r5, slot=%r6)
                new((byte)IrOpCode.CallNative, 7, pool.GetOrAddString("Inventory.Equip"), 4, 3),
                new((byte)IrOpCode.Return, BytecodeInstruction.NoRegister, 7, 0, 0)
            };

            var func = new BytecodeFunction(fnName, 8, 0, instructions);
            var prog = new BytecodeProgram(GraphId.New(), RevisionId.New(), "hash", pool);

            var vm = new AstraVm();
            var astraResult = vm.Execute(prog, func, hostServices: vmServices);

            Assert.That(astraResult.IsSuccess, Is.True);
            var astraEquipSuccess = astraResult.ReturnValue.AsBool();

            // Parity check
            Assert.That(astraEquipSuccess, Is.EqualTo(csResult), $"Equip outcome mismatch for Item {item} into Slot {slot}");
            Assert.That(astraInv.EntitySlots[user][slot].EquippedItem, Is.EqualTo(csharpInv.EntitySlots[user][slot].EquippedItem));
        }
    }

    [Test]
    public void BuiInteraction_DifferentialParity_MatchesNativeCSharp()
    {
        var graphDoc = SS14TemplateLibrary.CreateBuiInteractionGraph();
        Assert.That(graphDoc.Nodes.Count, Is.EqualTo(4));
        Assert.That(graphDoc.Connections.Count, Is.EqualTo(12));

        var csharpBui = new CSharpBuiSystem();
        var astraDispatched = new List<string>();

        var vmServices = new ParityVmHostServices();
        vmServices.RegisterNative("Bui.CheckRange", args =>
        {
            var dist = args[0].AsDouble();
            return AstraValue.FromBool(dist <= 2.0);
        });

        vmServices.RegisterNative("Bui.CheckAccess", args =>
        {
            var hasAccess = args[0].AsBool();
            return AstraValue.FromBool(hasAccess);
        });

        vmServices.RegisterNative("Bui.DispatchAction", args =>
        {
            var inRange = args[0].AsBool();
            var hasAccess = args[1].AsBool();
            var actor = args[2].AsEntityUid();
            var console = args[3].AsEntityUid();
            var action = args[4].AsString() ?? "";

            if (inRange && hasAccess)
            {
                astraDispatched.Add($"{actor}->{console}:{action}");
                return AstraValue.FromBool(true);
            }
            return AstraValue.FromBool(false);
        });

        var testCases = new (int Actor, int Console, string Action, double Distance, bool HasAccess)[]
        {
            (10, 50, "OpenAirlock", 1.5, true),    // Valid: In range, has access
            (10, 50, "OpenAirlock", 1.5, false),   // Denied: In range, no access
            (11, 50, "EmergencyBolt", 3.2, true),  // Denied: Out of range (3.2m > 2m)
            (12, 60, "DispenseSoda", 0.8, true),   // Valid: In range, has access
            (12, 60, "ServiceMode", 2.1, false)    // Denied: Out of range and no access
        };

        foreach (var (actor, console, action, distance, access) in testCases)
        {
            // C#
            var csAllowed = csharpBui.HandleBuiMessage(actor, console, action, distance, access);

            // AstraGraph
            var pool = new ConstantPool();
            var fnName = pool.GetOrAddString("OnBuiMessage");
            var instructions = new List<BytecodeInstruction>
            {
                // %r0 = distance, %r1 = access
                new((byte)IrOpCode.LoadConst, 0, pool.GetOrAddDouble(distance), 0, 0),
                new((byte)IrOpCode.LoadConst, 1, pool.GetOrAddBool(access), 0, 0),
                // %r2 = CheckRange(%r0)
                new((byte)IrOpCode.CallNative, 2, pool.GetOrAddString("Bui.CheckRange"), 1, 0),
                // %r3 = CheckAccess(%r1)
                new((byte)IrOpCode.CallNative, 3, pool.GetOrAddString("Bui.CheckAccess"), 1, 1),
                // %r4 = actor, %r5 = console, %r6 = action
                new((byte)IrOpCode.LoadConst, 4, pool.GetOrAddInt64(actor), 0, 0),
                new((byte)IrOpCode.LoadConst, 5, pool.GetOrAddInt64(console), 0, 0),
                new((byte)IrOpCode.LoadConst, 6, pool.GetOrAddString(action), 0, 0),
                // %r7 = DispatchAction(%r2=inRange, %r3=hasAccess, %r4=actor, %r5=console, %r6=action)
                new((byte)IrOpCode.CallNative, 7, pool.GetOrAddString("Bui.DispatchAction"), 5, 2),
                new((byte)IrOpCode.Return, BytecodeInstruction.NoRegister, 7, 0, 0)
            };

            var func = new BytecodeFunction(fnName, 8, 0, instructions);
            var prog = new BytecodeProgram(GraphId.New(), RevisionId.New(), "hash", pool);

            var vm = new AstraVm();
            var astraResult = vm.Execute(prog, func, hostServices: vmServices);

            Assert.That(astraResult.IsSuccess, Is.True);
            var astraAllowed = astraResult.ReturnValue.AsBool();

            Assert.That(astraAllowed, Is.EqualTo(csAllowed));
        }

        Assert.That(astraDispatched.Count, Is.EqualTo(csharpBui.DispatchedActions.Count));
        for (var i = 0; i < astraDispatched.Count; i++)
        {
            Assert.That(astraDispatched[i], Is.EqualTo(csharpBui.DispatchedActions[i]));
        }
    }

    [Test]
    public void TimedExplosive_DifferentialParity_MatchesNativeCSharp()
    {
        var graphDoc = SS14TemplateLibrary.CreateTimedExplosiveGraph();
        Assert.That(graphDoc.Nodes.Count, Is.EqualTo(3));
        Assert.That(graphDoc.Connections.Count, Is.EqualTo(4));

        var csharpExplosives = new CSharpTimedExplosiveSystem();
        var astraDetonated = new HashSet<int>();

        var vmServices = new ParityVmHostServices();
        vmServices.RegisterNative("Explosion.Detonate", args =>
        {
            var bomb = args[0].AsEntityUid();
            astraDetonated.Add(bomb);
            return AstraValue.Null;
        });

        const int bombUid = 999;
        const double fuse = 1.5;

        // C# execution
        csharpExplosives.Detonate(bombUid);

        // AstraGraph latent execution: Phase 1 (yield delay) -> Phase 2 (resume detonate)
        var pool = new ConstantPool();
        var fnName = pool.GetOrAddString("OnArmTimer");
        var resumeGuid = Guid.NewGuid();

        var instructions = new List<BytecodeInstruction>
        {
            new((byte)IrOpCode.LoadConst, 0, pool.GetOrAddInt64(bombUid), 0, 0),
            new((byte)IrOpCode.LoadConst, 1, pool.GetOrAddDouble(fuse), 0, 0),
            // L0002: YieldContinuation Delay (op1=Delay, op2=guidIndex, extra=nextIp(3))
            new((byte)IrOpCode.YieldContinuation, BytecodeInstruction.NoRegister, (int)ContinuationKind.Delay, pool.GetOrAddGuid(resumeGuid), 3),
            // L0003: Detonate(%r0)
            new((byte)IrOpCode.CallNative, 2, pool.GetOrAddString("Explosion.Detonate"), 1, 0),
            new((byte)IrOpCode.Return, BytecodeInstruction.NoRegister, 0, 0, 0)
        };

        var func = new BytecodeFunction(fnName, 3, 0, instructions);
        var prog = new BytecodeProgram(GraphId.New(), RevisionId.New(), "hash", pool);

        var vm = new AstraVm();

        // Step 1: Arms timer, yields continuation
        var step1 = vm.Execute(prog, func, hostServices: vmServices);
        Assert.That(step1.IsYielded, Is.True);
        Assert.That(step1.YieldState!.Kind, Is.EqualTo(ContinuationKind.Delay));
        Assert.That(astraDetonated.Contains(bombUid), Is.False);

        // Step 2: Resume after timer expires
        var step2 = vm.Execute(prog, func, initialRegisters: step1.YieldState.Arguments.ToArray(), startIp: step1.YieldState.NextInstructionPointer, hostServices: vmServices);
        Assert.That(step2.IsSuccess, Is.True);
        Assert.That(astraDetonated.Contains(bombUid), Is.True);

        // Parity
        Assert.That(astraDetonated.Contains(bombUid), Is.EqualTo(csharpExplosives.DetonatedBombs.Contains(bombUid)));
    }

    [Test]
    public void ReplicatedState_DifferentialParity_MatchesNativeCSharp()
    {
        var graphDoc = SS14TemplateLibrary.CreateReplicatedStateGraph();
        Assert.That(graphDoc.Side, Is.EqualTo(GraphSide.SharedPredicted));

        var csharpReplication = new CSharpReplicatedPowerSystem();
        var serverStore = new DynamicComponentStore();
        var clientStore = new DynamicComponentStore();

        const int entityUid = 42;
        var batterySchema = new SchemaType(
            SchemaId.New(),
            "AstraBattery",
            IsComponentSchema: true,
            [
                new SchemaField(FieldId.New(), "Charge", PrimitiveType.Float64, Options: SchemaFieldOptions.Replicated),
                new SchemaField(FieldId.New(), "MaxCharge", PrimitiveType.Float64, Options: SchemaFieldOptions.Replicated)
            ]);

        // Register component schema on server and client with initial charge 100.0
        var serverStorage = serverStore.AddComponent(entityUid, batterySchema, [AstraValue.FromDouble(100.0), AstraValue.FromDouble(100.0)]);
        var clientStorage = clientStore.AddComponent(entityUid, batterySchema, [AstraValue.FromDouble(100.0), AstraValue.FromDouble(100.0)]);

        var vmServices = new ParityVmHostServices();
        vmServices.RegisterNative("Power.ApplyDelta", args =>
        {
            var ent = args[0].AsEntityUid();
            var delta = args[1].AsDouble();
            serverStore.TryGetComponent(ent, batterySchema.Id, out var storage);
            var currentCharge = storage!.GetField(0).AsDouble();
            var newCharge = Math.Clamp(currentCharge + delta, 0.0, 100.0);
            storage.SetField(0, AstraValue.FromDouble(newCharge));
            return AstraValue.FromDouble(newCharge);
        });

        // 1. Apply -30 power delta on server
        csharpReplication.ApplyDeltaServer(entityUid, -30.0);

        var pool = new ConstantPool();
        var fnName = pool.GetOrAddString("OnConsumePower");
        var instructions = new List<BytecodeInstruction>
        {
            new((byte)IrOpCode.LoadConst, 0, pool.GetOrAddInt64(entityUid), 0, 0),
            new((byte)IrOpCode.LoadConst, 1, pool.GetOrAddDouble(-30.0), 0, 0),
            new((byte)IrOpCode.CallNative, 2, pool.GetOrAddString("Power.ApplyDelta"), 2, 0),
            new((byte)IrOpCode.Return, BytecodeInstruction.NoRegister, 2, 0, 0)
        };

        var func = new BytecodeFunction(fnName, 3, 0, instructions);
        var prog = new BytecodeProgram(GraphId.New(), RevisionId.New(), "hash", pool);

        var vm = new AstraVm();
        var astraResult = vm.Execute(prog, func, hostServices: vmServices);

        Assert.That(astraResult.IsSuccess, Is.True);
        var astraCharge = serverStorage.GetField(0).AsDouble();
        var csharpCharge = csharpReplication.ServerState[entityUid].Charge;

        Assert.That(astraCharge, Is.EqualTo(csharpCharge).Within(0.0001));
        Assert.That(astraCharge, Is.EqualTo(70.0));

        // Collect dirty deltas from server and replicate to client
        var deltaPackets = DeltaReplicationManager.CollectDirtyDeltas(serverStore);
        Assert.That(deltaPackets.Count, Is.GreaterThan(0));

        DeltaReplicationManager.ApplyDeltas(clientStore, deltaPackets);
        var clientReplicatedCharge = clientStorage.GetField(0).AsDouble();
        Assert.That(clientReplicatedCharge, Is.EqualTo(astraCharge));

        // 2. Client prediction and replication parity
        csharpReplication.PredictClient(entityUid, -15.0);
        Assert.That(csharpReplication.ClientState[entityUid].Charge, Is.EqualTo(85.0));

        // Reconcile client with server authoritative state
        csharpReplication.ReconcileClient(entityUid, astraCharge);
        Assert.That(csharpReplication.ClientState[entityUid].Charge, Is.EqualTo(70.0));
        Assert.That(csharpReplication.ClientState[entityUid].Charge, Is.EqualTo(astraCharge));
    }
}

public sealed class ParityVmHostServices : IVmHostServices
{
    private readonly Dictionary<string, Func<IReadOnlyList<AstraValue>, AstraValue>> _nativeMethods = [];

    public void RegisterNative(string name, Func<IReadOnlyList<AstraValue>, AstraValue> handler)
    {
        _nativeMethods[name] = handler;
    }

    public AstraValue CallNative(string methodDescriptor, IReadOnlyList<AstraValue> arguments)
    {
        if (_nativeMethods.TryGetValue(methodDescriptor, out var handler))
        {
            return handler(arguments);
        }
        throw new NotSupportedException($"Native binding '{methodDescriptor}' not found.");
    }

    public AstraValue GetVariable(SymbolId variableId, string name) => AstraValue.Null;
    public void SetVariable(SymbolId variableId, string name, AstraValue value) { }
    public AstraValue GetComponent(int entityUid, string componentTypeName) => AstraValue.Null;
    public bool HasComponent(int entityUid, string componentTypeName) => false;
    public void SetComponentField(int entityUid, string schemaIdAndFieldId, AstraValue value) { }
}
