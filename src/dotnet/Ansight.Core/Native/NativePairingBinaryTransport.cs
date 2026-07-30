using System.Net.WebSockets;
using Ansight.Pairing;

namespace Ansight.Native;

internal sealed class NativePairingBinaryTransport : IPairingBinaryTransport
{
    private readonly INativeRuntimeBridge nativeRuntime;

    internal NativePairingBinaryTransport(INativeRuntimeBridge nativeRuntime)
    {
        this.nativeRuntime = nativeRuntime ?? throw new ArgumentNullException(nameof(nativeRuntime));
    }

    public bool IsOpen => nativeRuntime.HostConnectionStatus.IsConnected;

    public Task<OperationResult> SendBinaryAsync(
        ReadOnlyMemory<byte> payload,
        WebSocketMessageType messageType,
        CancellationToken cancellationToken)
    {
        if (messageType != WebSocketMessageType.Binary)
        {
            return Task.FromResult(OperationResult.FromFailure(
                "The native pairing transport accepts binary WebSocket messages only."));
        }

        return nativeRuntime.SendBinaryAsync(payload, cancellationToken);
    }
}
