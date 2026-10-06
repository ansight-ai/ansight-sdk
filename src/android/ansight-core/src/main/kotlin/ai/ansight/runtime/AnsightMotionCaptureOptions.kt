package ai.ansight.runtime

/** App-fed motion evidence; the SDK does not register a sensor listener. */
data class AnsightMotionCaptureOptions(
    val captureShake: Boolean = true,
    val captureAccelerometer: Boolean = true,
    val minimumSampleIntervalMilliseconds: Int = 20,
) {
    fun validated(): AnsightMotionCaptureOptions = copy(
        minimumSampleIntervalMilliseconds = minimumSampleIntervalMilliseconds.coerceIn(10, 1_000),
    )
}
