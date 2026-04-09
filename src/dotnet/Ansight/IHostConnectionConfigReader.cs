namespace Ansight;

/// <summary>
/// Reads pairing config payloads from platform-owned input surfaces such as file pickers or QR scanners.
/// </summary>
public interface IHostConnectionConfigReader
{
    /// <summary>
    /// Indicates whether the reader can handle the specified request kind.
    /// </summary>
    bool CanRead(HostConnectionRequestKind kind);

    /// <summary>
    /// Reads a pairing config payload for the specified request.
    /// </summary>
    Task<string?> ReadConfigPayloadAsync(
        HostConnectionRequest request,
        CancellationToken cancellationToken = default);
}
