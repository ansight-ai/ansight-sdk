using Ansight.Pairing;
using Ansight.Tools;

namespace Ansight.Native;

internal interface INativeRuntimeBridge
{
    bool IsAvailable { get; }

    string BridgeVersion { get; }

    bool IsInitialized { get; }

    bool IsActive { get; }

    HostConnectionStatus HostConnectionStatus { get; }

    HostConnectionCapabilities HostConnectionCapabilities { get; }

    void Initialize(Options options);

    void Activate();

    void Deactivate();

    void Clear();

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
