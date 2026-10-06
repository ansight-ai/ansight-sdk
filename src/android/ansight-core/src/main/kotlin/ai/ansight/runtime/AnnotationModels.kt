package ai.ansight.runtime

data class AnnotationOptions(
    val enabled: Boolean = true,
    val captureScreenshot: Boolean = true,
    val captureVisualTrees: Boolean = true,
    val screenshotQuality: Int = 85,
    val screenshotMaxWidth: Int = 1440,
    val visualTreeMaxDepth: Int = 30,
    val visualTreeMaxNodes: Int = 1500,
) {
    fun validated(): AnnotationOptions = copy(
        screenshotQuality = screenshotQuality.coerceIn(1, 100),
        screenshotMaxWidth = screenshotMaxWidth.coerceIn(1, 8192),
        visualTreeMaxDepth = visualTreeMaxDepth.coerceIn(1, 64),
        visualTreeMaxNodes = visualTreeMaxNodes.coerceIn(1, 100_000),
    )
}

enum class AnnotationCaptureStatus { Completed, Queued, Cancelled, Disabled, Unavailable, Failed }

data class AnnotationCaptureResult(
    val status: AnnotationCaptureStatus,
    val annotationId: String? = null,
    val message: String? = null,
) {
    val isSuccess: Boolean get() = status == AnnotationCaptureStatus.Completed || status == AnnotationCaptureStatus.Queued
}
