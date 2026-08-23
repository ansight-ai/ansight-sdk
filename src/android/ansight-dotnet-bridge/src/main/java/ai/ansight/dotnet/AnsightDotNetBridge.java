package ai.ansight.dotnet;

import android.app.Application;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Iterator;
import java.util.Locale;
import java.util.TimeZone;

import ai.ansight.runtime.AnsightChannel;
import ai.ansight.runtime.AnsightCrashCaptureOptions;
import ai.ansight.runtime.AnsightEventType;
import ai.ansight.runtime.AnsightHostAutoProbeOptions;
import ai.ansight.runtime.AnsightHostConnectionOptions;
import ai.ansight.runtime.AnsightOptions;
import ai.ansight.runtime.AnsightOptionsBuilder;
import ai.ansight.runtime.AnsightRuntime;
import ai.ansight.runtime.AnsightSessionJpegCaptureMode;
import ai.ansight.runtime.AppLifecycleState;
import ai.ansight.runtime.DefaultMemoryChannels;
import ai.ansight.runtime.HostConnectionCapabilities;
import ai.ansight.runtime.HostConnectionRequest;
import ai.ansight.runtime.HostConnectionRequestKind;
import ai.ansight.runtime.HostConnectionResult;
import ai.ansight.runtime.HostConnectionStatus;
import ai.ansight.runtime.OperationResult;
import ai.ansight.runtime.RecordedEvent;
import ai.ansight.runtime.RecordedMetric;
import ai.ansight.tools.jnireferencediagnostics.AndroidJniReferenceDiagnostics;

/**
 * Stable Java surface consumed by the .NET Android binding.
 *
 * The bridge deliberately exposes only Java and Android platform types. Kotlin
 * implementation models remain private to the native SDK so changes to those
 * models do not create broad .NET binding API churn.
 */
public final class AnsightDotNetBridge {
    public static final String BRIDGE_VERSION = "1";

    public interface ToolProtocolHandler {
        String process(String requestJson);
        void responseSent(String requestJson);
    }

    public interface TouchCaptureGuard {
        boolean canCapture();
    }

    private AnsightDotNetBridge() {
    }

    public static void initialize(Application application, String optionsJson) throws JSONException {
        if (application == null) {
            throw new IllegalArgumentException("application must not be null");
        }

        AnsightRuntime.INSTANCE.initialize(application, buildOptions(optionsJson));
        JSONObject options = normalize(optionsJson) == null
            ? new JSONObject()
            : new JSONObject(optionsJson);
        int memoryChannels = options.optInt("defaultMemoryChannels", 7);
        if ((memoryChannels & 1) != 0) {
            AnsightRuntime.INSTANCE.setMetricChannelExternallyManaged(
                ai.ansight.runtime.AnsightChannels.JavaHeap,
                true
            );
        }
        if (options.optBoolean("enableJniReferenceCountTracking", false)) {
            AnsightRuntime.INSTANCE.setMetricChannelExternallyManaged(
                ai.ansight.runtime.AnsightChannels.JniReferenceCount,
                true
            );
        }
    }

    public static boolean isInitialized() {
        return AnsightRuntime.INSTANCE.snapshot().getInitialized();
    }

    public static boolean isActive() {
        return AnsightRuntime.INSTANCE.snapshot().getActive();
    }

    public static String processSessionId() {
        return AnsightRuntime.INSTANCE.processSessionId();
    }

    public static void activate() {
        AnsightRuntime.INSTANCE.activate();
    }

    public static void deactivate() {
        AnsightRuntime.INSTANCE.deactivate();
    }

    public static void clear() {
        AnsightRuntime.INSTANCE.clear();
    }

    public static void recordNetworkRequest(String requestJson) {
        AnsightRuntime.INSTANCE.recordNetworkRequest(requestJson);
    }

    public static String recordCrashCandidate(
        String runtime,
        String kind,
        String message,
        String stack,
        boolean fatal,
        String metadataJson
    ) throws JSONException {
        return AnsightRuntime.INSTANCE.recordCrashCandidate(
            runtime,
            kind,
            normalize(message),
            normalize(stack),
            fatal,
            normalize(metadataJson)
        );
    }

