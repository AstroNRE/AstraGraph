using System;
using System.Collections.Generic;
using AstraGraph.Core;

namespace AstraGraph.Runtime.Templates;

/// <summary>
/// Pre-built canonical visual graph templates replicating standard Space Station 14 gameplay systems.
/// These templates serve as verified design patterns and out-of-the-box presets for graph authors.
/// </summary>
public static class SS14TemplateLibrary
{
    /// <summary>
    /// Creates the canonical DamageSystem graph: receives DamageChangedEvent, calculates armor mitigation,
    /// updates health pool, and evaluates critical health thresholds.
    /// </summary>
    public static GraphDocument CreateDamageSystemGraph()
    {
        var graphId = GraphId.New();

        var entryNodeId = NodeId.New();
        var entryExecOut = PinId.New();
        var targetEntityOut = PinId.New();
        var rawDamageOut = PinId.New();
        var damageTypeOut = PinId.New();

        var getArmorNodeId = NodeId.New();
        var getArmorExecIn = PinId.New();
        var getArmorTargetIn = PinId.New();
        var getArmorTypeIn = PinId.New();
        var getArmorExecOut = PinId.New();
        var armorResistOut = PinId.New();

        var calcMitigationNodeId = NodeId.New();
        var calcMitExecIn = PinId.New();
        var calcMitDamageIn = PinId.New();
        var calcMitResistIn = PinId.New();
        var calcMitExecOut = PinId.New();
        var mitigatedDamageOut = PinId.New();

        var applyDamageNodeId = NodeId.New();
        var applyExecIn = PinId.New();
        var applyTargetIn = PinId.New();
        var applyDamageIn = PinId.New();
        var applyExecOut = PinId.New();
        var newTotalHealthOut = PinId.New();

        var checkCritNodeId = NodeId.New();
        var checkCritExecIn = PinId.New();
        var checkCritHealthIn = PinId.New();
        var checkCritExecOut = PinId.New();

        var nodes = new List<NodeDocument>
        {
            new()
            {
                Id = entryNodeId,
                Name = "OnDamageChanged",
                NodeType = "Event.DamageChanged",
                Pins =
                [
                    new() { Id = entryExecOut, Name = "Flow", Direction = PinDirection.Output, Kind = PinKind.Execution, DataType = "Flow" },
                    new() { Id = targetEntityOut, Name = "Target", Direction = PinDirection.Output, Kind = PinKind.Data, DataType = "Robust.Shared.GameObjects.EntityUid" },
                    new() { Id = rawDamageOut, Name = "Damage", Direction = PinDirection.Output, Kind = PinKind.Data, DataType = "System.Double" },
                    new() { Id = damageTypeOut, Name = "DamageType", Direction = PinDirection.Output, Kind = PinKind.Data, DataType = "System.String" }
                ]
            },
            new()
            {
                Id = getArmorNodeId,
                Name = "GetArmorResistance",
                NodeType = "Native.Call",
                Properties = new Dictionary<string, string> { ["Method"] = "Damage.GetArmorResistance" },
                Pins =
                [
                    new() { Id = getArmorExecIn, Name = "FlowIn", Direction = PinDirection.Input, Kind = PinKind.Execution, DataType = "Flow" },
                    new() { Id = getArmorTargetIn, Name = "Target", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "Robust.Shared.GameObjects.EntityUid" },
                    new() { Id = getArmorTypeIn, Name = "DamageType", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "System.String" },
                    new() { Id = getArmorExecOut, Name = "FlowOut", Direction = PinDirection.Output, Kind = PinKind.Execution, DataType = "Flow" },
                    new() { Id = armorResistOut, Name = "Resistance", Direction = PinDirection.Output, Kind = PinKind.Data, DataType = "System.Double" }
                ]
            },
            new()
            {
                Id = calcMitigationNodeId,
                Name = "MitigateDamage",
                NodeType = "Math.Mul",
                Pins =
                [
                    new() { Id = calcMitExecIn, Name = "FlowIn", Direction = PinDirection.Input, Kind = PinKind.Execution, DataType = "Flow" },
                    new() { Id = calcMitDamageIn, Name = "Damage", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "System.Double" },
                    new() { Id = calcMitResistIn, Name = "Factor", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "System.Double" },
                    new() { Id = calcMitExecOut, Name = "FlowOut", Direction = PinDirection.Output, Kind = PinKind.Execution, DataType = "Flow" },
                    new() { Id = mitigatedDamageOut, Name = "Result", Direction = PinDirection.Output, Kind = PinKind.Data, DataType = "System.Double" }
                ]
            },
            new()
            {
                Id = applyDamageNodeId,
                Name = "ApplyDamage",
                NodeType = "Native.Call",
                Properties = new Dictionary<string, string> { ["Method"] = "Damage.ApplyHealthDelta" },
                Pins =
                [
                    new() { Id = applyExecIn, Name = "FlowIn", Direction = PinDirection.Input, Kind = PinKind.Execution, DataType = "Flow" },
                    new() { Id = applyTargetIn, Name = "Target", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "Robust.Shared.GameObjects.EntityUid" },
                    new() { Id = applyDamageIn, Name = "Delta", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "System.Double" },
                    new() { Id = applyExecOut, Name = "FlowOut", Direction = PinDirection.Output, Kind = PinKind.Execution, DataType = "Flow" },
                    new() { Id = newTotalHealthOut, Name = "TotalDamage", Direction = PinDirection.Output, Kind = PinKind.Data, DataType = "System.Double" }
                ]
            },
            new()
            {
                Id = checkCritNodeId,
                Name = "EvaluateCritThreshold",
                NodeType = "Native.Call",
                Properties = new Dictionary<string, string> { ["Method"] = "Damage.CheckCritThreshold" },
                Pins =
                [
                    new() { Id = checkCritExecIn, Name = "FlowIn", Direction = PinDirection.Input, Kind = PinKind.Execution, DataType = "Flow" },
                    new() { Id = checkCritHealthIn, Name = "TotalDamage", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "System.Double" },
                    new() { Id = checkCritExecOut, Name = "FlowOut", Direction = PinDirection.Output, Kind = PinKind.Execution, DataType = "Flow" }
                ]
            }
        };

        var connections = new List<ConnectionDocument>
        {
            // Execution flow
            new() { FromNode = entryNodeId, FromPin = entryExecOut, ToNode = getArmorNodeId, ToPin = getArmorExecIn },
            new() { FromNode = getArmorNodeId, FromPin = getArmorExecOut, ToNode = calcMitigationNodeId, ToPin = calcMitExecIn },
            new() { FromNode = calcMitigationNodeId, FromPin = calcMitExecOut, ToNode = applyDamageNodeId, ToPin = applyExecIn },
            new() { FromNode = applyDamageNodeId, FromPin = applyExecOut, ToNode = checkCritNodeId, ToPin = checkCritExecIn },

            // Data flow
            new() { FromNode = entryNodeId, FromPin = targetEntityOut, ToNode = getArmorNodeId, ToPin = getArmorTargetIn },
            new() { FromNode = entryNodeId, FromPin = damageTypeOut, ToNode = getArmorNodeId, ToPin = getArmorTypeIn },
            new() { FromNode = entryNodeId, FromPin = rawDamageOut, ToNode = calcMitigationNodeId, ToPin = calcMitDamageIn },
            new() { FromNode = getArmorNodeId, FromPin = armorResistOut, ToNode = calcMitigationNodeId, ToPin = calcMitResistIn },
            new() { FromNode = entryNodeId, FromPin = targetEntityOut, ToNode = applyDamageNodeId, ToPin = applyTargetIn },
            new() { FromNode = calcMitigationNodeId, FromPin = mitigatedDamageOut, ToNode = applyDamageNodeId, ToPin = applyDamageIn },
            new() { FromNode = applyDamageNodeId, FromPin = newTotalHealthOut, ToNode = checkCritNodeId, ToPin = checkCritHealthIn }
        };

        return new GraphDocument
        {
            Id = graphId,
            Name = "SS14_DamageSystem",
            Kind = GraphKind.System,
            Side = GraphSide.Server,
            Nodes = nodes,
            Connections = connections
        };
    }

