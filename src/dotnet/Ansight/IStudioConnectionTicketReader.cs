namespace Ansight;

/// <summary>
/// Reads ticket payloads from platform-owned input surfaces such as file pickers or QR scanners.
/// </summary>
public interface IStudioConnectionTicketReader
{
    /// <summary>
    /// Indicates whether the reader can handle the specified request kind.
    /// </summary>
    bool CanRead(StudioConnectionRequestKind kind);

    /// <summary>
    /// Reads a ticket payload for the specified request.
    /// </summary>
    Task<string?> ReadTicketPayloadAsync(
        StudioConnectionRequest request,
        CancellationToken cancellationToken = default);
}
