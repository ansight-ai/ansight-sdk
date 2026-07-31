namespace Ansight.Pairing;

internal interface IHostAutoProbeSessionClient
{
    bool IsConnected { get; }

    bool HasCachedProfile { get; }

    bool CanAttemptLocalEnrollment => false;

    DateTimeOffset? LastDisconnectedAtUtc { get; }

    Task<HostSessionActionResult> ConnectUsingCachedProfileAsync(
        string? clientName,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken);

    Task<HostSessionActionResult> ConnectAutomaticallyAsync(
        string? clientName,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken)
        => ConnectUsingCachedProfileAsync(clientName, progress, cancellationToken);

    Task<HostSessionActionResult> DisconnectAsync(CancellationToken cancellationToken);
}