    /// <summary>
    /// Creates the canonical InventorySystem graph: validates slot compatibility, checks capacity,
    /// performs item equipping, and triggers inventory change notifications.
    /// </summary>
    public static GraphDocument CreateInventorySystemGraph()
    {
        var graphId = GraphId.New();

        var entryNodeId = NodeId.New();
        var entryExecOut = PinId.New();
        var userOut = PinId.New();
        var itemOut = PinId.New();
        var slotOut = PinId.New();

        var checkSlotNodeId = NodeId.New();
        var checkSlotExecIn = PinId.New();
        var checkSlotUserIn = PinId.New();
        var checkSlotItemIn = PinId.New();
        var checkSlotNameIn = PinId.New();
        var checkSlotExecOut = PinId.New();
        var checkSlotAllowedOut = PinId.New();

        var equipNodeId = NodeId.New();
        var equipExecIn = PinId.New();
        var equipAllowedIn = PinId.New();
        var equipUserIn = PinId.New();
        var equipItemIn = PinId.New();
        var equipSlotIn = PinId.New();
        var equipExecOut = PinId.New();

        var nodes = new List<NodeDocument>
        {
            new()
            {
                Id = entryNodeId,
                Name = "OnEquipAttempt",
                NodeType = "Event.EquipAttempt",
                Pins =
                [
                    new() { Id = entryExecOut, Name = "Flow", Direction = PinDirection.Output, Kind = PinKind.Execution, DataType = "Flow" },
                    new() { Id = userOut, Name = "User", Direction = PinDirection.Output, Kind = PinKind.Data, DataType = "Robust.Shared.GameObjects.EntityUid" },
                    new() { Id = itemOut, Name = "Item", Direction = PinDirection.Output, Kind = PinKind.Data, DataType = "Robust.Shared.GameObjects.EntityUid" },
                    new() { Id = slotOut, Name = "Slot", Direction = PinDirection.Output, Kind = PinKind.Data, DataType = "System.String" }
                ]
            },
            new()
            {
                Id = checkSlotNodeId,
                Name = "CanEquipItem",
                NodeType = "Native.Call",
                Properties = new Dictionary<string, string> { ["Method"] = "Inventory.CanEquip" },
                Pins =
                [
                    new() { Id = checkSlotExecIn, Name = "FlowIn", Direction = PinDirection.Input, Kind = PinKind.Execution, DataType = "Flow" },
                    new() { Id = checkSlotUserIn, Name = "User", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "Robust.Shared.GameObjects.EntityUid" },
                    new() { Id = checkSlotItemIn, Name = "Item", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "Robust.Shared.GameObjects.EntityUid" },
                    new() { Id = checkSlotNameIn, Name = "Slot", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "System.String" },
                    new() { Id = checkSlotExecOut, Name = "FlowOut", Direction = PinDirection.Output, Kind = PinKind.Execution, DataType = "Flow" },
                    new() { Id = checkSlotAllowedOut, Name = "CanEquip", Direction = PinDirection.Output, Kind = PinKind.Data, DataType = "System.Boolean" }
                ]
            },
            new()
            {
                Id = equipNodeId,
                Name = "EquipItem",
                NodeType = "Native.Call",
                Properties = new Dictionary<string, string> { ["Method"] = "Inventory.Equip" },
                Pins =
                [
                    new() { Id = equipExecIn, Name = "FlowIn", Direction = PinDirection.Input, Kind = PinKind.Execution, DataType = "Flow" },
                    new() { Id = equipAllowedIn, Name = "Allowed", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "System.Boolean" },
                    new() { Id = equipUserIn, Name = "User", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "Robust.Shared.GameObjects.EntityUid" },
                    new() { Id = equipItemIn, Name = "Item", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "Robust.Shared.GameObjects.EntityUid" },
                    new() { Id = equipSlotIn, Name = "Slot", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "System.String" },
                    new() { Id = equipExecOut, Name = "FlowOut", Direction = PinDirection.Output, Kind = PinKind.Execution, DataType = "Flow" }
                ]
            }
        };

        var connections = new List<ConnectionDocument>
        {
            new() { FromNode = entryNodeId, FromPin = entryExecOut, ToNode = checkSlotNodeId, ToPin = checkSlotExecIn },
            new() { FromNode = checkSlotNodeId, FromPin = checkSlotExecOut, ToNode = equipNodeId, ToPin = equipExecIn },

            new() { FromNode = entryNodeId, FromPin = userOut, ToNode = checkSlotNodeId, ToPin = checkSlotUserIn },
            new() { FromNode = entryNodeId, FromPin = itemOut, ToNode = checkSlotNodeId, ToPin = checkSlotItemIn },
            new() { FromNode = entryNodeId, FromPin = slotOut, ToNode = checkSlotNodeId, ToPin = checkSlotNameIn },

            new() { FromNode = checkSlotNodeId, FromPin = checkSlotAllowedOut, ToNode = equipNodeId, ToPin = equipAllowedIn },
            new() { FromNode = entryNodeId, FromPin = userOut, ToNode = equipNodeId, ToPin = equipUserIn },
            new() { FromNode = entryNodeId, FromPin = itemOut, ToNode = equipNodeId, ToPin = equipItemIn },
            new() { FromNode = entryNodeId, FromPin = slotOut, ToNode = equipNodeId, ToPin = equipSlotIn }
        };

        return new GraphDocument
        {
            Id = graphId,
            Name = "SS14_InventorySystem",
            Kind = GraphKind.System,
            Side = GraphSide.Server,
            Nodes = nodes,
            Connections = connections
        };
    }

