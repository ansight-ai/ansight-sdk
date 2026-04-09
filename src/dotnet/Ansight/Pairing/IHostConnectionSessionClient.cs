using Ansight.Pairing.Models;

namespace Ansight.Pairing;

internal interface IHostConnectionSessionClient : IDisposable
{
    event EventHandler? SessionClosed;

    bool IsSessionOpen { get; }

    bool HasCachedPairingProfile { get; }

    bool TryParseAndValidateDocument(string configJson, out ParsedPairingDocument? document, out string error);

    Task<OpenSessionResult> OpenSessionAsync(
        ParsedPairingDocument document,
        string clientName,
        PairingConnectionOptions? options,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken);

    Task<OpenSessionResult> OpenCachedSessionAsync(
        string? clientName,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken);

    Task<OperationResult> StartMetricsStreamingAsync(
        IDataSink dataSink,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken);

    Task<OperationResult> CloseSessionAsync(CancellationToken cancellationToken);

    void ClearCachedPairingProfile();

    string ResolveClientName(string? overrideClientName);
}