    public static String pendingCrashReportsJson() {
        return AnsightRuntime.INSTANCE.pendingCrashReportsJson();
    }

    public static void associateOfflineCaptureSession(String sessionId, String directory) {
        AnsightRuntime.INSTANCE.associateOfflineCaptureSession(sessionId, normalize(directory));
    }

    public static void completeOfflineCaptureSession(String sessionId) {
        AnsightRuntime.INSTANCE.completeOfflineCaptureSession(sessionId);
    }

    public static boolean markCrashReportPersistedToOfflineCapture(String reportId) {
        return AnsightRuntime.INSTANCE.markCrashReportPersistedToOfflineCapture(reportId);
    }

    public static void recordMetric(long value, int channel) {
        AnsightRuntime.INSTANCE.metric(value, channel);
    }

    public static void recordEvent(String label, String type, String details, int channel) {
        AnsightRuntime.INSTANCE.event(
            label,
            eventType(type),
            normalize(details),
            channel,
            java.util.UUID.randomUUID().toString(),
            null
        );
    }

    public static void screenViewed(String name, String details, int channel) throws JSONException {
        java.util.Map<String, String> nativeDetails = new java.util.LinkedHashMap<>();
        String normalizedDetails = normalize(details);
        if (normalizedDetails != null) {
            nativeDetails.put("details", normalizedDetails);
        }
        AnsightRuntime.INSTANCE.screenViewed(name, nativeDetails, channel);
    }

    public static void setAppLifecycleState(String state, String changedAtUtc) {
        AnsightRuntime.INSTANCE.setAppLifecycleState(
            lifecycleState(state),
            normalize(changedAtUtc) == null
                ? isoNow()
                : changedAtUtc
        );
    }

    public static void enableFramesPerSecond() {
        AnsightRuntime.INSTANCE.enableFramesPerSecond();
    }

    public static void disableFramesPerSecond() {
        AnsightRuntime.INSTANCE.disableFramesPerSecond();
    }

    public static void enableTouchCapture() {
        AnsightRuntime.INSTANCE.enableTouchCapture();
    }

    public static void disableTouchCapture() {
        AnsightRuntime.INSTANCE.disableTouchCapture();
    }

    public static void setTouchCaptureGuard(final TouchCaptureGuard guard) {
        if (guard == null) {
            AnsightRuntime.INSTANCE.setTouchCaptureGuard(null);
            return;
        }

        AnsightRuntime.INSTANCE.setTouchCaptureGuard(guard::canCapture);
    }

    public static void registerCustomProperty(String group, String key, String value) {
        AnsightRuntime.INSTANCE.registerCustomProperty(group, key, value);
    }

    public static void removeCustomProperty(String group, String key) {
        AnsightRuntime.INSTANCE.removeCustomProperty(group, key);
    }

    public static void clearCustomProperties() {
        AnsightRuntime.INSTANCE.clearCustomProperties();
    }

    public static String hostConnectionStatusJson() throws JSONException {
        return hostConnectionStatusJson(AnsightRuntime.INSTANCE.hostConnectionStatus()).toString();
    }

    public static String hostConnectionCapabilitiesJson() throws JSONException {
        HostConnectionCapabilities capabilities = AnsightRuntime.INSTANCE.hostConnectionCapabilities();
        return new JSONObject()
            .put("canConnectUsingSavedConfig", capabilities.getCanConnectUsingSavedConfig())
            .put("canConnectUsingBundledConfig", capabilities.getCanConnectUsingBundledConfig())
            .put("canChooseConfigFile", capabilities.getCanChooseConfigFile())
            .put("canScanConfigQrCode", capabilities.getCanScanConfigQrCode())
            .put("canClearSavedConfigs", capabilities.getCanClearSavedConfigs())
            .toString();
    }

