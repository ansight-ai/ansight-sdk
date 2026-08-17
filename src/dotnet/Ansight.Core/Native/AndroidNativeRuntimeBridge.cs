#if ANDROID
using System.Globalization;
using AI.Ansight.Dotnet;
using Ansight.Pairing;
using Ansight.Tools;
using Android.App;

namespace Ansight.Native;

internal sealed class AndroidNativeRuntimeBridge : INativeRuntimeBridge
{
    private ToolProtocolHandler? toolProtocolHandler;
    private TouchCaptureGuard? touchCaptureGuard;

    public bool IsAvailable => true;

    public string BridgeVersion => AnsightDotNetBridge.BridgeVersion;

    public bool IsInitialized => AnsightDotNetBridge.IsInitialized;

    public bool IsActive => AnsightDotNetBridge.IsActive;

    public string ProcessSessionId => AnsightDotNetBridge.ProcessSessionId();

    public HostConnectionStatus HostConnectionStatus
        => NativeRuntimeJson.ParseHostConnectionStatus(AnsightDotNetBridge.HostConnectionStatusJson());

    public HostConnectionCapabilities HostConnectionCapabilities
        => NativeRuntimeJson.ParseHostConnectionCapabilities(AnsightDotNetBridge.HostConnectionCapabilitiesJson());

    public void Initialize(Options options)
    {
        var application = Application.Context as Application
            ?? throw new InvalidOperationException("The Android application context is unavailable.");
        AnsightDotNetBridge.Initialize(application, NativeRuntimeOptionsJson.Serialize(options));
    }

    public void Activate() => AnsightDotNetBridge.Activate();

    public void Deactivate() => AnsightDotNetBridge.Deactivate();

    public void Clear() => AnsightDotNetBridge.Clear();

    public string? RecordCrashCandidate(
        string runtime,
        string kind,
        string? message,
        string? stack,
        bool fatal,
        string? metadataJson)
        => AnsightDotNetBridge.RecordCrashCandidate(runtime, kind, message, stack, fatal, metadataJson);

    public string PendingCrashReportsJson() => AnsightDotNetBridge.PendingCrashReportsJson();

    public void AssociateOfflineCaptureSession(string sessionId, string? directory)
        => AnsightDotNetBridge.AssociateOfflineCaptureSession(sessionId, directory);

    public void CompleteOfflineCaptureSession(string sessionId)
        => AnsightDotNetBridge.CompleteOfflineCaptureSession(sessionId);

    public bool MarkCrashReportPersistedToOfflineCapture(string reportId)
        => AnsightDotNetBridge.MarkCrashReportPersistedToOfflineCapture(reportId);

    public void Metric(long value, byte channel) => AnsightDotNetBridge.RecordMetric(value, channel);

    public void Event(string label, AppEventType type, string? details, byte channel)
    {
        AnsightDotNetBridge.RecordEvent(label, type.ToString(), details, channel);
    }

    public void ScreenViewed(string screenName, string? details, byte channel)
    {
        AnsightDotNetBridge.ScreenViewed(screenName, details, channel);
    }

