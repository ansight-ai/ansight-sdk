package ai.ansight.runtime

enum class AnsightLogLevel {
    Debug,
    Info,
    Warning,
    Error,
}

fun interface AnsightLogCallback {
    fun log(level: AnsightLogLevel, message: String, throwable: Throwable?)
}

object AnsightLogger {
    private val lock = Any()
    private val callbacks = mutableListOf<AnsightLogCallback>()

    @JvmStatic
    fun registerCallback(callback: AnsightLogCallback) {
        synchronized(lock) {
            if (callbacks.none { it === callback }) {
                callbacks += callback
            }
        }
    }

    @JvmStatic
    fun removeCallback(callback: AnsightLogCallback) {
        synchronized(lock) {
            callbacks.removeAll { it === callback }
        }
    }

    @JvmStatic
    fun clearCallbacks() {
        synchronized(lock) {
            callbacks.clear()
        }
    }

    @JvmStatic
    fun debug(message: String) {
        emit(AnsightLogLevel.Debug, message)
    }

    @JvmStatic
    fun info(message: String) {
        emit(AnsightLogLevel.Info, message)
    }

    @JvmStatic
    fun warning(message: String, throwable: Throwable? = null) {
        emit(AnsightLogLevel.Warning, message, throwable)
    }

    @JvmStatic
    fun error(message: String, throwable: Throwable? = null) {
        emit(AnsightLogLevel.Error, message, throwable)
    }

    private fun emit(level: AnsightLogLevel, message: String, throwable: Throwable? = null) {
        val normalized = message.trim().ifBlank { return }
        val snapshot = synchronized(lock) { callbacks.toList() }
        snapshot.forEach { callback ->
            runCatching { callback.log(level, normalized, throwable) }
        }
    }
}
