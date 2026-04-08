package ai.ansight.runtime

data class AnsightOptions(
    val sampleFrequencyMilliseconds: Int = 500,
    val retentionPeriodSeconds: Int = 600,
    val enableFramesPerSecond: Boolean = true,
    val additionalChannels: List<AnsightChannel> = emptyList(),
)

data class AnsightChannel(
    val id: Int,
    val name: String,
    val colorHex: String? = null,
)

enum class AnsightEventType {
    Event,
    Debug,
    Info,
    Warning,
    Error,
    Exception,
    Gc,
    Navigation,
}

data class PairingOpenOptions(
    val clientName: String,
    val expectedAppId: String? = null,
    val profileOverride: Map<String, String> = emptyMap(),
)

data class OpenSessionResult(
    val success: Boolean,
    val message: String,
    val sessionId: String? = null,
)

data class AnsightToolDescriptor(
    val id: String,
    val name: String,
    val scope: String = "Read",
)

data class RecordedMetric(
    val value: Long,
    val channel: Int,
    val capturedAtEpochMs: Long,
)

data class RecordedEvent(
    val id: String,
    val label: String,
    val type: AnsightEventType,
    val details: String?,
    val channel: Int,
    val capturedAtEpochMs: Long,
)

data class AnsightDebugSnapshot(
    val initialized: Boolean,
    val active: Boolean,
    val sessionOpen: Boolean,
    val metricsRecorded: Int,
    val eventsRecorded: Int,
    val registeredTools: Int,
    val lastMetric: RecordedMetric?,
    val lastEvent: RecordedEvent?,
    val sessionMessage: String?,
)

object AnsightChannels {
    const val Unspecified = 255
}