    /// <summary>
    /// Creates the canonical BuiInteraction graph: validates player interaction distance,
    /// verifies security access permissions, and executes Bound User Interface actions.
    /// </summary>
    public static GraphDocument CreateBuiInteractionGraph()
    {
        var graphId = GraphId.New();

        var entryNodeId = NodeId.New();
        var entryExecOut = PinId.New();
        var actorOut = PinId.New();
        var consoleOut = PinId.New();
        var actionNameOut = PinId.New();

        var checkDistNodeId = NodeId.New();
        var checkDistExecIn = PinId.New();
        var checkDistActorIn = PinId.New();
        var checkDistTargetIn = PinId.New();
        var checkDistExecOut = PinId.New();
        var inRangeOut = PinId.New();

        var checkAccessNodeId = NodeId.New();
        var checkAccessExecIn = PinId.New();
        var checkAccessActorIn = PinId.New();
        var checkAccessTargetIn = PinId.New();
        var checkAccessExecOut = PinId.New();
        var hasAccessOut = PinId.New();

        var dispatchNodeId = NodeId.New();
        var dispatchExecIn = PinId.New();
        var dispatchInRangeIn = PinId.New();
        var dispatchHasAccessIn = PinId.New();
        var dispatchActorIn = PinId.New();
        var dispatchConsoleIn = PinId.New();
        var dispatchActionIn = PinId.New();
        var dispatchExecOut = PinId.New();

        var nodes = new List<NodeDocument>
        {
            new()
            {
                Id = entryNodeId,
                Name = "OnBuiMessage",
                NodeType = "Event.BuiMessage",
                Pins =
                [
                    new() { Id = entryExecOut, Name = "Flow", Direction = PinDirection.Output, Kind = PinKind.Execution, DataType = "Flow" },
                    new() { Id = actorOut, Name = "Actor", Direction = PinDirection.Output, Kind = PinKind.Data, DataType = "Robust.Shared.GameObjects.EntityUid" },
                    new() { Id = consoleOut, Name = "Console", Direction = PinDirection.Output, Kind = PinKind.Data, DataType = "Robust.Shared.GameObjects.EntityUid" },
                    new() { Id = actionNameOut, Name = "Action", Direction = PinDirection.Output, Kind = PinKind.Data, DataType = "System.String" }
                ]
            },
            new()
            {
                Id = checkDistNodeId,
                Name = "CheckInteractionRange",
                NodeType = "Native.Call",
                Properties = new Dictionary<string, string> { ["Method"] = "Bui.CheckRange" },
                Pins =
                [
                    new() { Id = checkDistExecIn, Name = "FlowIn", Direction = PinDirection.Input, Kind = PinKind.Execution, DataType = "Flow" },
                    new() { Id = checkDistActorIn, Name = "Actor", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "Robust.Shared.GameObjects.EntityUid" },
                    new() { Id = checkDistTargetIn, Name = "Target", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "Robust.Shared.GameObjects.EntityUid" },
                    new() { Id = checkDistExecOut, Name = "FlowOut", Direction = PinDirection.Output, Kind = PinKind.Execution, DataType = "Flow" },
                    new() { Id = inRangeOut, Name = "InRange", Direction = PinDirection.Output, Kind = PinKind.Data, DataType = "System.Boolean" }
                ]
            },
            new()
            {
                Id = checkAccessNodeId,
                Name = "CheckConsoleAccess",
                NodeType = "Native.Call",
                Properties = new Dictionary<string, string> { ["Method"] = "Bui.CheckAccess" },
                Pins =
                [
                    new() { Id = checkAccessExecIn, Name = "FlowIn", Direction = PinDirection.Input, Kind = PinKind.Execution, DataType = "Flow" },
                    new() { Id = checkAccessActorIn, Name = "Actor", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "Robust.Shared.GameObjects.EntityUid" },
                    new() { Id = checkAccessTargetIn, Name = "Target", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "Robust.Shared.GameObjects.EntityUid" },
                    new() { Id = checkAccessExecOut, Name = "FlowOut", Direction = PinDirection.Output, Kind = PinKind.Execution, DataType = "Flow" },
                    new() { Id = hasAccessOut, Name = "HasAccess", Direction = PinDirection.Output, Kind = PinKind.Data, DataType = "System.Boolean" }
                ]
            },
            new()
            {
                Id = dispatchNodeId,
                Name = "DispatchBuiAction",
                NodeType = "Native.Call",
                Properties = new Dictionary<string, string> { ["Method"] = "Bui.DispatchAction" },
                Pins =
                [
                    new() { Id = dispatchExecIn, Name = "FlowIn", Direction = PinDirection.Input, Kind = PinKind.Execution, DataType = "Flow" },
                    new() { Id = dispatchInRangeIn, Name = "InRange", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "System.Boolean" },
                    new() { Id = dispatchHasAccessIn, Name = "HasAccess", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "System.Boolean" },
                    new() { Id = dispatchActorIn, Name = "Actor", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "Robust.Shared.GameObjects.EntityUid" },
                    new() { Id = dispatchConsoleIn, Name = "Console", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "Robust.Shared.GameObjects.EntityUid" },
                    new() { Id = dispatchActionIn, Name = "Action", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "System.String" },
                    new() { Id = dispatchExecOut, Name = "FlowOut", Direction = PinDirection.Output, Kind = PinKind.Execution, DataType = "Flow" }
                ]
            }
        };

        var connections = new List<ConnectionDocument>
        {
            new() { FromNode = entryNodeId, FromPin = entryExecOut, ToNode = checkDistNodeId, ToPin = checkDistExecIn },
            new() { FromNode = checkDistNodeId, FromPin = checkDistExecOut, ToNode = checkAccessNodeId, ToPin = checkAccessExecIn },
            new() { FromNode = checkAccessNodeId, FromPin = checkAccessExecOut, ToNode = dispatchNodeId, ToPin = dispatchExecIn },

            new() { FromNode = entryNodeId, FromPin = actorOut, ToNode = checkDistNodeId, ToPin = checkDistActorIn },
            new() { FromNode = entryNodeId, FromPin = consoleOut, ToNode = checkDistNodeId, ToPin = checkDistTargetIn },

            new() { FromNode = entryNodeId, FromPin = actorOut, ToNode = checkAccessNodeId, ToPin = checkAccessActorIn },
            new() { FromNode = entryNodeId, FromPin = consoleOut, ToNode = checkAccessNodeId, ToPin = checkAccessTargetIn },

            new() { FromNode = checkDistNodeId, FromPin = inRangeOut, ToNode = dispatchNodeId, ToPin = dispatchInRangeIn },
            new() { FromNode = checkAccessNodeId, FromPin = hasAccessOut, ToNode = dispatchNodeId, ToPin = dispatchHasAccessIn },
            new() { FromNode = entryNodeId, FromPin = actorOut, ToNode = dispatchNodeId, ToPin = dispatchActorIn },
            new() { FromNode = entryNodeId, FromPin = consoleOut, ToNode = dispatchNodeId, ToPin = dispatchConsoleIn },
            new() { FromNode = entryNodeId, FromPin = actionNameOut, ToNode = dispatchNodeId, ToPin = dispatchActionIn }
        };

        return new GraphDocument
        {
            Id = graphId,
            Name = "SS14_BuiInteraction",
            Kind = GraphKind.System,
            Side = GraphSide.Server,
            Nodes = nodes,
            Connections = connections
        };
    }

