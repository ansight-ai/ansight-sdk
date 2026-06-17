package ai.ansight.reactnative

import ai.ansight.runtime.AnsightChannel
import ai.ansight.runtime.AnsightMetricSampler
import ai.ansight.runtime.AnsightMetricStream
import ai.ansight.runtime.AnsightRuntime
import com.facebook.react.bridge.ReactApplicationContext
import com.facebook.react.bridge.ReadableMap
import com.facebook.react.bridge.ReadableType
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicLong

internal data class ReactNativeMemoryProfilingOptions(
    val enabled: Boolean = true,
    val jsHeapUsed: Boolean = true,
    val jsHeapTotal: Boolean = true,
) {
    fun toMap(): Map<String, Any> = mapOf(
        "enabled" to enabled,
        "jsHeapUsed" to jsHeapUsed,
        "jsHeapTotal" to jsHeapTotal,
    )

    companion object {
        val Defaults = ReactNativeMemoryProfilingOptions()
        val Disabled = ReactNativeMemoryProfilingOptions(enabled = false, jsHeapUsed = false, jsHeapTotal = false)
    }
}

internal object ReactNativeMemoryChannels {
    const val JsHeapUsed = 32
    const val JsHeapTotal = 33

    val jsHeapUsedChannel = AnsightChannel(
        id = JsHeapUsed,
        name = "React Native JS heap used",
        colorHex = "#61DAFB",
        unit = "bytes",
        type = "memory",
        source = "reactNative",
        group = "React Native",
        kind = "react_native_js_heap_used",
    )

    val jsHeapTotalChannel = AnsightChannel(
        id = JsHeapTotal,
        name = "React Native JS heap total",
        colorHex = "#0A84FF",
        unit = "bytes",
        type = "memory",
        source = "reactNative",
        group = "React Native",
        kind = "react_native_js_heap_total",
    )
}

internal class ReactNativeMemoryProfiler(
    private val reactContext: ReactApplicationContext,
) {
    private val hasSample = AtomicBoolean(false)
    private val refreshScheduled = AtomicBoolean(false)
    private val jsHeapUsedBytes = AtomicLong(0L)
    private val jsHeapTotalBytes = AtomicLong(0L)

    fun register(options: ReactNativeMemoryProfilingOptions) {
        if (!options.enabled || !ReactNativeMemoryNative.isAvailable) {
            return
        }

        if (options.jsHeapUsed) {
            AnsightRuntime.registerMetricStream(
                AnsightMetricStream(ReactNativeMemoryChannels.jsHeapUsedChannel, AnsightMetricSampler { sampleJsHeapUsedBytes() }),
            )
        }
        if (options.jsHeapTotal) {
            AnsightRuntime.registerMetricStream(
                AnsightMetricStream(ReactNativeMemoryChannels.jsHeapTotalChannel, AnsightMetricSampler { sampleJsHeapTotalBytes() }),
            )
        }
        requestRefresh()
    }

    private fun sampleJsHeapUsedBytes(): Long? {
        requestRefresh()
        return jsHeapUsedBytes.get().takeIf { hasSample.get() && it > 0L }
    }

    private fun sampleJsHeapTotalBytes(): Long? {
        requestRefresh()
        return jsHeapTotalBytes.get().takeIf { hasSample.get() && it > 0L }
    }

    private fun requestRefresh() {
        if (!reactContext.hasActiveReactInstance() || !ReactNativeMemoryNative.isAvailable) {
            return
        }
        if (!refreshScheduled.compareAndSet(false, true)) {
            return
        }

        val queued = reactContext.runOnJSQueueThread {
            try {
                val holder = reactContext.javaScriptContextHolder ?: return@runOnJSQueueThread
                val runtimePointer = synchronized(holder) { holder.get() }
                if (runtimePointer == 0L) {
                    return@runOnJSQueueThread
                }

                val values = ReactNativeMemoryNative.readHeapInfo(runtimePointer) ?: return@runOnJSQueueThread
                if (values.size >= 2 && values[0] > 0L) {
                    jsHeapUsedBytes.set(values[0])
                    jsHeapTotalBytes.set(values[1].coerceAtLeast(values[0]))
                    hasSample.set(true)
                }
            } finally {
                refreshScheduled.set(false)
            }
        }

        if (!queued) {
            refreshScheduled.set(false)
        }
    }
}

internal object ReactNativeMemoryNative {
    val isAvailable: Boolean = runCatching {
        System.loadLibrary("ansightreactnativememory")
    }.isSuccess

    external fun readHeapInfo(runtimePointer: Long): LongArray?
}

internal fun reactNativeMemoryProfilingOptions(map: ReadableMap?): ReactNativeMemoryProfilingOptions {
    if (map?.hasKey("reactNativeMemory") == true) {
        if (map.isNull("reactNativeMemory")) {
            return ReactNativeMemoryProfilingOptions.Disabled
        }
        if (map.getType("reactNativeMemory") == ReadableType.Boolean) {
            return if (map.getBoolean("reactNativeMemory")) {
                ReactNativeMemoryProfilingOptions.Defaults
            } else {
                ReactNativeMemoryProfilingOptions.Disabled
            }
        }
    }

    val options = if (map?.hasKey("reactNativeMemory") == true &&
        !map.isNull("reactNativeMemory") &&
        map.getType("reactNativeMemory") == ReadableType.Map
    ) {
        map.getMap("reactNativeMemory")
    } else {
        null
    }

    val enabled = options?.booleanOption("enabled", true) ?: true
    val jsHeap = options?.booleanOption("jsHeap", true) ?: true
    return ReactNativeMemoryProfilingOptions(
        enabled = enabled,
        jsHeapUsed = options?.booleanOption("jsHeapUsed", jsHeap) ?: jsHeap,
        jsHeapTotal = options?.booleanOption("jsHeapTotal", jsHeap) ?: jsHeap,
    )
}

private fun ReadableMap.booleanOption(name: String, default: Boolean): Boolean =
    if (hasKey(name) && !isNull(name) && getType(name) == ReadableType.Boolean) getBoolean(name) else default
