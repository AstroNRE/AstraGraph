using AstraGraph.Core;

namespace AstraGraph.Runtime.Network;

/// <summary>
/// Byte transport between Astra sync peers. Robust content sends these frames through its network manager.
/// </summary>
public interface IAstraNetworkTransport
{
    void Send(ReadOnlyMemory<byte> payload);

    event Action<ReadOnlyMemory<byte>>? Received;
}

public sealed class LoopbackAstraTransport : IAstraNetworkTransport
{
    public LoopbackAstraTransport? Peer { get; set; }

    public event Action<ReadOnlyMemory<byte>>? Received;

    public void Send(ReadOnlyMemory<byte> payload)
    {
        Peer?.Received?.Invoke(payload);
    }

    public void Deliver(ReadOnlyMemory<byte> payload)
    {
        Received?.Invoke(payload);
    }
}

public static class AstraSyncFrames
{
    public static byte[] Handshake(JoinHandshakeMessage message)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)1);
        writer.Write(message.CurrentServerTick);
        writer.Write(message.Manifest.Entries.Count);
        foreach (var entry in message.Manifest.Entries)
        {
            writer.Write(entry.Id.Value.ToByteArray());
            writer.Write(entry.Revision.Value.ToByteArray());
            writer.Write(entry.SemanticHash);
        }

        return stream.ToArray();
    }

    public static byte[] Package(GraphPackageMessage message)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)2);
        writer.Write(message.Id.Value.ToByteArray());
        writer.Write(message.Revision.Value.ToByteArray());
        writer.Write(message.BytecodePayload.Length);
        writer.Write(message.BytecodePayload);
        return stream.ToArray();
    }

    public static object Read(ReadOnlyMemory<byte> payload)
    {
        using var stream = new MemoryStream(payload.ToArray());
        using var reader = new BinaryReader(stream);
        var kind = reader.ReadByte();
        if (kind == 1)
        {
            var tick = reader.ReadInt32();
            var count = reader.ReadInt32();
            var entries = new List<SharedGraphEntry>(count);
            for (var i = 0; i < count; i++)
            {
                var id = new GraphId(new Guid(reader.ReadBytes(16)));
                var revision = new RevisionId(new Guid(reader.ReadBytes(16)));
                var hash = reader.ReadString();
                entries.Add(new SharedGraphEntry(id, revision, hash, "", GraphSide.Shared, tick));
            }

            return new JoinHandshakeMessage(AstraNetworkSyncService.CurrentProtocolVersion, new SharedGraphManifest(AstraNetworkSyncService.CurrentProtocolVersion, entries), [], tick);
        }

        if (kind == 2)
        {
            var id = new GraphId(new Guid(reader.ReadBytes(16)));
            var revision = new RevisionId(new Guid(reader.ReadBytes(16)));
            var length = reader.ReadInt32();
            return new GraphPackageMessage(id, revision, reader.ReadBytes(length));
        }

        throw new InvalidDataException($"Unknown Astra sync frame '{kind}'.");
    }
}