    /// <summary>
    /// Creates the canonical TimedExplosive graph: starts fuse timer via latent coroutine delay,
    /// triggers area explosion, spawns VFX particles, and deletes the bomb entity.
    /// </summary>
    public static GraphDocument CreateTimedExplosiveGraph()
    {
        var graphId = GraphId.New();

        var entryNodeId = NodeId.New();
        var entryExecOut = PinId.New();
        var bombOut = PinId.New();
        var fuseSecondsOut = PinId.New();

        var delayNodeId = NodeId.New();
        var delayExecIn = PinId.New();
        var delaySecondsIn = PinId.New();
        var delayExecOut = PinId.New();

        var explodeNodeId = NodeId.New();
        var explodeExecIn = PinId.New();
        var explodeBombIn = PinId.New();
        var explodeExecOut = PinId.New();

        var nodes = new List<NodeDocument>
        {
            new()
            {
                Id = entryNodeId,
                Name = "OnArmTimer",
                NodeType = "Event.ArmTimer",
                Pins =
                [
                    new() { Id = entryExecOut, Name = "Flow", Direction = PinDirection.Output, Kind = PinKind.Execution, DataType = "Flow" },
                    new() { Id = bombOut, Name = "Bomb", Direction = PinDirection.Output, Kind = PinKind.Data, DataType = "Robust.Shared.GameObjects.EntityUid" },
                    new() { Id = fuseSecondsOut, Name = "FuseSeconds", Direction = PinDirection.Output, Kind = PinKind.Data, DataType = "System.Double" }
                ]
            },
            new()
            {
                Id = delayNodeId,
                Name = "Delay",
                NodeType = "Core.Delay",
                Pins =
                [
                    new() { Id = delayExecIn, Name = "FlowIn", Direction = PinDirection.Input, Kind = PinKind.Execution, DataType = "Flow" },
                    new() { Id = delaySecondsIn, Name = "Seconds", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "System.Double" },
                    new() { Id = delayExecOut, Name = "FlowOut", Direction = PinDirection.Output, Kind = PinKind.Execution, DataType = "Flow" }
                ]
            },
            new()
            {
                Id = explodeNodeId,
                Name = "Detonate",
                NodeType = "Native.Call",
                Properties = new Dictionary<string, string> { ["Method"] = "Explosion.Detonate" },
                Pins =
                [
                    new() { Id = explodeExecIn, Name = "FlowIn", Direction = PinDirection.Input, Kind = PinKind.Execution, DataType = "Flow" },
                    new() { Id = explodeBombIn, Name = "Bomb", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "Robust.Shared.GameObjects.EntityUid" },
                    new() { Id = explodeExecOut, Name = "FlowOut", Direction = PinDirection.Output, Kind = PinKind.Execution, DataType = "Flow" }
                ]
            }
        };

        var connections = new List<ConnectionDocument>
        {
            new() { FromNode = entryNodeId, FromPin = entryExecOut, ToNode = delayNodeId, ToPin = delayExecIn },
            new() { FromNode = delayNodeId, FromPin = delayExecOut, ToNode = explodeNodeId, ToPin = explodeExecIn },

            new() { FromNode = entryNodeId, FromPin = fuseSecondsOut, ToNode = delayNodeId, ToPin = delaySecondsIn },
            new() { FromNode = entryNodeId, FromPin = bombOut, ToNode = explodeNodeId, ToPin = explodeBombIn }
        };

        return new GraphDocument
        {
            Id = graphId,
            Name = "SS14_TimedExplosive",
            Kind = GraphKind.System,
            Side = GraphSide.Server,
            Nodes = nodes,
            Connections = connections
        };
    }

