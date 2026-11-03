package ai.ansight.runtime

data class OfflineCaptureSession(
    val sessionId: String,
    val startedAtUtc: String,
    val ordinal: Long,
    val stoppedAtUtc: String?,
    val terminationKind: String?,
    val appId: String,
    val processSessionId: String,
    val bytes: Long,
    val records: Long,
    val droppedRecords: Long,
    val captureId: String? = null,
    val captureStartedAtUtc: String? = null,
    val sequence: Int? = null,
)
