using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
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
    private static readonly uint FormatVersion = 1;

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
                    case AstraValueType.Vector2:
                        var vec = val.AsVector2();
                        writer.Write(vec.X);
                        writer.Write(vec.Y);
                        break;
                    case AstraValueType.Object:
                        var obj = val.AsObject();
                        if (obj is string str)
                        {
                            writer.Write((byte)1); // String tag
                            writer.Write(str);
                        }
                        else
                        {
                            writer.Write((byte)0); // Null or unrepresented
                        }
                        break;
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
        if (version != FormatVersion)
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
            var valType = (AstraValueType)payloadReader.ReadByte();

            AstraValue val = valType switch
            {
                AstraValueType.Null => AstraValue.Null,
                AstraValueType.Bool => AstraValue.FromBool(payloadReader.ReadBoolean()),
                AstraValueType.Int64 => AstraValue.FromInt64(payloadReader.ReadInt64()),
                AstraValueType.Double => AstraValue.FromDouble(payloadReader.ReadDouble()),
                AstraValueType.EntityUid => AstraValue.FromEntityUid(payloadReader.ReadInt32()),
                AstraValueType.Vector2 => AstraValue.FromVector2(new Vector2(payloadReader.ReadSingle(), payloadReader.ReadSingle())),
                AstraValueType.Object => payloadReader.ReadByte() == 1
                    ? AstraValue.FromObject(payloadReader.ReadString())
                    : AstraValue.Null,
                _ => AstraValue.Null
            };

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
}
