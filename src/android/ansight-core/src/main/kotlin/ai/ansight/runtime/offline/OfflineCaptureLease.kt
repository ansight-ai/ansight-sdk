package ai.ansight.runtime

import java.io.File

/** Pins a sealed session until release or a verified host import acknowledgement. */
data class OfflineCaptureLease internal constructor(
    val sessionId: String,
    val token: String,
    val directory: File,
)
