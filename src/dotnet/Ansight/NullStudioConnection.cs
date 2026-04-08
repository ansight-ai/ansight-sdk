using Ansight.Pairing.Models;

namespace Ansight;

internal sealed class NullStudioConnection : IStudioConnection
{
    internal static NullStudioConnection Instance { get; } = new();

    private static readonly StudioConnectionStatus status = new(
        IsRuntimeActive: false,
        IsConnected: false,
        ConnectionState: HostConnectionState.Disconnected,
        HasCachedSession: false,
        HasSavedTicket: false,
        HasBundledTicket: false,
        SummaryKind: StudioConnectionSummaryKind.RuntimeUnavailable,
        SummaryMessage: "Ansight runtime is not initialized.");

    private static readonly StudioConnectionCapabilities capabilities = new(
        CanConnectUsingSavedTicket: false,
        CanConnectUsingBundledTicket: false,
        CanChooseTicketFile: false,
        CanScanTicketQrCode: false,
        CanClearSavedTickets: false);

    public bool HasSavedTicket => false;

    public bool IsConnected => false;

    public StudioConnectionStatus Status => status;

    public StudioConnectionCapabilities Capabilities => capabilities;

    public event EventHandler<StudioConnectionChangedEventArgs>? StatusChanged
    {
        add { }
        remove { }
    }

    private static string StatusSummary => status.SummaryMessage;

    public Task<StudioConnectionCapabilities> RefreshCapabilitiesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(capabilities);

    public bool TryParseTicket(string payload, out PairingTicket? ticket, out string error)
    {
        ticket = null;
        error = StatusSummary;
        return false;
    }

    public Task<StudioConnectionResult> ConnectAsync(
        StudioConnectionRequest? request = null,
        string? clientName = null,
        IProgress<StudioConnectionProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(StudioConnectionResult.FromFailure(
            StatusSummary,
            request?.Kind is StudioConnectionRequestKind.Auto or null
                ? StudioConnectionActionKind.AutoConnect
                : StudioConnectionActionKind.Connect));

    public Task<StudioConnectionResult> DisconnectAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(StudioConnectionResult.FromFailure(StatusSummary, StudioConnectionActionKind.Disconnect));

    public StudioConnectionResult ClearSavedTickets()
        => StudioConnectionResult.FromFailure(StatusSummary, StudioConnectionActionKind.ClearSavedTickets);
}
