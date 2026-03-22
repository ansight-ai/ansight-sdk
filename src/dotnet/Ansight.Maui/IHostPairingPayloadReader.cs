namespace Ansight.Maui;

/// <summary>
/// Reads pairing payloads from MAUI-specific sources such as file pickers or QR scanners.
/// </summary>
public interface IHostPairingPayloadReader
{
    /// <summary>
    /// Indicates whether this reader can handle the specified request kind.
    /// </summary>
    bool CanRead(HostPairingPayloadReadKind kind);

    /// <summary>
    /// Reads a pairing payload for the specified request.
    /// </summary>
    Task<string?> ReadPayloadAsync(
        HostPairingPayloadReadRequest request,
        CancellationToken cancellationToken = default);
}
