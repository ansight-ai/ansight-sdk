package ai.ansight.location

import ai.ansight.runtime.AnsightRuntime
import ai.ansight.runtime.OperationResult
import android.location.Location
import org.json.JSONObject
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.TimeZone
import kotlin.math.asin
import kotlin.math.cos
import kotlin.math.pow
import kotlin.math.round
import kotlin.math.sin
import kotlin.math.sqrt

class AnsightLocationRecorder @JvmOverloads constructor(
    options: AnsightLocationOptions = AnsightLocationOptions(),
) {
    private val lock = Any()
    private val options = options.normalized()
    private var lastEmittedSample: AnsightLocationSample? = null

    fun record(sample: AnsightLocationSample): OperationResult {
        if (!options.enabled) {
            return OperationResult.failure("Observed location capture is disabled.")
        }
        if (!sample.latitude.isFinite() || sample.latitude !in -90.0..90.0 ||
            !sample.longitude.isFinite() || sample.longitude !in -180.0..180.0
        ) {
            return OperationResult.failure("Observed location coordinates are invalid.")
        }

        val normalized = normalize(sample)
        synchronized(lock) {
            if (shouldSuppress(normalized)) {
                return OperationResult.success("Observed location suppressed by sampling controls.")
            }
            lastEmittedSample = normalized
        }

        val payload = JSONObject()
            .put("schema", Schema)
            .put("sampleId", normalized.sampleId)
            .put("capturedAtUtc", timestamp(normalized.capturedAtEpochMilliseconds))
            .put("source", "app_observed")
            .put("latitude", normalized.latitude)
            .put("longitude", normalized.longitude)
        putOptional(payload, "altitudeMeters", normalized.altitudeMeters)
        putOptional(payload, "horizontalAccuracyMeters", normalized.horizontalAccuracyMeters)
        putOptional(payload, "verticalAccuracyMeters", normalized.verticalAccuracyMeters)
        putOptional(payload, "speedMetersPerSecond", normalized.speedMetersPerSecond)
        putOptional(payload, "headingDegrees", normalized.headingDegrees)
        putOptional(payload, "correlationId", normalized.correlationId)
        putOptional(payload, "runId", normalized.runId)
        return AnsightRuntime.sendSessionEvent(EventType, payload)
    }

    @JvmOverloads
    fun record(
        location: Location,
        correlationId: String? = null,
        runId: String? = null,
    ): OperationResult = record(AnsightLocationSample(
        latitude = location.latitude,
        longitude = location.longitude,
        altitudeMeters = location.altitude.takeIf { location.hasAltitude() },
        horizontalAccuracyMeters = location.accuracy.toDouble().takeIf { location.hasAccuracy() },
        verticalAccuracyMeters = if (android.os.Build.VERSION.SDK_INT >= 26 && location.hasVerticalAccuracy()) {
            location.verticalAccuracyMeters.toDouble()
        } else {
            null
        },
        speedMetersPerSecond = location.speed.toDouble().takeIf { location.hasSpeed() },
        headingDegrees = location.bearing.toDouble().takeIf { location.hasBearing() },
        capturedAtEpochMilliseconds = location.time,
        correlationId = correlationId,
        runId = runId,
    ))

    private fun normalize(sample: AnsightLocationSample): AnsightLocationSample = sample.copy(
        latitude = rounded(sample.latitude),
        longitude = rounded(sample.longitude),
        altitudeMeters = finite(sample.altitudeMeters),
        horizontalAccuracyMeters = nonNegative(sample.horizontalAccuracyMeters),
        verticalAccuracyMeters = nonNegative(sample.verticalAccuracyMeters),
        speedMetersPerSecond = nonNegative(sample.speedMetersPerSecond),
        headingDegrees = finite(sample.headingDegrees),
        sampleId = sample.sampleId.trim().ifEmpty { java.util.UUID.randomUUID().toString() },
        correlationId = sample.correlationId?.trim()?.ifEmpty { null },
        runId = sample.runId?.trim()?.ifEmpty { null },
    )

    private fun shouldSuppress(sample: AnsightLocationSample): Boolean {
        val previous = lastEmittedSample ?: return false
        return sample.capturedAtEpochMilliseconds - previous.capturedAtEpochMilliseconds < options.minimumIntervalMilliseconds ||
            distanceMeters(previous, sample) < options.minimumDistanceMeters
    }

    private fun rounded(value: Double): Double {
        val scale = 10.0.pow(options.decimalPlaces)
        return round(value * scale) / scale
    }

    private fun distanceMeters(first: AnsightLocationSample, second: AnsightLocationSample): Double {
        val radius = 6_371_000.0
        val latitudeDelta = Math.toRadians(second.latitude - first.latitude)
        val longitudeDelta = Math.toRadians(second.longitude - first.longitude)
        val firstLatitude = Math.toRadians(first.latitude)
        val secondLatitude = Math.toRadians(second.latitude)
        val haversine = sin(latitudeDelta / 2).pow(2) +
            cos(firstLatitude) * cos(secondLatitude) * sin(longitudeDelta / 2).pow(2)
        return 2 * radius * asin(sqrt(haversine))
    }

    private fun timestamp(epochMilliseconds: Long): String = SimpleDateFormat(
        "yyyy-MM-dd'T'HH:mm:ss.SSS'Z'",
        Locale.US,
    ).apply { timeZone = TimeZone.getTimeZone("UTC") }.format(Date(epochMilliseconds))

    private fun finite(value: Double?): Double? = value?.takeIf { it.isFinite() }

    private fun nonNegative(value: Double?): Double? = value?.takeIf { it.isFinite() && it >= 0 }

    private fun putOptional(payload: JSONObject, key: String, value: Any?) {
        if (value != null) payload.put(key, value)
    }

    companion object {
        const val EventType = "CLIENT_LOCATION"
        const val Schema = "ansight.location.sample.v1"
    }
}
