package ai.ansight.runtime

import android.os.Build
import org.json.JSONObject
import java.io.File

internal object AndroidCrashSignalBridge {
    private val loaded = Build.VERSION.SDK_INT >= Build.VERSION_CODES.LOLLIPOP && runCatching {
        System.loadLibrary("ansight-crash-handler")
        true
    }.getOrDefault(false)
    private var installed = false

    fun consume(file: File): AndroidCrashSignalEvidence? {
        if (!loaded) return null
        val values = runCatching { nativeConsume(file.absolutePath) }.getOrNull() ?: return null
        if (values.size != 6 || values[0] != 1L) return null
        return AndroidCrashSignalEvidence(
            signalNumber = values[1].toInt(),
            signalCode = values[2].toInt(),
            faultAddress = values[3],
            occurredAtEpochMs = values[4] * 1_000,
            processId = values[5].toInt(),
        )
    }

    fun install(file: File) {
        if (!loaded || installed) return
        installed = runCatching { nativeInstall(file.absolutePath) }.getOrDefault(false)
    }

    private external fun nativeInstall(path: String): Boolean

    private external fun nativeConsume(path: String): LongArray?
}

internal data class AndroidCrashSignalEvidence(
    val signalNumber: Int,
    val signalCode: Int,
    val faultAddress: Long,
    val occurredAtEpochMs: Long,
    val processId: Int,
) {
    val kind: String = when (signalNumber) {
        6 -> "signal_sigabrt"
        7 -> "signal_sigbus"
        8 -> "signal_sigfpe"
        4 -> "signal_sigill"
        11 -> "signal_sigsegv"
        5 -> "signal_sigtrap"
        else -> "signal"
    }

    fun toJson(): JSONObject = JSONObject()
        .put("reason", kind)
        .put("signalNumber", signalNumber)
        .put("signalCode", signalCode)
        .put("faultAddress", "0x${faultAddress.toULong().toString(16)}")
        .put("processId", processId)
}
