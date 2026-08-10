package ai.ansight.runtime

import org.json.JSONObject

object SessionVisualTreeCaptureRegistry {
    private val lock = Any()
    private var provider: (AndroidToolExecutionContext) -> List<JSONObject> = {
        listOf(AndroidUiEvidence.visualTree())
    }

    @JvmStatic
    fun setProvider(provider: ((AndroidToolExecutionContext) -> List<JSONObject>)?) {
        synchronized(lock) {
            this.provider = provider ?: { listOf(AndroidUiEvidence.visualTree()) }
        }
    }

    internal fun capture(context: AndroidToolExecutionContext): List<JSONObject> {
        val current = synchronized(lock) { provider }
        return current(context)
    }
}
