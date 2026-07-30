using System.Net.WebSockets;

namespace Ansight.Pairing;

internal interface IPairingBinaryTransport
{
    bool IsOpen { get; }

    Task<OperationResult> SendBinaryAsync(
        ReadOnlyMemory<byte> payload,
        WebSocketMessageType messageType,
        CancellationToken cancellationToken);
}
