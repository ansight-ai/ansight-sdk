using Ansight.Pairing.Models;
using Ansight.Network;

namespace Ansight.Pairing;

internal interface IHostConnectionSessionClient : IDisposable
{
    event EventHandler? SessionClosed;

    bool IsSessionOpen { get; }

    bool HasCachedPairingProfile { get; }

    bool CanAttemptLocalEnrollment => false;

    bool TryParseAndValidateDocument(string configJson, out ParsedPairingDocument? document, out string error);

    Task<OpenSessionResult> OpenSessionAsync(
        ParsedPairingDocument document,
        string clientName,
        PairingConnectionOptions? options,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken);

    Task<OpenSessionResult> OpenCachedSessionAsync(
        string? clientName,
        PairingConnectionOptions? options,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken);

    Task<OpenSessionResult> OpenLocalSessionAsync(
        string? clientName,
        PairingConnectionOptions? options,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken)
        => Task.FromResult(OpenSessionResult.FromFailure(
            "Automatic local enrollment is unavailable.",
            PairingFailureCodes.EnrollmentUnavailable));

    Task<OperationResult> StartMetricsStreamingAsync(
        IDataSink dataSink,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken);

    Task<OperationResult> StartTouchCaptureStreamingAsync(
        TouchCaptureHub touchCaptureHub,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken);

    Task<OperationResult> StartNetworkRequestStreamingAsync(
        NetworkRequestHub networkRequestHub,
        CancellationToken cancellationToken)
        => Task.FromResult(OperationResult.FromSuccess("Network request streaming is unavailable for this session client."));

    Task<OperationResult> SendClientLogAsync(
        string logLine,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken);

    Task<OperationResult> CloseSessionAsync(CancellationToken cancellationToken);

    void ClearCachedPairingProfile();

    string ResolveClientName(string? overrideClientName);
}
