namespace Ansight.Pairing;

internal interface IHostAutoProbeSessionClient : IDisposable
{
    event EventHandler? SessionClosed;

    bool IsSessionOpen { get; }

    bool HasCachedPairingProfile { get; }

    Task<OpenSessionResult> OpenCachedSessionAsync(
        string? clientName,
        IProgress<string>? progress,
        CancellationToken cancellationToken);

    Task<OperationResult> StartMetricsStreamingAsync(
        IDataSink dataSink,
        IProgress<string>? progress,
        CancellationToken cancellationToken);

    Task<OperationResult> CloseSessionAsync(CancellationToken cancellationToken);

    void ClearCachedPairingProfile();
}