    public static String telemetrySnapshotJson(
        long afterMetricSequence,
        long afterEventSequence
    ) throws JSONException {
        JSONArray metrics = new JSONArray();
        for (RecordedMetric metric : AnsightRuntime.INSTANCE.recordedMetrics()) {
            if (metric.getSequence() <= afterMetricSequence) {
                continue;
            }
            metrics.put(new JSONObject()
                .put("value", metric.getValue())
                .put("channel", metric.getChannel())
                .put("capturedAtUtc", metric.getCapturedAtUtc())
                .put("capturedAtEpochMs", metric.getCapturedAtEpochMs())
                .put("sequence", metric.getSequence()));
        }

        JSONArray events = new JSONArray();
        for (RecordedEvent event : AnsightRuntime.INSTANCE.recordedEvents()) {
            if (event.getSequence() <= afterEventSequence) {
                continue;
            }
            events.put(new JSONObject()
                .put("label", event.getLabel())
                .put("type", event.getType().name())
                .put("details", event.getDetails())
                .put("channel", event.getChannel())
                .put("capturedAtUtc", event.getCapturedAtUtc())
                .put("capturedAtEpochMs", event.getCapturedAtEpochMs())
                .put("externalId", event.getExternalId())
                .put("sequence", event.getSequence()));
        }

        return new JSONObject()
            .put("metrics", metrics)
            .put("events", events)
            .toString();
    }

    public static String connect(String requestJson) throws JSONException {
        JSONObject json = new JSONObject(requestJson);
        HostConnectionRequest request = new HostConnectionRequest(
            requestKind(json.optString("kind", "auto")),
            optionalString(json, "payload"),
            optionalString(json, "clientName"),
            optionalString(json, "expectedAppId"),
            optionalString(json, "hostAddressOverride")
        );
        return hostConnectionResultJson(AnsightRuntime.INSTANCE.connect(request)).toString();
    }

    public static String disconnect() throws JSONException {
        return hostConnectionResultJson(AnsightRuntime.INSTANCE.disconnect()).toString();
    }

    public static String savePairingConfig(String pairingJson, String expectedAppId) throws JSONException {
        return hostConnectionResultJson(
            AnsightRuntime.INSTANCE.savePairingConfig(pairingJson, normalize(expectedAppId))
        ).toString();
    }

    public static String clearSavedPairing() throws JSONException {
        return hostConnectionResultJson(AnsightRuntime.INSTANCE.clearSavedPairingConfig()).toString();
    }

    public static String clearCachedSession() throws JSONException {
        return operationResultJson(AnsightRuntime.INSTANCE.clearCachedSession()).toString();
    }

    public static String notifyHostConnectionConfigChanged() throws JSONException {
        return hostConnectionResultJson(AnsightRuntime.INSTANCE.notifyHostConnectionConfigChanged()).toString();
    }

    public static String sendClientLog(String logLine) throws JSONException {
        return operationResultJson(AnsightRuntime.INSTANCE.sendClientLog(logLine)).toString();
    }

    public static String captureScreenFrame() throws JSONException {
        return operationResultJson(AnsightRuntime.INSTANCE.captureScreenFrame(null)).toString();
    }

    public static String captureJniReferenceGraph(
        Application application,
        int maximumNodes,
        int maximumEdges,
        int maximumDepth
    ) {
        if (application == null) {
            throw new IllegalArgumentException("application must not be null");
        }
        return AndroidJniReferenceDiagnostics.captureGraph(
            application,
            maximumNodes,
            maximumEdges,
            maximumDepth
        );
    }

    public static String sendControlRequest(String action, String payloadJson) throws JSONException {
        JSONObject payload = normalize(payloadJson) == null
            ? null
            : new JSONObject(payloadJson);
        return operationResultJson(
            AnsightRuntime.INSTANCE.sendControlRequest(action, payload)
        ).toString();
    }

    public static String sendBinary(byte[] payload) throws JSONException {
        if (payload == null) {
            throw new IllegalArgumentException("payload must not be null");
        }
        return operationResultJson(AnsightRuntime.INSTANCE.sendBinaryData(payload)).toString();
    }

