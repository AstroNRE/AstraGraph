using System;
using System.Collections.Generic;
using AstraGraph.Core;
using AstraGraph.State;

namespace AstraGraph.Runtime.Network;

public sealed record FieldDeltaValue(FieldId FieldId, int SlotIndex, AstraValue Value);

public sealed record ComponentDeltaPacket(
    int EntityUid,
    SchemaId SchemaId,
    IReadOnlyList<FieldDeltaValue> DirtyFields);

/// <summary>
/// Manages high-efficiency delta replication of dynamic component fields over the network.
/// Only fields marked dirty via bitmask dirty-tracking are serialized and transmitted.
/// </summary>
public sealed class DeltaReplicationManager
{
    /// <summary>
    /// Collects dirty field deltas across all entities and schemas in the store,
    /// then clears the dirty tracking flags.
    /// </summary>
    public static List<ComponentDeltaPacket> CollectDirtyDeltas(
        DynamicComponentStore store,
        Func<SchemaId, FieldId, bool>? replicationFilter = null)
    {
        ArgumentNullException.ThrowIfNull(store);

        var packets = new List<ComponentDeltaPacket>();
        var schemaIds = store.GetAllSchemas();

        foreach (var schemaId in schemaIds)
        {
            var entityUids = store.GetEntitiesWithComponent(schemaId);
            foreach (var entityUid in entityUids)
            {
                if (!store.TryGetComponent(entityUid, schemaId, out var storage) || storage is null)
                {
                    continue;
                }

                if (!storage.HasAnyDirty)
                {
                    continue;
                }

                var dirtyList = new List<FieldDeltaValue>();
                for (var slot = 0; slot < storage.FieldCount; slot++)
                {
                    if (storage.IsDirty(slot))
                    {
                        var field = storage.Schema.Fields[slot];
                        if (replicationFilter == null || replicationFilter(schemaId, field.Id))
                        {
                            var val = storage.GetField(slot);
                            dirtyList.Add(new FieldDeltaValue(field.Id, slot, val));
                        }
                    }
                }

                storage.ClearDirty();

                if (dirtyList.Count > 0)
                {
                    packets.Add(new ComponentDeltaPacket(entityUid, schemaId, dirtyList));
                }
            }
        }

        return packets;
    }

    /// <summary>
    /// Applies received delta packets to the target dynamic component store on client or server.
    /// </summary>
    public static void ApplyDeltas(DynamicComponentStore store, IEnumerable<ComponentDeltaPacket> packets)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(packets);

        foreach (var packet in packets)
        {
            if (store.TryGetComponent(packet.EntityUid, packet.SchemaId, out var storage) && storage is not null)
            {
                foreach (var delta in packet.DirtyFields)
                {
                    storage.SetField(delta.SlotIndex, delta.Value);
                }
                storage.ClearDirty();
            }
        }
    }
}
