#if IOS || MACCATALYST
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ansight.Native.Apple;
using Ansight.Network;
using Ansight.Pairing;
using Ansight.Screenshot;
using Ansight.Tools;
using Foundation;

namespace Ansight.Native;

internal sealed class AppleNativeRuntimeBridge : INativeRuntimeBridge
{
    private ANSToolProtocolHandler? toolProtocolHandler;
    private ANSToolProtocolResponseSentHandler? toolProtocolResponseSentHandler;
    private ANSBooleanHandler? touchCaptureGuard;
    private ANSSessionVisualTreeCaptureProvider? sessionVisualTreeCaptureProvider;

    public bool IsAvailable => true;

    public string BridgeVersion => ANSDotNetRuntime.BridgeVersion;

    public bool IsInitialized => ANSDotNetRuntime.IsInitialized;

    public bool IsActive => ANSDotNetRuntime.IsActive;

    public string ProcessSessionId => ANSDotNetRuntime.ProcessSessionId;

    public HostConnectionStatus HostConnectionStatus
        => NativeRuntimeJson.ParseHostConnectionStatus(ANSDotNetRuntime.HostConnectionStatusJson());

    public HostConnectionCapabilities HostConnectionCapabilities
        => NativeRuntimeJson.ParseHostConnectionCapabilities(ANSDotNetRuntime.HostConnectionCapabilitiesJson());

    public void Initialize(Options options)
    {
        ThrowIfNativeError(ANSDotNetRuntime.Initialize(NativeRuntimeOptionsJson.Serialize(options)));
        sessionVisualTreeCaptureProvider = CaptureSessionVisualTreesJson;
        ANSDotNetRuntime.SetSessionVisualTreeCaptureProvider(sessionVisualTreeCaptureProvider);
    }

    public void Activate()
    {
        ThrowIfNativeError(ANSDotNetRuntime.Activate());
    }

    public void Deactivate() => ANSDotNetRuntime.Deactivate();

    public void Clear() => ANSDotNetRuntime.Clear();

    public void RecordNetworkRequest(NetworkRequestRecord request)
        => ANSDotNetRuntime.RecordNetworkRequest(
            JsonSerializer.Serialize(request, PairingJson.Compact));

    public void SetNetworkCaptureRedactionEnabled(bool enabled)
        => ANSDotNetRuntime.SetNetworkCaptureRedactionEnabled(enabled);

    public string? RecordCrashCandidate(
        string runtime,
        string kind,
        string? message,
        string? stack,
        bool fatal,
        string? metadataJson)
        => ANSDotNetRuntime.RecordCrashCandidate(runtime, kind, message, stack, fatal, metadataJson);

    public string PendingCrashReportsJson() => ANSDotNetRuntime.PendingCrashReportsJson();

    public void AssociateOfflineCaptureSession(string sessionId, string? directory)
        => ANSDotNetRuntime.AssociateOfflineCaptureSession(sessionId, directory);

    public void CompleteOfflineCaptureSession(string sessionId)
        => ANSDotNetRuntime.CompleteOfflineCaptureSession(sessionId);

    public bool MarkCrashReportPersistedToOfflineCapture(string reportId)
        => ANSDotNetRuntime.MarkCrashReportPersistedToOfflineCapture(reportId);

    public void Metric(long value, byte channel)
    {
        ThrowIfNativeError(ANSDotNetRuntime.RecordMetric(value, channel));
    }

    public void Event(string label, AppEventType type, string? details, byte channel)
    {
        ThrowIfNativeError(ANSDotNetRuntime.RecordEvent(label, type.ToString(), details, channel));
    }

    public void ScreenViewed(string screenName, string? details, byte channel)
    {
        ThrowIfNativeError(ANSDotNetRuntime.ScreenViewed(screenName, details, channel));
    }

