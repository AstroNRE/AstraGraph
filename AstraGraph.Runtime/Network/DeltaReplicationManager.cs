using System;
using System.Collections.Generic;
using AstraGraph.Core;
using AstraGraph.State;

namespace AstraGraph.Runtime.Network;

public sealed record FieldDeltaValue(FieldId FieldId, int SlotIndex, AstraValue Value);

public sealed record ComponentDeltaPacket(
    AstraEntityId EntityUid,
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
    /// optionally filtering by schema/field or requiring SchemaFieldOptions.Replicated.
    /// Clears the dirty tracking flags on collected storages.
    /// </summary>
    public static List<ComponentDeltaPacket> CollectDirtyDeltas(
        DynamicComponentStore store,
        Func<SchemaId, FieldId, bool>? replicationFilter = null,
        bool requireReplicatedFlag = false)
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
                        if (requireReplicatedFlag && !field.IsReplicated)
                        {
                            continue;
                        }

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

    public static int MeasurePendingBytes(DynamicComponentStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        var total = 0;
        foreach (var schemaId in store.GetAllSchemas())
        {
            foreach (var entity in store.GetEntitiesWithComponent(schemaId))
            {
                if (!store.TryGetComponent(entity, schemaId, out var storage) || storage is null || !storage.HasAnyDirty)
                {
                    continue;
                }

                for (var slot = 0; slot < storage.FieldCount; slot++)
                {
                    if (!storage.IsDirty(slot) || !storage.Schema.Fields[slot].IsReplicated)
                    {
                        continue;
                    }

                    total += System.Text.Encoding.UTF8.GetByteCount(storage.GetField(slot).ToString());
                }
            }
        }

        return total;
    }

    /// <summary>
    /// Collects dirty field deltas only for fields marked with SchemaFieldOptions.Replicated.
    /// </summary>
    public static List<ComponentDeltaPacket> CollectReplicatedDeltas(DynamicComponentStore store) =>
        CollectDirtyDeltas(store, requireReplicatedFlag: true);

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

    /// <summary>
    /// Serializes a collection of delta packets into a compact binary byte array.
    /// </summary>
    public static byte[] SerializeDeltas(IReadOnlyList<ComponentDeltaPacket> packets)
    {
        ArgumentNullException.ThrowIfNull(packets);

        using var ms = new System.IO.MemoryStream();
        using var writer = new System.IO.BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true);

        writer.Write(packets.Count);
        foreach (var packet in packets)
        {
            writer.Write(packet.EntityUid);
            writer.Write(packet.SchemaId.Value.ToByteArray());
            writer.Write(packet.DirtyFields.Count);
            foreach (var field in packet.DirtyFields)
            {
                writer.Write(field.FieldId.Value.ToByteArray());
                writer.Write(field.SlotIndex);
                WriteAstraValue(writer, field.Value);
            }
        }

        writer.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// Deserializes a collection of delta packets from a compact binary byte array.
    /// </summary>
    public static List<ComponentDeltaPacket> DeserializeDeltas(ReadOnlySpan<byte> bytes)
    {
        using var ms = new System.IO.MemoryStream(bytes.ToArray());
        using var reader = new System.IO.BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true);

        var packetCount = reader.ReadInt32();
        var packets = new List<ComponentDeltaPacket>(packetCount);

        for (var i = 0; i < packetCount; i++)
        {
            var entityUid = reader.ReadInt32();
            var schemaGuid = new Guid(reader.ReadBytes(16));
            var schemaId = new SchemaId(schemaGuid);
            var fieldCount = reader.ReadInt32();
            var dirtyFields = new List<FieldDeltaValue>(fieldCount);

            for (var f = 0; f < fieldCount; f++)
            {
                var fieldGuid = new Guid(reader.ReadBytes(16));
                var fieldId = new FieldId(fieldGuid);
                var slotIndex = reader.ReadInt32();
                var value = ReadAstraValue(reader);
                dirtyFields.Add(new FieldDeltaValue(fieldId, slotIndex, value));
            }

            packets.Add(new ComponentDeltaPacket(entityUid, schemaId, dirtyFields));
        }

        return packets;
    }

    /// <summary>
    /// Serializes an entire component instance to binary.
    /// </summary>
    public static byte[] SerializeComponent(PackedFieldStorage storage)
    {
        ArgumentNullException.ThrowIfNull(storage);

        using var ms = new System.IO.MemoryStream();
        using var writer = new System.IO.BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true);

        writer.Write(storage.Schema.Id.Value.ToByteArray());
        writer.Write(storage.FieldCount);
        for (var i = 0; i < storage.FieldCount; i++)
        {
            WriteAstraValue(writer, storage.GetField(i));
        }

        writer.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// Deserializes a component instance from binary given its schema.
    /// </summary>
    public static PackedFieldStorage DeserializeComponent(ReadOnlySpan<byte> bytes, SchemaType schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        using var ms = new System.IO.MemoryStream(bytes.ToArray());
        using var reader = new System.IO.BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true);

        var schemaGuid = new Guid(reader.ReadBytes(16));
        if (schemaGuid != schema.Id.Value)
        {
            throw new InvalidOperationException($"Serialized schema GUID {schemaGuid} does not match target schema {schema.Id.Value}");
        }

        var fieldCount = reader.ReadInt32();
        var values = new AstraValue[fieldCount];
        for (var i = 0; i < fieldCount; i++)
        {
            values[i] = ReadAstraValue(reader);
        }

        return new PackedFieldStorage(schema, values);
    }

    private static void WriteAstraValue(System.IO.BinaryWriter writer, AstraValue val)
    {
        writer.Write((byte)val.Type);
        switch (val.Type)
        {
            case AstraValueType.Null:
                break;
            case AstraValueType.Bool:
                writer.Write(val.AsBool());
                break;
            case AstraValueType.Int64:
                writer.Write(val.AsInt64());
                break;
            case AstraValueType.Double:
                writer.Write(val.AsDouble());
                break;
            case AstraValueType.EntityUid:
                writer.Write(val.AsEntityUid());
                break;
            case AstraValueType.Object:
                writer.Write(val.AsString() ?? string.Empty);
                break;
            case AstraValueType.PersistentId:
                writer.Write(val.AsPersistentId().Value.ToByteArray());
                break;
        }
    }

    private static AstraValue ReadAstraValue(System.IO.BinaryReader reader)
    {
        var type = (AstraValueType)reader.ReadByte();
        return type switch
        {
            AstraValueType.Null => AstraValue.Null,
            AstraValueType.Bool => AstraValue.FromBool(reader.ReadBoolean()),
            AstraValueType.Int64 => AstraValue.FromInt64(reader.ReadInt64()),
            AstraValueType.Double => AstraValue.FromDouble(reader.ReadDouble()),
            AstraValueType.EntityUid => AstraValue.FromEntityUid(reader.ReadInt32()),
            AstraValueType.Object => AstraValue.FromString(reader.ReadString()),
            AstraValueType.PersistentId => AstraValue.FromPersistentId(new PersistentObjectId(new Guid(reader.ReadBytes(16)))),
            _ => AstraValue.Null
        };
    }
}
