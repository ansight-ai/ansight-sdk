using Foundation;
using ObjCRuntime;

namespace Ansight.Native.Apple;

delegate void ANSStringResultHandler(string resultJson);
delegate bool ANSBooleanHandler();
[return: NullAllowed]
delegate string ANSSessionVisualTreeCaptureProvider();
delegate void ANSToolProtocolResponseSentHandler(string requestJson);
[return: NullAllowed]
delegate string ANSToolProtocolHandler(string requestJson);

[BaseType(typeof(NSObject))]
interface ANSDotNetRuntime
{
    [Static]
    [Export("bridgeVersion")]
    string BridgeVersion { get; }

    [Static]
    [Export("isInitialized")]
    bool IsInitialized { get; }

    [Static]
    [Export("isActive")]
    bool IsActive { get; }

    [Static]
    [Export("processSessionId")]
    string ProcessSessionId { get; }

    [Static]
    [return: NullAllowed]
    [Export("initializeWithOptionsJson:")]
    string Initialize([NullAllowed] string optionsJson);

    [Static]
    [return: NullAllowed]
    [Export("activate")]
    string Activate();

    [Static]
    [Export("deactivate")]
    void Deactivate();

    [Static]
    [Export("clear")]
    void Clear();

    [Static]
    [Export("recordNetworkRequest:")]
    void RecordNetworkRequest(string requestJson);

    [Static]
    [Export("setNetworkCaptureRedactionEnabled:")]
    void SetNetworkCaptureRedactionEnabled(bool enabled);

    [Static]
    [return: NullAllowed]
    [Export("recordCrashCandidateWithRuntime:kind:message:stack:fatal:metadataJson:")]
    string RecordCrashCandidate(
        string runtime,
        string kind,
        [NullAllowed] string message,
        [NullAllowed] string stack,
        bool fatal,
        [NullAllowed] string metadataJson);

    [Static]
    [Export("pendingCrashReportsJson")]
    string PendingCrashReportsJson();

    [Static]
    [Export("associateOfflineCaptureSession:directory:")]
    void AssociateOfflineCaptureSession(string sessionId, [NullAllowed] string directory);

    [Static]
    [Export("completeOfflineCaptureSession:")]
    void CompleteOfflineCaptureSession(string sessionId);

    [Static]
    [Export("markCrashReportPersistedToOfflineCapture:")]
    bool MarkCrashReportPersistedToOfflineCapture(string reportId);

    [Static]
    [return: NullAllowed]
    [Export("recordMetric:channel:")]
    string RecordMetric(long value, nint channel);

    [Static]
    [return: NullAllowed]
    [Export("recordEventWithLabel:type:details:channel:")]
    string RecordEvent(
        string label,
        [NullAllowed] string type,
        [NullAllowed] string details,
        nint channel);

    [Static]
    [return: NullAllowed]
    [Export("screenViewedWithName:details:channel:")]
    string ScreenViewed(string name, [NullAllowed] string details, nint channel);

    [Static]
    [Export("setAppLifecycleState:changedAtUtc:")]
    void SetAppLifecycleState(string state, string changedAtUtc);

    [Static]
    [Export("enableFramesPerSecond")]
    void EnableFramesPerSecond();

    [Static]
    [Export("disableFramesPerSecond")]
    void DisableFramesPerSecond();

    [Static]
    [Export("enableTouchCapture")]
    void EnableTouchCapture();

    [Static]
    [Export("disableTouchCapture")]
    void DisableTouchCapture();

    [Static]
    [Export("setTouchCaptureGuard:")]
    void SetTouchCaptureGuard([NullAllowed, BlockCallback] ANSBooleanHandler guardCallback);

    [Static]
    [Export("setSessionVisualTreeCaptureProvider:")]
    void SetSessionVisualTreeCaptureProvider(
        [NullAllowed, BlockCallback] ANSSessionVisualTreeCaptureProvider provider);

    [Static]
    [Export("registerCustomPropertyWithGroup:key:value:")]
    void RegisterCustomProperty(string group, string key, string value);

    [Static]
    [Export("removeCustomPropertyWithGroup:key:")]
    void RemoveCustomProperty(string group, string key);

    [Static]
    [Export("clearCustomProperties")]
    void ClearCustomProperties();

    [Static]
    [Export("hostConnectionStatusJson")]
    string HostConnectionStatusJson();

    [Static]
    [Export("hostConnectionCapabilitiesJson")]
    string HostConnectionCapabilitiesJson();

    [Static]
    [Export("telemetrySnapshotJsonAfterMetricSequence:afterEventSequence:")]
    string TelemetrySnapshotJson(long afterMetricSequence, long afterEventSequence);

    [Static]
    [Export("connectWithRequestJson:completion:")]
    void Connect(string requestJson, [BlockCallback] ANSStringResultHandler completion);

    [Static]
    [Export("disconnectWithCompletion:")]
    void Disconnect([BlockCallback] ANSStringResultHandler completion);

    [Static]
    [Export("savePairingConfig:expectedAppId:")]
    string SavePairingConfig(string pairingJson, [NullAllowed] string expectedAppId);

    [Static]
    [Export("clearSavedPairing")]
    string ClearSavedPairing();

    [Static]
    [Export("clearCachedSession")]
    string ClearCachedSession();

    [Static]
    [Export("notifyHostConnectionConfigChanged")]
    string NotifyHostConnectionConfigChanged();

    [Static]
    [Export("sendClientLog:completion:")]
    void SendClientLog(string logLine, [BlockCallback] ANSStringResultHandler completion);

    [Static]
    [Export("captureScreenFrameWithCompletion:")]
    void CaptureScreenFrame([BlockCallback] ANSStringResultHandler completion);

    [Static]
    [Export("sendControlRequest:payloadJson:completion:")]
    void SendControlRequest(
        string action,
        string payloadJson,
        [BlockCallback] ANSStringResultHandler completion);

    [Static]
    [Export("sendBinary:completion:")]
    void SendBinary(NSData payload, [BlockCallback] ANSStringResultHandler completion);

    [Static]
    [Export("setToolProtocolHandler:")]
    void SetToolProtocolHandler([NullAllowed, BlockCallback] ANSToolProtocolHandler handler);

    [Static]
    [Export("setToolProtocolResponseSentHandler:")]
    void SetToolProtocolResponseSentHandler(
        [NullAllowed, BlockCallback] ANSToolProtocolResponseSentHandler handler);
}