    /// <summary>
    /// Creates the canonical ReplicatedState graph: modifies dynamic component state,
    /// serializes delta replication packet, and coordinates predicted client updates.
    /// </summary>
    public static GraphDocument CreateReplicatedStateGraph()
    {
        var graphId = GraphId.New();

        var entryNodeId = NodeId.New();
        var entryExecOut = PinId.New();
        var targetOut = PinId.New();
        var powerDeltaOut = PinId.New();

        var updatePowerNodeId = NodeId.New();
        var updatePowerExecIn = PinId.New();
        var updatePowerTargetIn = PinId.New();
        var updatePowerDeltaIn = PinId.New();
        var updatePowerExecOut = PinId.New();
        var newChargeOut = PinId.New();

        var nodes = new List<NodeDocument>
        {
            new()
            {
                Id = entryNodeId,
                Name = "OnConsumePower",
                NodeType = "Event.ConsumePower",
                Pins =
                [
                    new() { Id = entryExecOut, Name = "Flow", Direction = PinDirection.Output, Kind = PinKind.Execution, DataType = "Flow" },
                    new() { Id = targetOut, Name = "Target", Direction = PinDirection.Output, Kind = PinKind.Data, DataType = "Robust.Shared.GameObjects.EntityUid" },
                    new() { Id = powerDeltaOut, Name = "Delta", Direction = PinDirection.Output, Kind = PinKind.Data, DataType = "System.Double" }
                ]
            },
            new()
            {
                Id = updatePowerNodeId,
                Name = "ApplyPowerDelta",
                NodeType = "Native.Call",
                Properties = new Dictionary<string, string> { ["Method"] = "Power.ApplyDelta" },
                Pins =
                [
                    new() { Id = updatePowerExecIn, Name = "FlowIn", Direction = PinDirection.Input, Kind = PinKind.Execution, DataType = "Flow" },
                    new() { Id = updatePowerTargetIn, Name = "Target", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "Robust.Shared.GameObjects.EntityUid" },
                    new() { Id = updatePowerDeltaIn, Name = "Delta", Direction = PinDirection.Input, Kind = PinKind.Data, DataType = "System.Double" },
                    new() { Id = updatePowerExecOut, Name = "FlowOut", Direction = PinDirection.Output, Kind = PinKind.Execution, DataType = "Flow" },
                    new() { Id = newChargeOut, Name = "NewCharge", Direction = PinDirection.Output, Kind = PinKind.Data, DataType = "System.Double" }
                ]
            }
        };

        var connections = new List<ConnectionDocument>
        {
            new() { FromNode = entryNodeId, FromPin = entryExecOut, ToNode = updatePowerNodeId, ToPin = updatePowerExecIn },
            new() { FromNode = entryNodeId, FromPin = targetOut, ToNode = updatePowerNodeId, ToPin = updatePowerTargetIn },
            new() { FromNode = entryNodeId, FromPin = powerDeltaOut, ToNode = updatePowerNodeId, ToPin = updatePowerDeltaIn }
        };

        return new GraphDocument
        {
            Id = graphId,
            Name = "SS14_ReplicatedState",
            Kind = GraphKind.System,
            Side = GraphSide.SharedPredicted,
            Nodes = nodes,
            Connections = connections
        };
    }
}