    public static void setToolProtocolHandler(final ToolProtocolHandler handler) {
        if (handler == null) {
            AnsightRuntime.INSTANCE.setExternalToolProtocolCallback(null);
            AnsightRuntime.INSTANCE.setExternalToolProtocolResponseSentCallback(null);
            return;
        }

        AnsightRuntime.INSTANCE.setExternalToolProtocolCallback(handler::process);
        AnsightRuntime.INSTANCE.setExternalToolProtocolResponseSentCallback(requestJson -> {
            handler.responseSent(requestJson);
            return kotlin.Unit.INSTANCE;
        });
    }

    public static void setToolCatalog(String catalogJson) throws JSONException {
        JSONObject catalog = new JSONObject(catalogJson);
        AnsightRuntime.INSTANCE.setExternalToolCatalog(
            stringList(catalog.optJSONArray("toolIds")),
            stringList(catalog.optJSONArray("toolCategories"))
        );
    }

    private static AnsightOptions buildOptions(String optionsJson) throws JSONException {
        JSONObject json = normalize(optionsJson) == null
            ? new JSONObject()
            : new JSONObject(optionsJson);
        AnsightOptionsBuilder builder = AnsightOptions.createBuilder()
            .withSampleFrequencyMilliseconds(json.optInt("sampleFrequencyMilliseconds", 500))
            .withRetentionPeriodSeconds(json.optInt("retentionPeriodSeconds", 600));

        if (json.optBoolean("enableFramesPerSecond", true)) {
            builder.withFramesPerSecond();
        } else {
            builder.withoutFramesPerSecond();
        }

        if (json.optBoolean("enableBatteryLevel", false)) {
            builder.withBatteryLevel();
        } else {
            builder.withoutBatteryLevel();
        }

        if (json.optBoolean("enableOpenFileHandleTracking", false)) {
            builder.withOpenFileHandleTracking();
        } else {
            builder.withoutOpenFileHandleTracking();
        }

        if (json.optBoolean("enableJniReferenceCountTracking", false)) {
            builder.withJniReferenceCountTracking();
        } else {
            builder.withoutJniReferenceCountTracking();
        }

        int memoryChannels = json.optInt("defaultMemoryChannels", 7);
        builder.withDefaultMemoryChannels(new DefaultMemoryChannels(
            (memoryChannels & 1) != 0,
            (memoryChannels & 2) != 0,
            (memoryChannels & 4) != 0
        ));

        JSONArray additionalChannels = json.optJSONArray("additionalChannels");
        if (additionalChannels != null) {
            for (int index = 0; index < additionalChannels.length(); index++) {
                JSONObject channel = additionalChannels.getJSONObject(index);
                builder.addAdditionalChannel(new AnsightChannel(
                    channel.getInt("id"),
                    channel.getString("name"),
                    optionalString(channel, "color"),
                    optionalString(channel, "unit"),
                    channel.optString("type", "custom"),
                    optionalString(channel, "source"),
                    optionalString(channel, "group"),
                    optionalString(channel, "kind")
                ));
            }
        }

        JSONObject sessionJpegCapture = json.optJSONObject("sessionJpegCapture");
        if (sessionJpegCapture == null || sessionJpegCapture == JSONObject.NULL) {
            builder.withoutSessionJpegCapture();
        } else {
            builder.withSessionJpegCapture(
                sessionJpegCapture.optInt("intervalMilliseconds", 2_000),
                sessionJpegCapture.optInt("quality", 60),
                sessionJpegCapture.has("maxWidth") && !sessionJpegCapture.isNull("maxWidth")
                    ? Integer.valueOf(sessionJpegCapture.getInt("maxWidth"))
                    : null,
                sessionJpegCapture.optBoolean("captureGpuBackedSurfaces", true),
                sessionJpegCaptureMode(sessionJpegCapture.optString("mode")),
                sessionJpegCapture.optBoolean("captureKeyboardPresence", false)
            );
        }

        JSONObject touchCapture = json.optJSONObject("touchCapture");
        if (touchCapture == null || touchCapture == JSONObject.NULL) {
            builder.withoutTouchCapture();
        } else {
            builder.withTouchCapture(
                touchCapture.optDouble("moveCaptureDistanceThreshold", 4.0),
                touchCapture.optInt("moveCaptureFramesPerSecond", 15)
            );
        }

        JSONObject crashCapture = json.optJSONObject("crashCapture");
        if (crashCapture == null || !crashCapture.optBoolean("enabled", true)) {
            builder.withoutCrashCapture();
        } else {
            builder.withCrashCapture(new AnsightCrashCaptureOptions(
                true,
                crashCapture.optBoolean("studioHandoffEnabled", true),
                crashCapture.optBoolean("offlineCaptureAttachmentEnabled", true),
                crashCapture.optInt("maximumPendingReports", 8),
                crashCapture.optInt("retentionDays", 7),
                crashCapture.optInt("maximumBreadcrumbs", 64),
                crashCapture.optInt("maximumTraceBytes", 1_048_576)
            ));
        }

        applyToolGuard(builder, json.optString("toolGuard", "disabled"));
        applyCustomProperties(builder, json.optJSONObject("customProperties"));
        applyHostOptions(builder, json);

        return builder.build();
    }

