package ai.ansight.runtime

import java.util.UUID
import kotlin.math.ceil

object AnsightSamplingLimits {
    const val DefaultSampleFrequencyMilliseconds = 500
    const val MinSampleFrequencyMilliseconds = 200
    const val MaxSampleFrequencyMilliseconds = 2_000
    const val DefaultRetentionPeriodSeconds = 600
    const val MinRetentionPeriodSeconds = 60
    const val MaxRetentionPeriodSeconds = 3_600
}

object AnsightChannels {
    const val JavaHeap = 0
    const val NativeHeap = 1
    const val Rss = 2
    const val FramesPerSecond = 3
    const val Lifecycle = 4
    const val BatteryLevel = 5
    const val Unspecified = 255

    val reservedIds: Set<Int> = setOf(
        JavaHeap,
        NativeHeap,
        Rss,
        FramesPerSecond,
        Lifecycle,
        BatteryLevel,
        Unspecified,
    )
}

data class AnsightChannel(
    val id: Int,
    val name: String,
    val colorHex: String? = null,
    val unit: String? = null,
    val type: String = "custom",
) {
    fun validated(): AnsightChannel {
        require(id in 0..255) { "Channel ids must be between 0 and 255." }
        require(name.isNotBlank()) { "Channel names must not be blank." }
        return copy(name = name.trim(), colorHex = colorHex?.trim()?.ifBlank { null })
    }
}

data class DefaultMemoryChannels(
    val javaHeap: Boolean = true,
    val nativeHeap: Boolean = true,
    val rss: Boolean = true,
) {
    companion object {
        val PlatformDefaults = DefaultMemoryChannels()
        val None = DefaultMemoryChannels(javaHeap = false, nativeHeap = false, rss = false)
    }
}

data class AnsightSessionJpegCaptureOptions(
    val intervalMilliseconds: Int = 1_000,
    val quality: Int = 75,
    val maxWidth: Int? = 960,
) {
    fun validated(): AnsightSessionJpegCaptureOptions {
        return copy(
            intervalMilliseconds = intervalMilliseconds.coerceAtLeast(250),
            quality = quality.coerceIn(1, 100),
            maxWidth = maxWidth?.takeIf { it > 0 }?.coerceAtMost(8_192),
        )
    }
}

data class AnsightTouchCaptureOptions(
    val moveCaptureDistanceThreshold: Double = 8.0,
    val moveCaptureFramesPerSecond: Int = 20,
) {
    fun validated(): AnsightTouchCaptureOptions {
        return copy(
            moveCaptureDistanceThreshold = moveCaptureDistanceThreshold.takeIf { it.isFinite() && it >= 0.0 } ?: 8.0,
            moveCaptureFramesPerSecond = moveCaptureFramesPerSecond.coerceAtLeast(0),
        )
    }
}

enum class AnsightToolGuard {
    Disabled,
    ReadOnly,
    Full,
}

data class AnsightHostAutoProbeOptions(
    val enabled: Boolean = true,
    val initialDelayMilliseconds: Long = 1_000,
    val probeIntervalMilliseconds: Long = 5_000,
    val reconnectDelayMilliseconds: Long = 10_000,
    val clientName: String? = null,
) {
    fun validated(): AnsightHostAutoProbeOptions {
        return copy(
            initialDelayMilliseconds = initialDelayMilliseconds.coerceAtLeast(0),
            probeIntervalMilliseconds = probeIntervalMilliseconds.coerceAtLeast(1_000),
            reconnectDelayMilliseconds = reconnectDelayMilliseconds.coerceAtLeast(1_000),
            clientName = clientName?.trim()?.ifBlank { null },
        )
    }
}

data class AnsightHostConnectionOptions(
    val savedConfigKey: String = "ai.ansight.android.saved-pairing",
    val bundledConfigJson: String? = null,
    val bundledDeveloperConfigJson: String? = null,
    val discoveryPort: Int? = null,
    val connectionProfileRetentionSeconds: Long = 14L * 24L * 60L * 60L,
) {
    fun validated(): AnsightHostConnectionOptions {
        if (discoveryPort != null) {
            require(discoveryPort in 1..65_535) { "Discovery port must be between 1 and 65535." }
        }

        return copy(
            savedConfigKey = savedConfigKey.trim().ifBlank { "ai.ansight.android.saved-pairing" },
            bundledConfigJson = bundledConfigJson?.trim()?.ifBlank { null },
            bundledDeveloperConfigJson = bundledDeveloperConfigJson?.trim()?.ifBlank { null },
            connectionProfileRetentionSeconds = connectionProfileRetentionSeconds.coerceAtLeast(1),
        )
    }
}

