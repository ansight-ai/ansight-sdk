using Ansight.Pairing;
using Ansight.Pairing.Models;

namespace Ansight;

/// <summary>
/// Controls the runtime-owned Ansight host connection.
/// </summary>
internal interface IHostSessionConnection
{
    HostConnectionState State { get; }

    bool IsConnected { get; }

    bool HasCachedProfile { get; }

    string StatusSummary { get; }

    event EventHandler<HostSessionStatusChangedEventArgs>? StatusChanged;

    bool TryParseAndValidateDocument(string configJson, out ParsedPairingDocument? document, out string error);

    Task<HostSessionActionResult> ConnectAsync(
        ParsedPairingDocument document,
        string? clientName = null,
        PairingConnectionOptions? connectionOptions = null,
        IProgress<HostConnectionProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);

    Task<HostSessionActionResult> ConnectUsingCachedProfileAsync(
        string? clientName = null,
        IProgress<HostConnectionProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);

    Task<OperationResult> SendClientLogAsync(
        string logLine,
        IProgress<HostConnectionProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);

    Task<HostSessionActionResult> DisconnectAsync(CancellationToken cancellationToken = default);

    HostSessionActionResult ClearCachedProfile();
}