    private static AnsightSessionJpegCaptureMode sessionJpegCaptureMode(String value) {
        if ("screenshotAndVisualTree".equalsIgnoreCase(value)) {
            return AnsightSessionJpegCaptureMode.ScreenshotAndVisualTree;
        }
        if ("screenshotWithVisualTreeOnTouch".equalsIgnoreCase(value)) {
            return AnsightSessionJpegCaptureMode.ScreenshotWithVisualTreeOnTouch;
        }
        return AnsightSessionJpegCaptureMode.ScreenshotOnly;
    }

    private static HostConnectionRequestKind requestKind(String value) {
        String normalized = normalize(value);
        if ("savedConfig".equalsIgnoreCase(normalized)) {
            return HostConnectionRequestKind.SavedConfig;
        }
        if ("bundledConfig".equalsIgnoreCase(normalized)) {
            return HostConnectionRequestKind.BundledConfig;
        }
        if ("file".equalsIgnoreCase(normalized)) {
            return HostConnectionRequestKind.File;
        }
        if ("qrCode".equalsIgnoreCase(normalized)) {
            return HostConnectionRequestKind.QrCode;
        }
        if ("payload".equalsIgnoreCase(normalized)) {
            return HostConnectionRequestKind.Payload;
        }
        if ("config".equalsIgnoreCase(normalized)) {
            return HostConnectionRequestKind.Config;
        }
        return HostConnectionRequestKind.Auto;
    }

    private static JSONObject hostConnectionResultJson(HostConnectionResult result) throws JSONException {
        return new JSONObject()
            .put("success", result.getSuccess())
            .put("message", result.getMessage())
            .put("kind", result.getKind().name())
            .put("source", result.getSource().name())
            .put("reasonCode", result.getReasonCode());
    }

    private static JSONObject operationResultJson(OperationResult result) throws JSONException {
        return new JSONObject()
            .put("success", result.getSuccess())
            .put("message", result.getMessage());
    }

    private static java.util.List<String> stringList(JSONArray values) throws JSONException {
        java.util.List<String> result = new java.util.ArrayList<>();
        if (values == null) {
            return result;
        }
        for (int index = 0; index < values.length(); index++) {
            String value = normalize(values.optString(index, null));
            if (value != null && !result.contains(value)) {
                result.add(value);
            }
        }
        return result;
    }

    private static JSONObject hostConnectionStatusJson(HostConnectionStatus status) throws JSONException {
        return new JSONObject()
            .put("isRuntimeActive", status.isRuntimeActive())
            .put("isConnected", status.isConnected())
            .put("connectionState", status.getConnectionState().name())
            .put("hasCachedSession", status.getHasCachedSession())
            .put("hasSavedConfig", status.getHasSavedConfig())
            .put("hasBundledConfig", status.getHasBundledConfig())
            .put("summaryKind", status.getSummaryKind().name())
            .put("summaryMessage", status.getSummaryMessage());
    }

