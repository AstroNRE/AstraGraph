using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using AstraGraph.Core;
using AstraGraph.State;

namespace AstraGraph.Persistence.State;

/// <summary>
/// Manages binary persistence of opt-in runtime state snapshots.
/// Ensures reliable disk save/restore across server restarts.
/// </summary>
public sealed class PersistentStateStore
{
    private static readonly uint MagicHeader = 0x54534741; // 'A', 'G', 'S', 'T' (little-endian)
    private static readonly uint FormatVersion = 2;
    private const int MaxCollectionCount = 1_000_000;

    private readonly StorageLayout _layout;

    public PersistentStateStore(StorageLayout layout)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _layout.EnsureDirectories();
    }

    /// <summary>
    /// Captures persistent variables from AstraStateStore and writes them atomically to disk.
    /// </summary>
    public void SaveState(string snapshotName, AstraStateStore stateStore)
    {
        ArgumentNullException.ThrowIfNull(snapshotName);
        ArgumentNullException.ThrowIfNull(stateStore);

        var snapshot = stateStore.GetPersistentSnapshot();
        var filePath = _layout.GetStatePath(snapshotName);

        using var payloadMs = new MemoryStream();
        using (var writer = new BinaryWriter(payloadMs))
        {
            writer.Write(snapshot.Count);
            foreach (var ((graphId, symbolId), (name, val)) in snapshot)
            {
                writer.Write(graphId.Value.ToByteArray());
                writer.Write(symbolId.Value.ToByteArray());
                writer.Write(name);
                try
                {
                    WriteFramed(writer, val);
                }
                catch (IOException)
                {
                    WriteFramed(writer, AstraValue.Null);
                }
                catch (InvalidDataException)
                {
                    WriteFramed(writer, AstraValue.Null);
                }
            }
        }

        var payloadBytes = payloadMs.ToArray();
        var crc = ChecksumUtility.ComputeCrc32(payloadBytes);

        using var finalMs = new MemoryStream();
        using (var writer = new BinaryWriter(finalMs))
        {
            writer.Write(MagicHeader);
            writer.Write(FormatVersion);
            writer.Write(payloadBytes.Length);
            writer.Write(payloadBytes);
            writer.Write(crc);
        }

        AtomicFileStore.WriteAllBytesAtomic(filePath, finalMs.ToArray(), _layout.BackupsDirectory);
    }

    /// <summary>
    /// Restores persistent variables from disk into AstraStateStore.
    /// Returns true if restored successfully, false if snapshot was not found.
    /// Throws InvalidDataException if checksum fails or file is corrupted.
    /// </summary>
    public bool RestoreState(string snapshotName, AstraStateStore stateStore)
    {
        ArgumentNullException.ThrowIfNull(snapshotName);
        ArgumentNullException.ThrowIfNull(stateStore);

        var filePath = _layout.GetStatePath(snapshotName);
        if (!File.Exists(filePath))
            return false;

        var rawBytes = AtomicFileStore.ReadAllBytes(filePath);
        if (rawBytes.Length < 16)
            throw new InvalidDataException("State snapshot file is truncated.");

        using var stream = new MemoryStream(rawBytes);
        using var reader = new BinaryReader(stream);

        var magic = reader.ReadUInt32();
        if (magic != MagicHeader)
            throw new InvalidDataException("Invalid magic header in state file.");

        var version = reader.ReadUInt32();
        if (version < 1 || version > FormatVersion)
            throw new InvalidDataException($"Unsupported state version: {version}.");

        var payloadLen = reader.ReadInt32();
        if (payloadLen < 0 || payloadLen > rawBytes.Length - 16)
            throw new InvalidDataException("Invalid state payload length.");

        var payloadBytes = reader.ReadBytes(payloadLen);
        var expectedCrc = reader.ReadUInt32();

        var actualCrc = ChecksumUtility.ComputeCrc32(payloadBytes);
        if (actualCrc != expectedCrc)
            throw new InvalidDataException("State file CRC32 checksum mismatch (file corrupted).");

        using var payloadStream = new MemoryStream(payloadBytes);
        using var payloadReader = new BinaryReader(payloadStream);

        var count = payloadReader.ReadInt32();
        var snapshot = new Dictionary<(GraphId, SymbolId), (string Name, AstraValue Value)>(count);

        for (var i = 0; i < count; i++)
        {
            var graphGuid = new Guid(payloadReader.ReadBytes(16));
            var symbolGuid = new Guid(payloadReader.ReadBytes(16));
            var name = payloadReader.ReadString();
            var val = version == 1
                ? ReadValueV1(payloadReader)
                : ReadFramed(payloadReader);

            snapshot[(new GraphId(graphGuid), new SymbolId(symbolGuid))] = (name, val);
        }

        stateStore.RestorePersistentSnapshot(snapshot);
        return true;
    }

    /// <summary>
    /// Lists all existing state snapshot names.
    /// </summary>
    public IReadOnlyList<string> ListSnapshots()
    {
        if (!Directory.Exists(_layout.StateDirectory))
            return Array.Empty<string>();

        var list = new List<string>();
        foreach (var file in Directory.EnumerateFiles(_layout.StateDirectory, "*.agstate"))
        {
            list.Add(Path.GetFileNameWithoutExtension(file));
        }
        return list;
    }

    /// <summary>
    /// Deletes a state snapshot file.
    /// </summary>
    public bool DeleteState(string snapshotName)
    {
        var filePath = _layout.GetStatePath(snapshotName);
        if (File.Exists(filePath))
        {
            try
            {
                File.Delete(filePath);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
        }
        return false;
    }

    private static void WriteFramed(BinaryWriter writer, AstraValue value)
    {
        using var payload = new MemoryStream();
        using (var payloadWriter = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
        {
            WritePayload(payloadWriter, value);
        }

        var bytes = payload.ToArray();
        writer.Write((byte)value.Type);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static void WritePayload(BinaryWriter writer, AstraValue value)
    {
        switch (value.Type)
        {
            case AstraValueType.Null:
                break;
            case AstraValueType.Bool:
                writer.Write(value.AsBool());
                break;
            case AstraValueType.Int64:
                writer.Write(value.AsInt64());
                break;
            case AstraValueType.Double:
                writer.Write(value.AsDouble());
                break;
            case AstraValueType.EntityUid:
                writer.Write(value.AsEntityUid());
                break;
            case AstraValueType.Vector2:
                var vec = value.AsVector2();
                writer.Write(vec.X);
                writer.Write(vec.Y);
                break;
            case AstraValueType.Object:
                WriteObject(writer, value.AsObject());
                break;
        }
    }

    private static void WriteObject(BinaryWriter writer, object? obj)
    {
        switch (obj)
        {
            case string text:
                writer.Write((byte)1);
                writer.Write(text);
                break;
            case AstraStruct item:
                writer.Write((byte)2);
                writer.Write(item.SchemaId.Value.ToByteArray());
                writer.Write(item.SchemaName);
                writer.Write(item.Fields.Count);
                foreach (var field in item.Fields)
                {
                    writer.Write(field.Id.Value.ToByteArray());
                    writer.Write(field.Name);
                    WriteFramed(writer, field.Value);
                }

                break;
            case AstraList list:
                writer.Write((byte)3);
                writer.Write(list.Count);
                foreach (var item in list.Items)
                {
                    WriteFramed(writer, item);
                }

                break;
            default:
                writer.Write((byte)0);
                break;
        }
    }

    private static AstraValue ReadValueV1(BinaryReader reader)
    {
        var valType = (AstraValueType)reader.ReadByte();
        return valType switch
        {
            AstraValueType.Null => AstraValue.Null,
            AstraValueType.Bool => AstraValue.FromBool(reader.ReadBoolean()),
            AstraValueType.Int64 => AstraValue.FromInt64(reader.ReadInt64()),
            AstraValueType.Double => AstraValue.FromDouble(reader.ReadDouble()),
            AstraValueType.EntityUid => AstraValue.FromEntityUid(reader.ReadInt32()),
            AstraValueType.Vector2 => AstraValue.FromVector2(new Vector2(reader.ReadSingle(), reader.ReadSingle())),
            AstraValueType.Object => reader.ReadByte() == 1
                ? AstraValue.FromObject(reader.ReadString())
                : AstraValue.Null,
            _ => AstraValue.Null
        };
    }

    private static AstraValue ReadFramed(BinaryReader reader)
    {
        var valType = (AstraValueType)reader.ReadByte();
        var length = reader.ReadInt32();
        if (length < 0 || length > reader.BaseStream.Length - reader.BaseStream.Position)
        {
            throw new InvalidDataException("State value length is invalid.");
        }

        var bytes = reader.ReadBytes(length);
        try
        {
            using var slice = new MemoryStream(bytes);
            using var payload = new BinaryReader(slice, Encoding.UTF8, leaveOpen: true);
            return ReadPayload(payload, valType);
        }
        catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException or ArgumentException or FormatException)
        {
            return AstraValue.Null;
        }
    }

    private static AstraValue ReadPayload(BinaryReader reader, AstraValueType valType)
    {
        return valType switch
        {
            AstraValueType.Null => AstraValue.Null,
            AstraValueType.Bool => AstraValue.FromBool(reader.ReadBoolean()),
            AstraValueType.Int64 => AstraValue.FromInt64(reader.ReadInt64()),
            AstraValueType.Double => AstraValue.FromDouble(reader.ReadDouble()),
            AstraValueType.EntityUid => AstraValue.FromEntityUid(reader.ReadInt32()),
            AstraValueType.Vector2 => AstraValue.FromVector2(new Vector2(reader.ReadSingle(), reader.ReadSingle())),
            AstraValueType.Object => ReadObject(reader),
            _ => AstraValue.Null
        };
    }

    private static AstraValue ReadObject(BinaryReader reader)
    {
        var tag = reader.ReadByte();
        switch (tag)
        {
            case 1:
                return AstraValue.FromObject(reader.ReadString());
            case 2:
                var schemaId = new SchemaId(new Guid(reader.ReadBytes(16)));
                var schemaName = reader.ReadString();
                var fieldCount = reader.ReadInt32();
                if (fieldCount < 0 || fieldCount > MaxCollectionCount)
                {
                    throw new InvalidDataException("Struct field count is invalid.");
                }

                var item = new AstraStruct(schemaId, schemaName);
                for (var i = 0; i < fieldCount; i++)
                {
                    var fieldId = new FieldId(new Guid(reader.ReadBytes(16)));
                    var fieldName = reader.ReadString();
                    item.Set(fieldId, fieldName, ReadFramed(reader));
                }

                return AstraValue.FromObject(item);
            case 3:
                var count = reader.ReadInt32();
                if (count < 0 || count > MaxCollectionCount)
                {
                    throw new InvalidDataException("List count is invalid.");
                }

                var list = new AstraList();
                for (var i = 0; i < count; i++)
                {
                    list.Add(ReadFramed(reader));
                }

                return AstraValue.FromObject(list);
            default:
                return AstraValue.Null;
        }
    }
}
