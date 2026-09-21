using System.Net.WebSockets;

namespace AstraGraph.Editor.Protocol;

/// <summary>
/// State of an authoring transport connection.
/// </summary>
public enum TransportState
{
    Disconnected,
    Connecting,
    Connected,
    Disconnecting,
    Faulted
}

/// <summary>
/// Unified transport abstraction for Astra Studio Web.
/// Implementations: LocalGameBridgeTransport (loopback) and RemoteServerTransport (direct WSS).
/// </summary>
public interface IAuthoringTransport : IAsyncDisposable
{
    TransportState State { get; }
    string? SessionId { get; }
    bool IsAuthenticated { get; }

    event Action<string>? OnConnected;
    event Action<string?>? OnDisconnected;
    event Action<AuthoringMessage>? OnMessageReceived;
    event Action<Exception>? OnError;

    Task ConnectAsync(Uri endpoint, CancellationToken ct = default);
    Task SendMessageAsync(AuthoringMessage message, CancellationToken ct = default);
    Task DisconnectAsync();
}

/// <summary>
/// Raw framed message envelope over WebSocket.
/// </summary>
public sealed record FramedMessage(
    string MessageId,
    string Kind,
    byte[] Payload,
    DateTimeOffset SentAt);

/// <summary>
/// Framing utilities for WebSocket message transport.
/// Binary format: [4 bytes kind-length][kind UTF8][payload bytes]
/// </summary>
public static class MessageFramer
{
    public static byte[] Frame(AuthoringMessage message)
    {
        byte[] kindBytes = System.Text.Encoding.UTF8.GetBytes(message.Kind);
        byte[] payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(message, message.GetType(), AuthoringJsonContext.Default);

        var buffer = new byte[4 + kindBytes.Length + payload.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(buffer, kindBytes.Length);
        kindBytes.CopyTo(buffer, 4);
        payload.CopyTo(buffer, 4 + kindBytes.Length);
        return buffer;
    }

    public static (string Kind, byte[] Payload) Unframe(byte[] buffer)
    {
        if (buffer.Length < 4)
            throw new InvalidOperationException("Buffer too small to contain framing header.");

        int kindLength = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(buffer);
        if (kindLength < 0 || kindLength > buffer.Length - 4)
            throw new InvalidOperationException($"Invalid kind length: {kindLength}.");

        string kind = System.Text.Encoding.UTF8.GetString(buffer, 4, kindLength);
        byte[] payload = buffer[(4 + kindLength)..];
        return (kind, payload);
    }
}
