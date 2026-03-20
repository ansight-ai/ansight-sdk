package ai.ansight.reactnative

import ai.ansight.runtime.AnsightEventType
import ai.ansight.runtime.AnsightOptions
import ai.ansight.runtime.AnsightRuntime
import ai.ansight.runtime.AnsightToolDescriptor
import ai.ansight.runtime.PairingOpenOptions
import com.facebook.react.bridge.Arguments
import com.facebook.react.bridge.Promise
import com.facebook.react.bridge.ReactApplicationContext
import com.facebook.react.bridge.ReactContextBaseJavaModule
import com.facebook.react.bridge.ReactMethod
import com.facebook.react.bridge.ReadableArray
import com.facebook.react.bridge.ReadableMap

class AnsightBridgeModule(
    reactContext: ReactApplicationContext,
) : ReactContextBaseJavaModule(reactContext) {
    override fun getName(): String = "AnsightBridgeModule"

    @ReactMethod
    fun initialize(options: ReadableMap?, promise: Promise) {
        runCatching {
            val application = reactApplicationContext.applicationContext
            require(application is android.app.Application) {
                "React application context must be backed by an Application instance."
            }

            AnsightRuntime.initialize(application, options.toOptions())
            promise.resolve(null)
        }.onFailure { promise.reject("ansight_initialize_failed", it) }
    }

    @ReactMethod
    fun activate(promise: Promise) {
        runCatching {
            AnsightRuntime.activate()
            promise.resolve(null)
        }.onFailure { promise.reject("ansight_activate_failed", it) }
    }

    @ReactMethod
    fun deactivate(promise: Promise) {
        AnsightRuntime.deactivate()
        promise.resolve(null)
    }

    @ReactMethod
    fun clear(promise: Promise) {
        AnsightRuntime.clear()
        promise.resolve(null)
    }

    @ReactMethod
    fun metric(value: String, channel: Double?, promise: Promise) {
        runCatching {
            AnsightRuntime.metric(value.toLong(), (channel ?: 255.0).toInt())
            promise.resolve(null)
        }.onFailure { promise.reject("ansight_metric_failed", it) }
    }

    @ReactMethod
    fun event(label: String, options: ReadableMap?, promise: Promise) {
        runCatching {
            AnsightRuntime.event(
                label = label,
                type = options?.getString("type")?.let(AnsightEventType::valueOf) ?: AnsightEventType.Info,
                details = options?.getString("details"),
                channel = options?.getDouble("channel")?.toInt() ?: 255,
                id = options?.getString("id") ?: java.util.UUID.randomUUID().toString(),
            )
            promise.resolve(null)
        }.onFailure { promise.reject("ansight_event_failed", it) }
    }

    @ReactMethod
    fun openSession(pairingJson: String, options: ReadableMap, promise: Promise) {
        runCatching {
            val result = AnsightRuntime.openSession(
                pairingJson = pairingJson,
                options = PairingOpenOptions(
                    clientName = options.getString("clientName").orEmpty(),
                    manualHostAddress = options.getString("manualHostAddress").orEmpty(),
                    expectedAppId = options.getString("expectedAppId"),
                    profileOverride = options.getMap("profileOverride").toStringMap(),
                ),
            )

            val map = Arguments.createMap().apply {
                putBoolean("success", result.success)
                putString("message", result.message)
                putString("sessionId", result.sessionId)
            }
            promise.resolve(map)
        }.onFailure { promise.reject("ansight_open_session_failed", it) }
    }

    @ReactMethod
    fun completeSession(promise: Promise) {
        AnsightRuntime.completeSession()
        promise.resolve(null)
    }

    @ReactMethod
    fun closeSession(promise: Promise) {
        AnsightRuntime.closeSession()
        promise.resolve(null)
    }

    @ReactMethod
    fun registerTool(tool: ReadableMap, promise: Promise) {
        runCatching {
            AnsightRuntime.registerTool(
                AnsightToolDescriptor(
                    id = tool.getString("id").orEmpty(),
                    name = tool.getString("name").orEmpty(),
                    scope = tool.getString("scope") ?: "Read",
                ),
            )
            promise.resolve(null)
        }.onFailure { promise.reject("ansight_register_tool_failed", it) }
    }

    @ReactMethod
    fun getDebugSnapshot(promise: Promise) {
        val snapshot = AnsightRuntime.snapshot()
        val map = Arguments.createMap().apply {
            putBoolean("initialized", snapshot.initialized)
            putBoolean("active", snapshot.active)
            putBoolean("sessionOpen", snapshot.sessionOpen)
            putInt("metricsRecorded", snapshot.metricsRecorded)
            putInt("eventsRecorded", snapshot.eventsRecorded)
            putInt("registeredTools", snapshot.registeredTools)
            putString("sessionMessage", snapshot.sessionMessage)
            snapshot.lastMetric?.let {
                putMap("lastMetric", Arguments.createMap().apply {
                    putDouble("value", it.value.toDouble())
                    putInt("channel", it.channel)
                    putDouble("capturedAtEpochMs", it.capturedAtEpochMs.toDouble())
                })
            }
            snapshot.lastEvent?.let {
                putMap("lastEvent", Arguments.createMap().apply {
                    putString("id", it.id)
                    putString("label", it.label)
                    putString("type", it.type.name)
                    putString("details", it.details)
                    putInt("channel", it.channel)
                    putDouble("capturedAtEpochMs", it.capturedAtEpochMs.toDouble())
                })
            }
        }
        promise.resolve(map)
    }

    private fun ReadableMap?.toOptions(): AnsightOptions {
        if (this == null) {
            return AnsightOptions()
        }

        return AnsightOptions(
            sampleFrequencyMilliseconds = getIntOrDefault("sampleFrequencyMilliseconds", 500),
            retentionPeriodSeconds = getIntOrDefault("retentionPeriodSeconds", 600),
            enableFramesPerSecond = if (hasKey("enableFramesPerSecond")) getBoolean("enableFramesPerSecond") else true,
            additionalChannels = getArray("additionalChannels").toChannels(),
        )
    }

    private fun ReadableMap.getIntOrDefault(key: String, defaultValue: Int): Int {
        return if (hasKey(key)) getDouble(key).toInt() else defaultValue
    }

    private fun ReadableArray?.toChannels(): List<ai.ansight.runtime.AnsightChannel> {
        if (this == null) {
            return emptyList()
        }

        return (0 until size()).mapNotNull { index ->
            getMap(index)?.let {
                ai.ansight.runtime.AnsightChannel(
                    id = it.getDouble("id").toInt(),
                    name = it.getString("name").orEmpty(),
                    colorHex = it.getString("colorHex"),
                )
            }
        }
    }

    private fun ReadableMap?.toStringMap(): Map<String, String> {
        if (this == null) {
            return emptyMap()
        }

        val map = linkedMapOf<String, String>()
        val iterator = keySetIterator()
        while (iterator.hasNextKey()) {
            val key = iterator.nextKey()
            map[key] = getDynamic(key).asString()
        }
        return map
    }
}
