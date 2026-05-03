namespace Ansight.Pairing;

internal interface IHostAutoProbeSessionClient
{
    bool IsConnected { get; }

    bool HasCachedProfile { get; }

    DateTimeOffset? LastDisconnectedAtUtc { get; }

    Task<HostSessionActionResult> ConnectUsingCachedProfileAsync(
        string? clientName,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken);

    Task<HostSessionActionResult> DisconnectAsync(CancellationToken cancellationToken);
}
