using Ansight.Pairing.Models;

namespace Ansight;

internal sealed class NullHostSessionConnection : IHostSessionConnection
{
    internal static NullHostSessionConnection Instance { get; } = new();

    public HostConnectionState State => HostConnectionState.Disconnected;

    public bool IsConnected => false;

    public bool HasCachedProfile => false;

    public string StatusSummary => "Ansight runtime is not initialized.";

    public event EventHandler<HostSessionStatusChangedEventArgs>? StatusChanged
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

    public Task<HostSessionActionResult> ConnectAsync(
        ParsedPairingDocument document,
        string? clientName = null,
        PairingConnectionOptions? connectionOptions = null,
        IProgress<HostConnectionProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(HostSessionActionResult.FromFailure(
            StatusSummary,
            kind: HostConnectionActionKind.ConnectFromPayload,
            source: HostConnectionSource.HostConnection));
    }

    public Task<HostSessionActionResult> ConnectUsingCachedProfileAsync(
        string? clientName = null,
        IProgress<HostConnectionProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(HostSessionActionResult.FromFailure(
            StatusSummary,
            kind: HostConnectionActionKind.ConnectUsingCachedSession,
            source: HostConnectionSource.CachedSession));
    }

    public Task<HostSessionActionResult> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(HostSessionActionResult.FromFailure(
            StatusSummary,
            kind: HostConnectionActionKind.Disconnect,
            source: HostConnectionSource.HostConnection));
    }

    public HostSessionActionResult ClearCachedProfile()
    {
        return HostSessionActionResult.FromFailure(
            StatusSummary,
            kind: HostConnectionActionKind.ClearSavedConfigs,
            source: HostConnectionSource.CachedSession);
    }
}