    public void SetAppLifecycleState(AppLifecycleState state, DateTimeOffset changedAtUtc)
    {
        AnsightDotNetBridge.SetAppLifecycleState(
            state.ToString().ToLowerInvariant(),
            changedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
    }

    public void EnableFramesPerSecond() => AnsightDotNetBridge.EnableFramesPerSecond();

    public void DisableFramesPerSecond() => AnsightDotNetBridge.DisableFramesPerSecond();

    public void EnableTouchCapture() => AnsightDotNetBridge.EnableTouchCapture();

    public void DisableTouchCapture() => AnsightDotNetBridge.DisableTouchCapture();

    public void SetTouchCaptureGuard(Func<bool>? guard)
    {
        touchCaptureGuard?.Dispose();
        touchCaptureGuard = guard is null ? null : new TouchCaptureGuard(guard);
        AnsightDotNetBridge.SetTouchCaptureGuard(touchCaptureGuard);
    }

    public void RegisterCustomProperty(string group, string key, object? value)
    {
        AnsightDotNetBridge.RegisterCustomProperty(
            group,
            key,
            Convert.ToString(value, CultureInfo.InvariantCulture));
    }

    public void RemoveCustomProperty(string group, string key)
    {
        AnsightDotNetBridge.RemoveCustomProperty(group, key);
    }

    public void ClearCustomProperties() => AnsightDotNetBridge.ClearCustomProperties();

    public NativeTelemetrySnapshot ReadTelemetrySnapshot(
        long afterMetricSequence,
        long afterEventSequence)
        => NativeRuntimeJson.ParseTelemetrySnapshot(
            AnsightDotNetBridge.TelemetrySnapshotJson(
                afterMetricSequence,
                afterEventSequence));

    public async Task<HostConnectionResult> ConnectAsync(
        NativeHostConnectionRequest request,
        CancellationToken cancellationToken)
    {
        var json = await Task.Run(
                () => AnsightDotNetBridge.Connect(NativeRuntimeJson.SerializeRequest(request)),
                cancellationToken)
            .WaitAsync(cancellationToken);
        return NativeRuntimeJson.ParseHostConnectionResult(json);
    }

    public async Task<HostConnectionResult> DisconnectAsync(CancellationToken cancellationToken)
    {
        var json = await Task.Run(AnsightDotNetBridge.Disconnect, cancellationToken)
            .WaitAsync(cancellationToken);
        return NativeRuntimeJson.ParseHostConnectionResult(json);
    }

    public HostConnectionResult SavePairingConfig(string pairingJson, string? expectedAppId)
        => NativeRuntimeJson.ParseHostConnectionResult(
            AnsightDotNetBridge.SavePairingConfig(pairingJson, expectedAppId));

    public HostConnectionResult ClearSavedPairing()
        => NativeRuntimeJson.ParseHostConnectionResult(AnsightDotNetBridge.ClearSavedPairing());

    public OperationResult ClearCachedSession()
        => NativeRuntimeJson.ParseOperationResult(AnsightDotNetBridge.ClearCachedSession());

    public HostConnectionResult NotifyHostConnectionConfigChanged()
        => NativeRuntimeJson.ParseHostConnectionResult(AnsightDotNetBridge.NotifyHostConnectionConfigChanged());

    public async Task<OperationResult> SendClientLogAsync(
        string logLine,
        CancellationToken cancellationToken)
    {
        var json = await Task.Run(() => AnsightDotNetBridge.SendClientLog(logLine), cancellationToken)
            .WaitAsync(cancellationToken);
        return NativeRuntimeJson.ParseOperationResult(json);
    }

    public async Task<OperationResult> CaptureScreenFrameAsync(CancellationToken cancellationToken)
    {
        var json = await Task.Run(AnsightDotNetBridge.CaptureScreenFrame, cancellationToken)
            .WaitAsync(cancellationToken);
        return NativeRuntimeJson.ParseOperationResult(json);
    }

    public async Task<OperationResult> SendControlRequestAsync(
        string action,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        var json = await Task.Run(
                () => AnsightDotNetBridge.SendControlRequest(action, payloadJson),
                cancellationToken)
            .WaitAsync(cancellationToken);
        return NativeRuntimeJson.ParseOperationResult(json);
    }

    public async Task<OperationResult> SendSessionEventAsync(
        string type,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        var json = await Task.Run(
                () => AnsightDotNetBridge.SendSessionEvent(type, payloadJson),
                cancellationToken)
            .WaitAsync(cancellationToken);
        return NativeRuntimeJson.ParseOperationResult(json);
    }

    public async Task<OperationResult> SendBinaryAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var bytes = payload.ToArray();
        var json = await Task.Run(() => AnsightDotNetBridge.SendBinary(bytes), cancellationToken)
            .WaitAsync(cancellationToken);
        return NativeRuntimeJson.ParseOperationResult(json);
    }

    public void ConfigureToolProtocol(ToolProtocolBridge toolBridge)
    {
        toolProtocolHandler?.Dispose();
        toolProtocolHandler = new ToolProtocolHandler(toolBridge);
        AnsightDotNetBridge.SetToolCatalog(
            NativeToolProtocolAdapter.CreateSessionCatalogJson(toolBridge));
        AnsightDotNetBridge.SetToolProtocolHandler(toolProtocolHandler);
    }

    private sealed class ToolProtocolHandler : Java.Lang.Object, AnsightDotNetBridge.IToolProtocolHandler
    {
        private readonly ToolProtocolBridge toolBridge;

        internal ToolProtocolHandler(ToolProtocolBridge toolBridge)
        {
            this.toolBridge = toolBridge ?? throw new ArgumentNullException(nameof(toolBridge));
        }

        public string? Process(string? requestJson)
            => NativeToolProtocolAdapter.Handle(toolBridge, requestJson);

        public void ResponseSent(string? requestJson)
            => NativeToolProtocolAdapter.ResponseSent(toolBridge, requestJson);
    }

    private sealed class TouchCaptureGuard : Java.Lang.Object, AnsightDotNetBridge.ITouchCaptureGuard
    {
        private readonly Func<bool> guard;

        internal TouchCaptureGuard(Func<bool> guard)
        {
            this.guard = guard ?? throw new ArgumentNullException(nameof(guard));
        }

        public bool CanCapture() => guard();
    }
}
#endif
