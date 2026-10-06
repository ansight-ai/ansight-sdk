package ai.ansight.runtime

import org.json.JSONObject

object SessionVisualTreeCaptureRegistry {
    private val lock = Any()
    private var provider: ((AndroidToolExecutionContext) -> List<JSONObject>)? = null

    @JvmStatic
    fun setProvider(provider: ((AndroidToolExecutionContext) -> List<JSONObject>)?) {
        synchronized(lock) {
            this.provider = provider
        }
    }

    internal fun capture(context: AndroidToolExecutionContext): List<JSONObject> {
        val current = synchronized(lock) { provider }
        return current?.invoke(context) ?: listOf(AndroidUiEvidence.visualTree())
    }

    internal fun captureForAnnotation(context: AndroidToolExecutionContext): List<JSONObject> {
        val current = synchronized(lock) { provider }
        return current?.invoke(context) ?: listOf(AndroidUiEvidence.visualTree(
            context.options.annotatedFeedback.visualTreeMaxDepth,
            context.options.annotatedFeedback.visualTreeMaxNodes,
        ))
    }
}