data class AnsightSecureStorageOptions(
    val preferencesName: String = "ai.ansight.secure-storage",
    val allowedKeys: Set<String> = emptySet(),
    val allowedPrefixes: Set<String> = emptySet(),
) {
    fun validated(): AnsightSecureStorageOptions {
        return copy(
            preferencesName = preferencesName.trim().ifBlank { "ai.ansight.secure-storage" },
            allowedKeys = allowedKeys.mapNotNull { it.trim().ifBlank { null } }.toSet(),
            allowedPrefixes = allowedPrefixes.mapNotNull { it.trim().ifBlank { null } }.toSet(),
        )
    }

    fun isAllowed(key: String): Boolean {
        val normalized = key.trim()
        return normalized in allowedKeys || allowedPrefixes.any { normalized.startsWith(it) }
    }
}

data class AnsightOptions(
    val sampleFrequencyMilliseconds: Int = AnsightSamplingLimits.DefaultSampleFrequencyMilliseconds,
    val retentionPeriodSeconds: Int = AnsightSamplingLimits.DefaultRetentionPeriodSeconds,
    val enableFramesPerSecond: Boolean = true,
    val enableBatteryLevel: Boolean = false,
    val additionalChannels: List<AnsightChannel> = emptyList(),
    val defaultMemoryChannels: DefaultMemoryChannels = DefaultMemoryChannels.PlatformDefaults,
    val sessionJpegCapture: AnsightSessionJpegCaptureOptions? = null,
    val touchCapture: AnsightTouchCaptureOptions? = AnsightTouchCaptureOptions(),
    val toolGuard: AnsightToolGuard = AnsightToolGuard.Disabled,
    val customProperties: Map<String, Map<String, String>> = emptyMap(),
    val hostAutoProbe: AnsightHostAutoProbeOptions = AnsightHostAutoProbeOptions(),
    val hostConnection: AnsightHostConnectionOptions = AnsightHostConnectionOptions(),
    val secureStorage: AnsightSecureStorageOptions = AnsightSecureStorageOptions(),
    val initialTools: List<AndroidTool> = emptyList(),
) {
    val maximumBufferSize: Int
        get() = retentionPeriodSeconds * ceil(1000.0 / sampleFrequencyMilliseconds.toDouble()).toInt()

    fun validated(): AnsightOptions {
        val validatedChannels = additionalChannels.map { channel ->
            val validated = channel.validated()
            require(validated.id !in AnsightChannels.reservedIds) {
                "Additional channel '${validated.name}' uses reserved channel id ${validated.id}."
            }
            validated
        }

        return copy(
            sampleFrequencyMilliseconds = sampleFrequencyMilliseconds.coerceIn(
                AnsightSamplingLimits.MinSampleFrequencyMilliseconds,
                AnsightSamplingLimits.MaxSampleFrequencyMilliseconds,
            ),
            retentionPeriodSeconds = retentionPeriodSeconds.coerceIn(
                AnsightSamplingLimits.MinRetentionPeriodSeconds,
                AnsightSamplingLimits.MaxRetentionPeriodSeconds,
            ),
            additionalChannels = validatedChannels,
            sessionJpegCapture = sessionJpegCapture?.validated(),
            touchCapture = touchCapture?.validated(),
            customProperties = customProperties.normalizedCustomProperties(),
            hostAutoProbe = hostAutoProbe.validated(),
            hostConnection = hostConnection.validated(),
            secureStorage = secureStorage.validated(),
        )
    }
}

enum class AnsightEventType(val wireName: String) {
    Event("Event"),
    Debug("Debug"),
    Info("Info"),
    Warning("Warning"),
    Error("Error"),
    Exception("Exception"),
    Gc("Gc"),
    Navigation("Navigation"),
    ScreenViewed("ScreenViewed"),
    Lifecycle("Lifecycle"),
}

enum class AppLifecycleState(val wireName: String) {
    Unknown("unknown"),
    Foreground("foreground"),
    Background("background"),
}

enum class HostConnectionRequestKind {
    Auto,
    SavedConfig,
    BundledConfig,
    File,
    QrCode,
    Payload,
    Config,
}

enum class HostConnectionSource {
    None,
    AutoProbe,
    CachedSession,
    SavedConfig,
    BundledDeveloperConfig,
    BundledConfig,
    Payload,
    ConfigReader,
    HostConnection,
    Transport,
    Telemetry,
    AppState,
    SessionJpegCapture,
    TouchCapture,
}

enum class HostConnectionState {
    Disconnected,
    Connecting,
    Connected,
    Disconnecting,
    Failed,
    Unavailable,
}

enum class HostConnectionSummaryKind {
    None,
    Ready,
    Connected,
    Disconnected,
    Failed,
    Unavailable,
}

enum class HostConnectionActionKind {
    None,
    Connect,
    Disconnect,
    ClearSavedConfig,
}

data class HostConnectionRequest(
    val kind: HostConnectionRequestKind = HostConnectionRequestKind.Auto,
    val payload: String? = null,
    val clientName: String? = null,
    val expectedAppId: String? = null,
    val hostAddressOverride: String? = null,
)

