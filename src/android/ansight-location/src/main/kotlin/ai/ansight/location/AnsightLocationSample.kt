package ai.ansight.location

import java.util.UUID

data class AnsightLocationSample(
    val latitude: Double,
    val longitude: Double,
    val altitudeMeters: Double? = null,
    val horizontalAccuracyMeters: Double? = null,
    val verticalAccuracyMeters: Double? = null,
    val speedMetersPerSecond: Double? = null,
    val headingDegrees: Double? = null,
    val capturedAtEpochMilliseconds: Long = System.currentTimeMillis(),
    val sampleId: String = UUID.randomUUID().toString(),
    val correlationId: String? = null,
    val runId: String? = null,
)
