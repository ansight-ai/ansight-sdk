package ai.ansight.runtime

internal object AnsightSessionConfigurationProperties {
    const val CaptureGroup = "ansight.capture"
    const val TelemetryGroup = "ansight.telemetry"

    fun create(
        options: AnsightOptions,
        capturePolicy: HostSessionJpegCapturePolicy,
    ): Map<String, Map<String, String>> {
        val properties = LinkedHashMap(options.customProperties.normalizedCustomProperties())
        properties[CaptureGroup] = captureProperties(options.sessionJpegCapture, capturePolicy)
        properties[TelemetryGroup] = linkedMapOf(
            "schemaVersion" to "1",
            "sampleFrequencyMilliseconds" to options.sampleFrequencyMilliseconds.toString(),
            "retentionPeriodSeconds" to options.retentionPeriodSeconds.toString(),
            "framesPerSecond" to options.enableFramesPerSecond.toString(),
            "batteryLevel" to options.enableBatteryLevel.toString(),
            "openFileHandles" to options.enableOpenFileHandleTracking.toString(),
            "jniReferenceCount" to options.enableJniReferenceCountTracking.toString(),
        )
        return properties
    }

    private fun captureProperties(
        options: AnsightSessionJpegCaptureOptions?,
        capturePolicy: HostSessionJpegCapturePolicy,
    ): Map<String, String> {
        val properties = linkedMapOf(
            "schemaVersion" to "1",
            "enabled" to (options != null || capturePolicy.useHostCapture).toString(),
            "owner" to when {
                capturePolicy.useHostCapture -> "host"
                options == null -> "none"
                else -> "app"
            },
        )

        if (options != null) {
            properties["intervalMilliseconds"] = options.intervalMilliseconds.toString()
            properties["quality"] = options.quality.toString()
            properties["maxWidth"] = options.maxWidth?.toString() ?: "full"
            properties["captureGpuBackedSurfaces"] = options.captureGpuBackedSurfaces.toString()
            properties["captureKeyboardPresence"] = options.captureKeyboardPresence.toString()
            properties["mode"] = options.mode.wireName()
        }
        capturePolicy.source?.let { properties["ownerSource"] = it }
        return properties
    }

    private fun AnsightSessionJpegCaptureMode.wireName(): String = when (this) {
        AnsightSessionJpegCaptureMode.ScreenshotOnly -> "screenshotOnly"
        AnsightSessionJpegCaptureMode.ScreenshotAndVisualTree -> "screenshotAndVisualTree"
        AnsightSessionJpegCaptureMode.ScreenshotWithVisualTreeOnTouch -> "screenshotWithVisualTreeOnTouch"
    }
}