    public void SetAppLifecycleState(AppLifecycleState state, DateTimeOffset changedAtUtc)
    {
        ANSDotNetRuntime.SetAppLifecycleState(
            state.ToString().ToLowerInvariant(),
            changedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
    }

    public void EnableFramesPerSecond() => ANSDotNetRuntime.EnableFramesPerSecond();

    public void DisableFramesPerSecond() => ANSDotNetRuntime.DisableFramesPerSecond();

    public void EnableTouchCapture() => ANSDotNetRuntime.EnableTouchCapture();

    public void DisableTouchCapture() => ANSDotNetRuntime.DisableTouchCapture();

    public void SetTouchCaptureGuard(Func<bool>? guard)
    {
        touchCaptureGuard = guard is null ? null : () => guard();
        ANSDotNetRuntime.SetTouchCaptureGuard(touchCaptureGuard);
    }

    public void RegisterCustomProperty(string group, string key, object? value)
    {
        ANSDotNetRuntime.RegisterCustomProperty(
            group,
            key,
            Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
    }

    public void RemoveCustomProperty(string group, string key)
    {
        ANSDotNetRuntime.RemoveCustomProperty(group, key);
    }

    public void ClearCustomProperties()
    {
        ANSDotNetRuntime.ClearCustomProperties();
    }

    public NativeTelemetrySnapshot ReadTelemetrySnapshot(
        long afterMetricSequence,
        long afterEventSequence)
        => NativeRuntimeJson.ParseTelemetrySnapshot(
            ANSDotNetRuntime.TelemetrySnapshotJson(
                afterMetricSequence,
                afterEventSequence));

    public async Task<HostConnectionResult> ConnectAsync(
        NativeHostConnectionRequest request,
        CancellationToken cancellationToken)
    {
        var json = await InvokeAsync(
            completion => ANSDotNetRuntime.Connect(NativeRuntimeJson.SerializeRequest(request), completion),
            cancellationToken);
        return NativeRuntimeJson.ParseHostConnectionResult(json);
    }

    public async Task<HostConnectionResult> DisconnectAsync(CancellationToken cancellationToken)
    {
        var json = await InvokeAsync(ANSDotNetRuntime.Disconnect, cancellationToken);
        return NativeRuntimeJson.ParseHostConnectionResult(json);
    }

    public HostConnectionResult SavePairingConfig(string pairingJson, string? expectedAppId)
        => NativeRuntimeJson.ParseHostConnectionResult(
            ANSDotNetRuntime.SavePairingConfig(pairingJson, expectedAppId));

    public HostConnectionResult ClearSavedPairing()
        => NativeRuntimeJson.ParseHostConnectionResult(ANSDotNetRuntime.ClearSavedPairing());

    public OperationResult ClearCachedSession()
        => NativeRuntimeJson.ParseOperationResult(ANSDotNetRuntime.ClearCachedSession());

    public HostConnectionResult NotifyHostConnectionConfigChanged()
        => NativeRuntimeJson.ParseHostConnectionResult(ANSDotNetRuntime.NotifyHostConnectionConfigChanged());

    public async Task<OperationResult> SendClientLogAsync(
        string logLine,
        CancellationToken cancellationToken)
    {
        var json = await InvokeAsync(
            completion => ANSDotNetRuntime.SendClientLog(logLine, completion),
            cancellationToken);
        return NativeRuntimeJson.ParseOperationResult(json);
    }

    public async Task<OperationResult> CaptureScreenFrameAsync(CancellationToken cancellationToken)
    {
        var json = await InvokeAsync(ANSDotNetRuntime.CaptureScreenFrame, cancellationToken);
        return NativeRuntimeJson.ParseOperationResult(json);
    }

    public async Task<OperationResult> SendControlRequestAsync(
        string action,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        var json = await InvokeAsync(
            completion => ANSDotNetRuntime.SendControlRequest(action, payloadJson, completion),
            cancellationToken);
        return NativeRuntimeJson.ParseOperationResult(json);
    }

    public async Task<OperationResult> SendBinaryAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        using var data = NSData.FromArray(payload.ToArray());
        var json = await InvokeAsync(
            completion => ANSDotNetRuntime.SendBinary(data, completion),
            cancellationToken);
        return NativeRuntimeJson.ParseOperationResult(json);
    }

    public void ConfigureToolProtocol(ToolProtocolBridge toolBridge)
    {
        toolProtocolHandler = requestJson =>
            NativeToolProtocolAdapter.Handle(toolBridge, requestJson);
        toolProtocolResponseSentHandler = requestJson =>
            NativeToolProtocolAdapter.ResponseSent(toolBridge, requestJson);
        ANSDotNetRuntime.SetToolProtocolHandler(toolProtocolHandler);
        ANSDotNetRuntime.SetToolProtocolResponseSentHandler(toolProtocolResponseSentHandler);
    }

    private static string CaptureSessionVisualTreesJson()
    {
        try
        {
            var visualTrees = SessionVisualTreeCaptureRegistry
                .CaptureAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            return new JsonArray(
                visualTrees.Select(visualTree => visualTree.DeepClone()).ToArray())
                .ToJsonString();
        }
        catch (Exception exception)
        {
            Logger.Warning($"Native session visual-tree capture skipped: {exception.Message}");
            return "[]";
        }
    }

    private static void ThrowIfNativeError(string? error)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            throw new InvalidOperationException(error);
        }
    }

    private static async Task<string> InvokeAsync(
        Action<ANSStringResultHandler> start,
        CancellationToken cancellationToken)
    {
        var completionSource = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellationRegistration = cancellationToken.Register(
            () => completionSource.TrySetCanceled(cancellationToken));
        start(resultJson => completionSource.TrySetResult(resultJson));
        return await completionSource.Task.ConfigureAwait(false);
    }
}
#endif
