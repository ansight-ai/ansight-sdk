using Ansight.Pairing.Models;

namespace Ansight;

internal sealed class NullHostConnection : IHostConnection
{
    internal static NullHostConnection Instance { get; } = new();

    public HostConnectionState State => HostConnectionState.Disconnected;

    public bool IsConnected => false;

    public bool HasCachedProfile => false;

    public string StatusSummary => "Ansight runtime is not initialized.";

    public event EventHandler<HostConnectionStatusChangedEventArgs>? StatusChanged
    {
        add { }
        remove { }
    }

    public bool TryParseAndValidateDocument(string configJson, out ParsedPairingDocument? document, out string error)
    {
        document = null;
        error = "Ansight runtime is not initialized.";
        return false;
    }

    public Task<HostConnectionActionResult> ConnectAsync(
        ParsedPairingDocument document,
        string? clientName = null,
        PairingConnectionOptions? connectionOptions = null,
        IProgress<HostPairingProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(HostConnectionActionResult.FromFailure(
            StatusSummary,
            kind: HostPairingActionKind.ConnectFromPayload,
            source: HostPairingSource.HostConnection));
    }

    public Task<HostConnectionActionResult> ConnectUsingCachedProfileAsync(
        string? clientName = null,
        IProgress<HostPairingProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(HostConnectionActionResult.FromFailure(
            StatusSummary,
            kind: HostPairingActionKind.ConnectUsingCachedProfile,
            source: HostPairingSource.CachedProfile));
    }

    public Task<HostConnectionActionResult> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(HostConnectionActionResult.FromFailure(
            StatusSummary,
            kind: HostPairingActionKind.Disconnect,
            source: HostPairingSource.HostConnection));
    }

    public HostConnectionActionResult ClearCachedProfile()
    {
        return HostConnectionActionResult.FromFailure(
            StatusSummary,
            kind: HostPairingActionKind.ClearStoredProfiles,
            source: HostPairingSource.CachedProfile);
    }
}