    private static void applyToolGuard(AnsightOptionsBuilder builder, String toolGuard) {
        switch (toolGuard.toLowerCase(java.util.Locale.ROOT)) {
            case "readonly":
                builder.withReadOnlyToolAccess();
                break;
            case "readwrite":
                builder.withReadWriteToolAccess();
                break;
            case "fullaccess":
                builder.withAllToolAccess();
                break;
            default:
                builder.withToolsDisabled();
                break;
        }
    }

    private static void applyCustomProperties(
        AnsightOptionsBuilder builder,
        JSONObject customProperties
    ) throws JSONException {
        if (customProperties == null) {
            return;
        }

        Iterator<String> groups = customProperties.keys();
        while (groups.hasNext()) {
            String group = groups.next();
            JSONObject properties = customProperties.optJSONObject(group);
            if (properties == null) {
                continue;
            }

            Iterator<String> keys = properties.keys();
            while (keys.hasNext()) {
                String key = keys.next();
                builder.registerCustomProperty(group, key, properties.optString(key, ""));
            }
        }
    }

    private static void applyHostOptions(AnsightOptionsBuilder builder, JSONObject json) {
        JSONObject autoProbe = json.optJSONObject("hostAutoProbe");
        if (autoProbe == null) {
            builder.withoutHostAutoProbe();
        } else {
            builder.withHostAutoProbe(new AnsightHostAutoProbeOptions(
                autoProbe.optBoolean("enabled", false),
                autoProbe.optLong("initialDelayMilliseconds", 1_000),
                autoProbe.optLong("probeIntervalMilliseconds", 5_000),
                autoProbe.optLong("reconnectDelayMilliseconds", 10_000),
                optionalString(autoProbe, "clientName")
            ));
        }

        JSONObject hostConnection = json.optJSONObject("hostConnection");
        if (hostConnection != null) {
            builder.withHostConnection(new AnsightHostConnectionOptions(
                hostConnection.optString("savedConfigKey", "ai.ansight.android.saved-pairing"),
                optionalString(hostConnection, "bundledConfigJson"),
                hostConnection.has("discoveryPort") && !hostConnection.isNull("discoveryPort")
                    ? Integer.valueOf(hostConnection.optInt("discoveryPort"))
                    : null,
                hostConnection.optBoolean("allowCellularConnections", false),
                hostConnection.optBoolean("allowUnattendedProvisioning", false),
                hostConnection.optLong("connectionProfileRetentionSeconds", 14L * 24L * 60L * 60L),
                null
            ));
        }
    }

    private static AnsightEventType eventType(String value) {
        String normalized = normalize(value);
        if (normalized == null) {
            return AnsightEventType.Info;
        }

        for (AnsightEventType candidate : AnsightEventType.values()) {
            if (candidate.name().equalsIgnoreCase(normalized)) {
                return candidate;
            }
        }
        return AnsightEventType.Info;
    }

    private static AppLifecycleState lifecycleState(String value) {
        String normalized = normalize(value);
        if ("foreground".equalsIgnoreCase(normalized)) {
            return AppLifecycleState.Foreground;
        }
        if ("background".equalsIgnoreCase(normalized)) {
            return AppLifecycleState.Background;
        }
        return AppLifecycleState.Unknown;
    }

    private static String optionalString(JSONObject json, String key) {
        if (!json.has(key) || json.isNull(key)) {
            return null;
        }
        return normalize(json.optString(key, null));
    }

    private static String normalize(String value) {
        if (value == null) {
            return null;
        }
        String normalized = value.trim();
        return normalized.isEmpty() ? null : normalized;
    }

    private static String isoNow() {
        SimpleDateFormat formatter =
            new SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss.SSS'Z'", Locale.US);
        formatter.setTimeZone(TimeZone.getTimeZone("UTC"));
        return formatter.format(new Date());
    }
}