data class HostConnectionStatus(
    val isRuntimeActive: Boolean,
    val isConnected: Boolean,
    val connectionState: HostConnectionState,
    val hasCachedSession: Boolean,
    val hasSavedConfig: Boolean,
    val hasBundledConfig: Boolean,
    val summaryKind: HostConnectionSummaryKind,
    val summaryMessage: String,
)

data class HostConnectionResult(
    val success: Boolean,
    val message: String,
    val kind: HostConnectionActionKind = HostConnectionActionKind.None,
    val source: HostConnectionSource = HostConnectionSource.None,
    val reasonCode: String? = null,
    val openSession: OpenSessionResult? = null,
) {
    companion object {
        fun success(
            message: String,
            kind: HostConnectionActionKind = HostConnectionActionKind.None,
            source: HostConnectionSource = HostConnectionSource.None,
            openSession: OpenSessionResult? = null,
        ) = HostConnectionResult(true, message, kind, source, openSession = openSession)

        fun failure(
            message: String,
            kind: HostConnectionActionKind = HostConnectionActionKind.None,
            source: HostConnectionSource = HostConnectionSource.None,
            reasonCode: String? = null,
            openSession: OpenSessionResult? = null,
        ) = HostConnectionResult(false, message, kind, source, reasonCode, openSession)
    }
}

data class PairingOpenOptions(
    val clientName: String,
    val expectedAppId: String? = null,
    val hostAddressOverride: String? = null,
    val profileOverride: Map<String, String> = emptyMap(),
)

data class OpenSessionResult(
    val success: Boolean,
    val message: String,
    val accepted: Boolean = success,
    val sessionId: String? = null,
    val configId: String? = null,
    val appId: String? = null,
    val resolvedHostAddress: String? = null,
    val usedEmbeddedDeveloperPairing: Boolean = false,
    val discoverySource: String? = null,
    val reasonCode: String? = null,
    val hostId: String? = null,
    val hostName: String? = null,
)

data class OperationResult(
    val success: Boolean,
    val message: String,
) {
    companion object {
        fun success(message: String) = OperationResult(true, message)
        fun failure(message: String) = OperationResult(false, message)
    }
}

data class AnsightToolDescriptor(
    val id: String,
    val name: String,
    val scope: String = "Read",
)

data class RecordedMetric(
    val value: Long,
    val channel: Int,
    val capturedAtUtc: String = AnsightClock.isoNow(),
    val capturedAtEpochMs: Long = System.currentTimeMillis(),
    val sequence: Long = 0,
)

data class RecordedEvent(
    val id: String = UUID.randomUUID().toString(),
    val label: String,
    val type: AnsightEventType,
    val details: String?,
    val channel: Int,
    val capturedAtUtc: String = AnsightClock.isoNow(),
    val capturedAtEpochMs: Long = System.currentTimeMillis(),
    val externalId: String? = null,
    val sequence: Long = 0,
)

data class RecordedScreenView(
    val name: String,
    val details: Map<String, String> = emptyMap(),
    val capturedAtUtc: String = AnsightClock.isoNow(),
)

data class RecordedTouch(
    val id: String,
    val action: String,
    val pointerId: Long,
    val pointerIndex: Int,
    val pointerCount: Int,
    val x: Double,
    val y: Double,
    val surfaceWidth: Double?,
    val surfaceHeight: Double?,
    val coordinateUnit: String,
    val surfaceScale: Double?,
    val normalizedX: Double?,
    val normalizedY: Double?,
    val capturedAtUtc: String,
    val capturedAtEpochMs: Long,
    val sequence: Long,
)

data class AnsightDebugSnapshot(
    val initialized: Boolean,
    val active: Boolean,
    val sessionOpen: Boolean,
    val connectionStatus: HostConnectionStatus,
    val lifecycleState: AppLifecycleState,
    val lifecycleChangedAtUtc: String?,
    val currentScreen: RecordedScreenView?,
    val metricsRecorded: Int,
    val eventsRecorded: Int,
    val touchesRecorded: Int,
    val channels: List<AnsightChannel>,
    val registeredTools: Int,
    val lastMetric: RecordedMetric?,
    val lastEvent: RecordedEvent?,
    val deviceProfile: DeviceAppProfile?,
    val sessionMessage: String?,
)

object PairingFailureCodes {
    const val HostAddressRequired = "HostAddressRequired"
    const val WifiRequired = "WifiRequired"
    const val PairingRequired = "PairingRequired"
    const val PairingTokenInvalid = "PairingTokenInvalid"
    const val PairingTokenExpired = "PairingTokenExpired"
    const val PairingProofInvalid = "PairingProofInvalid"
    const val SignInRequired = "SignInRequired"
    const val UdpBootstrapFailed = "UdpBootstrapFailed"
    const val UdpBootstrapTimeout = "UdpBootstrapTimeout"
    const val WebSocketHandoffUnavailable = "WebSocketHandoffUnavailable"
    const val WebSocketEndpointUnreachable = "WebSocketEndpointUnreachable"
    const val WebSocketHandshakeFailed = "WebSocketHandshakeFailed"
}
