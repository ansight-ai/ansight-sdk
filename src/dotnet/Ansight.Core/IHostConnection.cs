using Ansight.Pairing;
using Ansight.Pairing.Models;

namespace Ansight;

/// <summary>
/// Controls runtime-owned Ansight host connection state, pairing configs, and config-based connection flows.
/// </summary>
public interface IHostConnection
{
    /// <summary>
    /// Indicates whether a saved pairing config is stored locally for the current app.
    /// </summary>
    bool HasSavedConfig { get; }

    /// <summary>
    /// Indicates whether a live Ansight host session is currently connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// The latest runtime-owned host connection status snapshot.
    /// </summary>
    HostConnectionStatus Status { get; }

    /// <summary>
    /// The latest runtime-owned host connection capability snapshot.
    /// </summary>
    HostConnectionCapabilities Capabilities { get; }

    /// <summary>
    /// Raised when the runtime-owned host connection status or capabilities change.
    /// </summary>
    event EventHandler<HostConnectionChangedEventArgs>? StatusChanged;

    /// <summary>
    /// Refreshes the connection capability snapshot, including bundled config availability when supported.
    /// </summary>
    Task<HostConnectionCapabilities> RefreshCapabilitiesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Signals that the configured host pairing config source may have changed.
    /// The runtime re-reads and validates the configured bundled config source, updates host connection status,
    /// and raises <see cref="StatusChanged" /> when the effective availability or contents changed.
    /// </summary>
    Task<HostConnectionResult> NotifyConfigChangedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses and validates a pairing config document or compact pairing config code.
    /// </summary>
    bool TryParseConfigDocument(string payload, out PairingConfigDocument? config, out string error);

    /// <summary>
    /// Opens a host connection using the requested config source or flow.
    /// </summary>
    Task<HostConnectionResult> ConnectAsync(
        HostConnectionRequest? request = null,
        string? clientName = null,
        IProgress<HostConnectionProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a single client log line to the connected host over the live pairing session.
    /// </summary>
    Task<OperationResult> SendClientLogAsync(
        string logLine,
        IProgress<HostConnectionProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects the current host connection.
    /// </summary>
    Task<HostConnectionResult> DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears both the saved pairing config and the runtime cached host config.
    /// </summary>
    HostConnectionResult ClearSavedConfigs();
}
