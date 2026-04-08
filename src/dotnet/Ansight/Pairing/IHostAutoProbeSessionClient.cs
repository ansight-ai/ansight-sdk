namespace Ansight.Pairing;

internal interface IHostAutoProbeSessionClient
{
    bool IsConnected { get; }

    bool HasCachedProfile { get; }

    DateTimeOffset? LastDisconnectedAtUtc { get; }

    Task<HostConnectionActionResult> ConnectUsingCachedProfileAsync(
        string? clientName,
        IProgress<StudioConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken);

    Task<HostConnectionActionResult> DisconnectAsync(CancellationToken cancellationToken);
}
