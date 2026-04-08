using Ansight.Pairing.Models;

namespace Ansight;

/// <summary>
/// Controls runtime-owned Ansight Studio connection state, pairing tickets, and ticket-based connection flows.
/// </summary>
public interface IStudioConnection
{
    /// <summary>
    /// Indicates whether a saved pairing ticket is stored locally for the current app.
    /// </summary>
    bool HasSavedTicket { get; }

    /// <summary>
    /// Indicates whether a live Ansight Studio session is currently connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// The latest runtime-owned Studio connection status snapshot.
    /// </summary>
    StudioConnectionStatus Status { get; }

    /// <summary>
    /// The latest runtime-owned Studio connection capability snapshot.
    /// </summary>
    StudioConnectionCapabilities Capabilities { get; }

    /// <summary>
    /// Raised when the runtime-owned Studio connection status or capabilities change.
    /// </summary>
    event EventHandler<StudioConnectionChangedEventArgs>? StatusChanged;

    /// <summary>
    /// Refreshes the connection capability snapshot, including bundled ticket availability when supported.
    /// </summary>
    Task<StudioConnectionCapabilities> RefreshCapabilitiesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses and validates a pairing ticket or compact pairing ticket code.
    /// </summary>
    bool TryParseTicket(string payload, out PairingTicket? ticket, out string error);

    /// <summary>
    /// Opens a Studio connection using the requested ticket source or flow.
    /// </summary>
    Task<StudioConnectionResult> ConnectAsync(
        StudioConnectionRequest? request = null,
        string? clientName = null,
        IProgress<StudioConnectionProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects the current Studio connection.
    /// </summary>
    Task<StudioConnectionResult> DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears both the saved pairing ticket and the runtime cached Studio ticket.
    /// </summary>
    StudioConnectionResult ClearSavedTickets();
}
