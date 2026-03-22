using Ansight.Pairing.Models;

namespace Ansight;

/// <summary>
/// Controls the runtime-owned Ansight host connection.
/// </summary>
public interface IHostConnection
{
    HostConnectionState State { get; }

    bool IsConnected { get; }

    bool HasCachedProfile { get; }

    string StatusSummary { get; }

    event EventHandler<HostConnectionStatusChangedEventArgs>? StatusChanged;

    bool TryParseAndValidateDocument(string configJson, out ParsedPairingDocument? document, out string error);

    Task<HostConnectionActionResult> ConnectAsync(
        ParsedPairingDocument document,
        string? clientName = null,
        PairingConnectionOptions? connectionOptions = null,
        IProgress<HostPairingProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);

    Task<HostConnectionActionResult> ConnectUsingCachedProfileAsync(
        string? clientName = null,
        IProgress<HostPairingProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);

    Task<HostConnectionActionResult> DisconnectAsync(CancellationToken cancellationToken = default);

    HostConnectionActionResult ClearCachedProfile();
}
