using AstraGraph.Runtime.Network;
using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace AstraGraph.Robust.Shared;

/// <summary>
/// Carries Astra sync frames on Robust <see cref="INetManager"/>.
/// </summary>
public sealed class RobustNetManagerTransport : IAstraNetworkTransport
{
    private readonly INetManager _net;

    public RobustNetManagerTransport(INetManager net)
    {
        _net = net ?? throw new ArgumentNullException(nameof(net));
        _net.RegisterNetMessage<MsgAstraSync>(OnMessage);
    }

    public event Action<ReadOnlyMemory<byte>>? Received;

    public void Send(ReadOnlyMemory<byte> payload)
    {
        var message = new MsgAstraSync { Payload = payload.ToArray() };
        if (_net.IsServer)
        {
            _net.ServerSendToAll(message);
            return;
        }

        _net.ClientSendMessage(message);
    }

    private void OnMessage(MsgAstraSync message)
    {
        Received?.Invoke(message.Payload);
    }
}

public sealed class MsgAstraSync : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Entity;

    public byte[] Payload { get; set; } = [];

    public override int EstimateBufferSize() => 4 + Payload.Length;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        var length = buffer.ReadInt32();
        Payload = buffer.ReadBytes(length);
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(Payload.Length);
        buffer.Write(Payload);
    }
}
