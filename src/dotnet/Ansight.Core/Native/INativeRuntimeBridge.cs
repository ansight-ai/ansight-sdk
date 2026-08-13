using Ansight.Pairing;
using Ansight.Tools;

namespace Ansight.Native;

internal interface INativeRuntimeBridge
{
    bool IsAvailable { get; }

    string BridgeVersion { get; }

    bool IsInitialized { get; }

    bool IsActive { get; }

    string ProcessSessionId { get; }

    HostConnectionStatus HostConnectionStatus { get; }

    HostConnectionCapabilities HostConnectionCapabilities { get; }

    void Initialize(Options options);

    void Activate();

    void Deactivate();

    void Clear();

    string? RecordCrashCandidate(
        string runtime,
        string kind,
        string? message,
        string? stack,
        bool fatal,
        string? metadataJson);

    string PendingCrashReportsJson();

    void AssociateOfflineCaptureSession(string sessionId, string? directory);

    void CompleteOfflineCaptureSession(string sessionId);

    bool MarkCrashReportPersistedToOfflineCapture(string reportId);

    void Metric(long value, byte channel);

    void Event(string label, AppEventType type, string? details, byte channel);

    void ScreenViewed(string screenName, string? details, byte channel);

    void SetAppLifecycleState(AppLifecycleState state, DateTimeOffset changedAtUtc);

    void EnableFramesPerSecond();

    void DisableFramesPerSecond();

    void EnableTouchCapture();

    void DisableTouchCapture();

    void SetTouchCaptureGuard(Func<bool>? guard);

    void RegisterCustomProperty(string group, string key, object? value);

    void RemoveCustomProperty(string group, string key);

    void ClearCustomProperties();

    NativeTelemetrySnapshot ReadTelemetrySnapshot(
        long afterMetricSequence,
        long afterEventSequence);

    Task<HostConnectionResult> ConnectAsync(
        NativeHostConnectionRequest request,
        CancellationToken cancellationToken);

    Task<HostConnectionResult> DisconnectAsync(CancellationToken cancellationToken);

    HostConnectionResult SavePairingConfig(string pairingJson, string? expectedAppId);

    HostConnectionResult ClearSavedPairing();

    OperationResult ClearCachedSession();

    HostConnectionResult NotifyHostConnectionConfigChanged();

    Task<OperationResult> SendClientLogAsync(string logLine, CancellationToken cancellationToken);

    Task<OperationResult> CaptureScreenFrameAsync(CancellationToken cancellationToken);

    Task<OperationResult> SendControlRequestAsync(
        string action,
        string payloadJson,
        CancellationToken cancellationToken);

    Task<OperationResult> SendBinaryAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken);

    void ConfigureToolProtocol(ToolProtocolBridge toolBridge);
}
