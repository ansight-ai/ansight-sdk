using Ansight.Pairing;
using Ansight.Tools;

namespace Ansight.Native;

internal sealed class NullNativeRuntimeBridge : INativeRuntimeBridge
{
    internal static NullNativeRuntimeBridge Instance { get; } = new();

    public bool IsAvailable => false;

    public string BridgeVersion => "managed";

    public bool IsInitialized => false;

    public bool IsActive => false;

    public string ProcessSessionId { get; } = Guid.NewGuid().ToString("N");

    public HostConnectionStatus HostConnectionStatus => new(
        false,
        false,
        HostConnectionState.Disconnected,
        false,
        false,
        false,
        HostConnectionSummaryKind.RuntimeUnavailable,
        "The native Ansight runtime is unavailable on this target.");

    public HostConnectionCapabilities HostConnectionCapabilities => new(false, false, false, false, false);

    private NullNativeRuntimeBridge()
    {
    }

    public void Initialize(Options options)
    {
    }

    public void Activate()
    {
    }

    public void Deactivate()
    {
    }

    public void Clear()
    {
    }

    public string? RecordCrashCandidate(
        string runtime,
        string kind,
        string? message,
        string? stack,
        bool fatal,
        string? metadataJson) => null;

    public string PendingCrashReportsJson()
        => $"{{\"processSessionId\":\"{ProcessSessionId}\",\"reports\":[]}}";

    public void AssociateOfflineCaptureSession(string sessionId, string? directory)
    {
    }

    public void CompleteOfflineCaptureSession(string sessionId)
    {
    }

    public bool MarkCrashReportPersistedToOfflineCapture(string reportId) => false;

    public void Metric(long value, byte channel)
    {
    }

    public void Event(string label, AppEventType type, string? details, byte channel)
    {
    }

    public void ScreenViewed(string screenName, string? details, byte channel)
    {
    }

    public void SetAppLifecycleState(AppLifecycleState state, DateTimeOffset changedAtUtc)
    {
    }

    public void EnableFramesPerSecond()
    {
    }

    public void DisableFramesPerSecond()
    {
    }

    public void EnableTouchCapture()
    {
    }

    public void DisableTouchCapture()
    {
    }

    public void SetTouchCaptureGuard(Func<bool>? guard)
    {
    }

    public void RegisterCustomProperty(string group, string key, object? value)
    {
    }

    public void RemoveCustomProperty(string group, string key)
    {
    }

    public void ClearCustomProperties()
    {
    }

    public NativeTelemetrySnapshot ReadTelemetrySnapshot(
        long afterMetricSequence,
        long afterEventSequence)
        => new(Array.Empty<NativeRecordedMetric>(), Array.Empty<NativeRecordedEvent>());

    public Task<HostConnectionResult> ConnectAsync(
        NativeHostConnectionRequest request,
        CancellationToken cancellationToken)
        => Task.FromResult(UnavailableHostConnectionResult(HostConnectionActionKind.Connect));

    public Task<HostConnectionResult> DisconnectAsync(CancellationToken cancellationToken)
        => Task.FromResult(UnavailableHostConnectionResult(HostConnectionActionKind.Disconnect));

    public HostConnectionResult SavePairingConfig(string pairingJson, string? expectedAppId)
        => UnavailableHostConnectionResult(HostConnectionActionKind.ConnectFromPayload);

    public HostConnectionResult ClearSavedPairing()
        => UnavailableHostConnectionResult(HostConnectionActionKind.ClearSavedConfigs);

    public OperationResult ClearCachedSession()
        => OperationResult.FromFailure("The native Ansight runtime is unavailable on this target.");

    public HostConnectionResult NotifyHostConnectionConfigChanged()
        => UnavailableHostConnectionResult(HostConnectionActionKind.NotifyConfigChanged);

    public Task<OperationResult> SendClientLogAsync(string logLine, CancellationToken cancellationToken)
        => Task.FromResult(OperationResult.FromFailure("The native Ansight runtime is unavailable on this target."));

    public Task<OperationResult> CaptureScreenFrameAsync(CancellationToken cancellationToken)
        => Task.FromResult(OperationResult.FromFailure("The native Ansight runtime is unavailable on this target."));

    public Task<OperationResult> SendControlRequestAsync(
        string action,
        string payloadJson,
        CancellationToken cancellationToken)
        => Task.FromResult(OperationResult.FromFailure("The native Ansight runtime is unavailable on this target."));

    public Task<OperationResult> SendBinaryAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
        => Task.FromResult(OperationResult.FromFailure("The native Ansight runtime is unavailable on this target."));

    public void ConfigureToolProtocol(ToolProtocolBridge toolBridge)
    {
    }

    private static HostConnectionResult UnavailableHostConnectionResult(HostConnectionActionKind kind)
        => HostConnectionResult.FromFailure(
            "The native Ansight runtime is unavailable on this target.",
            kind);
}
