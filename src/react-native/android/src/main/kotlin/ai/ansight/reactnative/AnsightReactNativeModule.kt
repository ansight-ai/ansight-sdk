package ai.ansight.reactnative

import ai.ansight.Ansight
import ai.ansight.runtime.AnsightDeveloperMode
import ai.ansight.runtime.AndroidToolExecutionContext
import ai.ansight.runtime.AndroidToolResult
import ai.ansight.runtime.AnsightChannel
import ai.ansight.runtime.AnsightChannels
import ai.ansight.runtime.AnsightHostAutoProbeOptions
import ai.ansight.runtime.AnsightHostConnectionOptions
import ai.ansight.runtime.AnsightLogCallback
import ai.ansight.runtime.AnsightLogLevel
import ai.ansight.runtime.AnsightLogger
import ai.ansight.runtime.AnsightOptions
import ai.ansight.runtime.AnsightOptionsBuilder
import ai.ansight.runtime.AnsightRuntime
import ai.ansight.runtime.AnsightSecureStorageOptions
import ai.ansight.runtime.AnsightSessionJpegCaptureOptions
import ai.ansight.runtime.AnsightToolGuard
import ai.ansight.runtime.AnsightTouchCaptureOptions
import ai.ansight.runtime.AppLifecycleState
import ai.ansight.runtime.DefaultMemoryChannels
import ai.ansight.runtime.FunctionAndroidTool
import ai.ansight.runtime.HostConnectionRequest
import ai.ansight.runtime.HostConnectionRequestKind
import ai.ansight.runtime.HostConnectionCapabilities
import ai.ansight.runtime.HostConnectionResult
import ai.ansight.runtime.HostConnectionStatus
import ai.ansight.runtime.OperationResult
import ai.ansight.runtime.OpenSessionResult
import ai.ansight.runtime.PairingOpenOptions
import ai.ansight.runtime.PairingFileTransferWireProtocol
import ai.ansight.runtime.RecordedEvent
import ai.ansight.runtime.RecordedMetric
import ai.ansight.runtime.ToolDefinition
import ai.ansight.runtime.ToolSchema
import ai.ansight.runtime.ToolScope
import ai.ansight.runtime.ToolSecurity
import ai.ansight.runtime.ToolSecurityLevel
import ai.ansight.runtime.sendBinaryTransfer
import ai.ansight.pairing.AnsightPairing
import ai.ansight.tools.database.AndroidDatabaseRoot
import ai.ansight.tools.database.AndroidDatabaseToolsOptions
import ai.ansight.tools.database.withDatabaseTools
import ai.ansight.tools.filesystem.AndroidFileSystemRoot
import ai.ansight.tools.filesystem.AndroidFileSystemToolsOptions
import ai.ansight.tools.filesystem.withFileSystemTools
import ai.ansight.tools.preferences.AndroidPreferencesToolsOptions
import ai.ansight.tools.preferences.withPreferencesTools
import ai.ansight.tools.reflection.AndroidReflectionToolsOptions
import ai.ansight.tools.reflection.withReflectionTools
import ai.ansight.tools.securestorage.withSecureStorageTools
import ai.ansight.tools.visualtree.withVisualTreeTools
import android.app.Activity
import android.app.Application
import android.util.Base64
import com.facebook.react.bridge.Arguments
import com.facebook.react.bridge.LifecycleEventListener
import com.facebook.react.bridge.Promise
import com.facebook.react.bridge.ReactApplicationContext
import com.facebook.react.bridge.ReactContextBaseJavaModule
import com.facebook.react.bridge.ReactMethod
import com.facebook.react.bridge.ReadableArray
import com.facebook.react.bridge.ReadableMap
import com.facebook.react.bridge.ReadableType
import com.facebook.react.bridge.UiThreadUtil
import com.facebook.react.bridge.WritableArray
import com.facebook.react.bridge.WritableMap
import com.facebook.react.modules.core.DeviceEventManagerModule
import org.json.JSONArray
import org.json.JSONObject
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.CountDownLatch
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.atomic.AtomicBoolean
import java.util.Locale

class AnsightReactNativeModule(
    private val reactContext: ReactApplicationContext,
) : ReactContextBaseJavaModule(reactContext), LifecycleEventListener {
    private data class PendingToolCall(
        val context: AndroidToolExecutionContext,
        val latch: CountDownLatch = CountDownLatch(1),
        @Volatile var result: AndroidToolResult? = null,
    )

    private data class CustomToolRegistration(
        val definition: ToolDefinition,
        val timeoutMilliseconds: Long,
    )

    private val backgroundExecutor = Executors.newCachedThreadPool()
    private val pendingToolCalls = ConcurrentHashMap<String, PendingToolCall>()
    private val customToolRegistrations = ConcurrentHashMap<String, CustomToolRegistration>()
    private val activeCustomToolIds = ConcurrentHashMap.newKeySet<String>()
    private val listenerCount = AtomicInteger(0)
    private val reactNativeMemoryProfiler = ReactNativeMemoryProfiler(reactContext)
    private var currentReactNativeMemoryOptions = ReactNativeMemoryProfilingOptions.Defaults
    private val logCallback = AnsightLogCallback { level, message, throwable ->
        emitLogEvent(level, message, throwable)
    }

    init {
        reactContext.addLifecycleEventListener(this)
        AnsightLogger.registerCallback(logCallback)
    }

    override fun getName(): String = "AnsightReactNative"

    override fun invalidate() {
        AnsightLogger.removeCallback(logCallback)
        reactContext.removeLifecycleEventListener(this)
        super.invalidate()
    }

    override fun onHostResume() {
        bindCurrentActivity()
    }

    override fun onHostPause() = Unit

    override fun onHostDestroy() = Unit

    @ReactMethod
    fun addListener(eventName: String) {
        listenerCount.incrementAndGet()
    }

    @ReactMethod
    fun removeListeners(count: Int) {
        listenerCount.updateAndGet { current -> (current - count).coerceAtLeast(0) }
    }

    @ReactMethod
    fun initialize(options: ReadableMap?, promise: Promise) {
        runCatching {
            val runtimeOptions = buildOptions(options)
            Ansight.initialize(application(), runtimeOptions)
            configureReactNativeMemoryProfiling(options)
            installRegisteredCustomTools()
            snapshotMap()
        }.resolve(promise)
    }

    @ReactMethod
    fun initializeAndActivate(options: ReadableMap?, promise: Promise) {
        runCatching {
            val runtimeOptions = buildOptions(options)
            Ansight.initializeAndActivate(application(), runtimeOptions)
            configureReactNativeMemoryProfiling(options)
            bindCurrentActivity()
            installRegisteredCustomTools()
            snapshotMap()
        }.resolve(promise)
    }

    @ReactMethod
    fun activate(promise: Promise) {
        runCatching {
            AnsightRuntime.activate()
            bindCurrentActivity()
            snapshotMap()
        }.resolve(promise)
    }

    @ReactMethod
    fun deactivate(promise: Promise) {
        runCatching {
            AnsightRuntime.deactivate()
            snapshotMap()
        }.resolve(promise)
    }

    @ReactMethod
    fun clear(promise: Promise) {
        runCatching {
            AnsightRuntime.clear()
            snapshotMap()
        }.resolve(promise)
    }

    @ReactMethod
    fun registerMetricChannel(channel: ReadableMap, promise: Promise) {
        runCatching {
            AnsightRuntime.registerMetricChannel(
                AnsightChannel(
                    id = channel.intValue("id", -1),
                    name = channel.stringValue("name") ?: "",
                    unit = channel.stringValue("unit"),
                    type = channel.stringValue("type") ?: "custom",
                    colorHex = channel.stringValue("colorHex"),
                    source = channel.stringValue("source"),
                    group = channel.stringValue("group"),
                    kind = channel.stringValue("kind"),
                ),
            )
            snapshotMap()
        }.resolve(promise)
    }

    @ReactMethod
    fun recordMetric(value: Double, channel: Double, promise: Promise) {
        runCatching {
            val channelId = if (channel.isNaN()) AnsightChannels.Unspecified else channel.toInt()
            AnsightRuntime.metric(value.toLong(), channelId)
            snapshotMap()
        }.resolve(promise)
    }

    @ReactMethod
    fun recordEvent(input: ReadableMap, promise: Promise) {
        runCatching {
            val label = input.stringValue("label") ?: ""
            AnsightRuntime.event(
                label = label,
                type = eventType(input.stringValue("type")),
                details = input.stringValue("details"),
                channel = input.intValue("channel", AnsightChannels.Unspecified),
            )
            snapshotMap()
        }.resolve(promise)
    }

    @ReactMethod
    fun screenViewed(name: String, details: ReadableMap?, promise: Promise) {
        runCatching {
            AnsightRuntime.screenViewed(name, details.toStringMap())
            snapshotMap()
        }.resolve(promise)
    }

    @ReactMethod
    fun setAppLifecycleState(state: String, promise: Promise) {
        runCatching {
            AnsightRuntime.setAppLifecycleState(lifecycleState(state))
            snapshotMap()
        }.resolve(promise)
    }

    @ReactMethod
    fun connect(pairingPayload: String?, options: ReadableMap?, promise: Promise) {
        backgroundExecutor.execute {
            runCatching {
                val request = if (pairingPayload.isNullOrBlank()) {
                    HostConnectionRequest(
                        kind = HostConnectionRequestKind.Auto,
                        clientName = options.stringValue("clientName"),
                        expectedAppId = options.stringValue("expectedAppId"),
                        hostAddressOverride = options.stringValue("hostAddressOverride"),
                    )
                } else {
                    HostConnectionRequest(
                        kind = HostConnectionRequestKind.Payload,
                        payload = pairingPayload,
                        clientName = options.stringValue("clientName"),
                        expectedAppId = options.stringValue("expectedAppId"),
                        hostAddressOverride = options.stringValue("hostAddressOverride"),
                    )
                }
                hostConnectionResultMap(AnsightRuntime.connect(request))
            }.resolve(promise)
        }
    }

    @ReactMethod
    fun scanPairingQrCode(options: ReadableMap?, promise: Promise) {
        val activity = reactContext.currentActivity
        if (activity == null) {
            promise.reject("ansight_qr_unavailable", "QR pairing is unavailable because no Android activity is available.")
            return
        }

        val completed = AtomicBoolean(false)
        AnsightPairing.scanQrCode(
            activity = activity,
            onPayload = { payload ->
                if (!completed.compareAndSet(false, true)) {
                    return@scanQrCode
                }
                if (payload.isNullOrBlank()) {
                    promise.resolve(
                        hostConnectionResultMap(
                            HostConnectionResult.failure(
                                message = "QR pairing canceled.",
                                reasonCode = "pairing_canceled",
                            ),
                        ),
                    )
                    return@scanQrCode
                }
                backgroundExecutor.execute {
                    runCatching {
                        hostConnectionResultMap(
                            AnsightRuntime.connect(
                                HostConnectionRequest(
                                    kind = HostConnectionRequestKind.QrCode,
                                    payload = payload,
                                    clientName = options.stringValue("clientName"),
                                    expectedAppId = options.stringValue("expectedAppId"),
                                    hostAddressOverride = options.stringValue("hostAddressOverride"),
                                ),
                            ),
                        )
                    }.resolve(promise)
                }
            },
            onError = { error ->
                if (completed.compareAndSet(false, true)) {
                    promise.reject("ansight_qr_error", error.message, error)
                }
            },
        )
    }

    @ReactMethod
    fun openSession(pairingPayload: String?, options: ReadableMap?, promise: Promise) {
        backgroundExecutor.execute {
            runCatching {
                openSessionResultMap(
                    AnsightRuntime.openSession(
                        pairingPayload.orEmpty(),
                        pairingOpenOptions(options),
                    ),
                )
            }.resolve(promise)
        }
    }

    @ReactMethod
    fun disconnect(promise: Promise) {
        backgroundExecutor.execute {
            runCatching { hostConnectionResultMap(AnsightRuntime.disconnect()) }.resolve(promise)
        }
    }

    @ReactMethod
    fun completeSession(promise: Promise) {
        backgroundExecutor.execute {
            runCatching {
                AnsightRuntime.completeSession()
                operationResultMap(OperationResult.success("Session completed."))
            }.resolve(promise)
        }
    }

    @ReactMethod
    fun closeSession(promise: Promise) {
        backgroundExecutor.execute {
            runCatching {
                AnsightRuntime.closeSession()
                operationResultMap(OperationResult.success("Session closed."))
            }.resolve(promise)
        }
    }

    @ReactMethod
    fun savePairingConfig(pairingPayload: String?, options: ReadableMap?, promise: Promise) {
        backgroundExecutor.execute {
            runCatching {
                hostConnectionResultMap(
                    AnsightRuntime.savePairingConfig(
                        pairingPayload.orEmpty(),
                        options.stringValue("expectedAppId"),
                    ),
                )
            }.resolve(promise)
        }
    }

    @ReactMethod
    fun clearSavedPairing(promise: Promise) {
        runCatching { hostConnectionResultMap(AnsightRuntime.clearSavedPairingConfig()) }.resolve(promise)
    }

    @ReactMethod
    fun clearCachedSession(promise: Promise) {
        runCatching { operationResultMap(AnsightRuntime.clearCachedSession()) }.resolve(promise)
    }

    @ReactMethod
    fun notifyHostConnectionConfigChanged(promise: Promise) {
        runCatching { hostConnectionResultMap(AnsightRuntime.notifyHostConnectionConfigChanged()) }.resolve(promise)
    }

    @ReactMethod
    fun status(promise: Promise) {
        runCatching { snapshotMap() }.resolve(promise)
    }

    @ReactMethod
    fun snapshot(promise: Promise) {
        runCatching { snapshotMap() }.resolve(promise)
    }

    @ReactMethod
    fun hostConnectionStatus(promise: Promise) {
        runCatching { hostConnectionStatusMap(AnsightRuntime.hostConnectionStatus()) }.resolve(promise)
    }

    @ReactMethod
    fun hostConnectionCapabilities(promise: Promise) {
        runCatching { hostConnectionCapabilitiesMap(AnsightRuntime.hostConnectionCapabilities()) }.resolve(promise)
    }

    @ReactMethod
    fun currentOptions(promise: Promise) {
        runCatching { optionsMap(AnsightRuntime.options()) }.resolve(promise)
    }

    @ReactMethod
    fun recordedMetrics(limit: Double, promise: Promise) {
        runCatching {
            val metrics = AnsightRuntime.recordedMetrics()
            metricsArray(metrics.takeLast(limit.toInt().takeIf { it > 0 } ?: metrics.size))
        }.resolve(promise)
    }

    @ReactMethod
    fun recordedEvents(limit: Double, promise: Promise) {
        runCatching {
            val events = AnsightRuntime.recordedEvents()
            eventsArray(events.takeLast(limit.toInt().takeIf { it > 0 } ?: events.size))
        }.resolve(promise)
    }

    @ReactMethod
    fun sendClientLog(line: String, promise: Promise) {
        runCatching { operationResultMap(AnsightRuntime.sendClientLog(line)) }.resolve(promise)
    }

    @ReactMethod
    fun captureBuiltInTelemetrySample(promise: Promise) {
        runCatching {
            AnsightRuntime.captureBuiltInTelemetrySample()
            snapshotMap()
        }.resolve(promise)
    }

    @ReactMethod
    fun isFramesPerSecondEnabled(promise: Promise) {
        runCatching { AnsightRuntime.isFramesPerSecondEnabled() }.resolve(promise)
    }

    @ReactMethod
    fun enableFramesPerSecond(promise: Promise) {
        runCatching {
            AnsightRuntime.enableFramesPerSecond()
            snapshotMap()
        }.resolve(promise)
    }

    @ReactMethod
    fun disableFramesPerSecond(promise: Promise) {
        runCatching {
            AnsightRuntime.disableFramesPerSecond()
            snapshotMap()
        }.resolve(promise)
    }

    @ReactMethod
    fun captureScreenFrame(options: ReadableMap?, promise: Promise) {
        backgroundExecutor.execute {
            runCatching {
                bindCurrentActivity()
                operationResultMap(AnsightRuntime.captureScreenFrame(sessionJpegCaptureOptions(options)))
            }.resolve(promise)
        }
    }

    @ReactMethod
    fun enableTouchCapture(promise: Promise) {
        runCatching { operationResultMap(AnsightRuntime.enableTouchCapture()) }.resolve(promise)
    }

    @ReactMethod
    fun disableTouchCapture(promise: Promise) {
        runCatching { operationResultMap(AnsightRuntime.disableTouchCapture()) }.resolve(promise)
    }

    @ReactMethod
    fun updateSessionProperties(properties: ReadableMap?, promise: Promise) {
        runCatching {
            operationResultMap(AnsightRuntime.updateCustomProperties(properties.toGroupedStringMap()))
        }.resolve(promise)
    }

    @ReactMethod
    fun clearSessionProperties(promise: Promise) {
        runCatching { operationResultMap(AnsightRuntime.clearCustomProperties()) }.resolve(promise)
    }

    @ReactMethod
    fun registerCustomProperty(group: String, key: String, value: String, promise: Promise) {
        runCatching { operationResultMap(AnsightRuntime.registerCustomProperty(group, key, value)) }.resolve(promise)
    }

    @ReactMethod
    fun removeCustomProperty(group: String, key: String, promise: Promise) {
        runCatching { operationResultMap(AnsightRuntime.removeCustomProperty(group, key)) }.resolve(promise)
    }

    @ReactMethod
    fun registerCustomTool(definitionMap: ReadableMap, promise: Promise) {
        runCatching {
            val definition = toolDefinition(definitionMap)
            val timeoutMilliseconds = definitionMap.intValue("timeoutMilliseconds", 30_000).toLong().coerceAtLeast(250L)
            val registration = CustomToolRegistration(definition, timeoutMilliseconds)
            customToolRegistrations[definition.id] = registration
            activeCustomToolIds.add(definition.id)
            if (AnsightRuntime.snapshot().initialized) {
                installCustomTool(registration)
            }
            mapOf("id" to definition.id, "registered" to true).toWritableMap()
        }.resolve(promise)
    }

    @ReactMethod
    fun unregisterCustomTool(id: String, promise: Promise) {
        activeCustomToolIds.remove(id.trim())
        customToolRegistrations.remove(id.trim())
        promise.resolve(mapOf("id" to id.trim(), "registered" to false).toWritableMap())
    }

    @ReactMethod
    fun clearRegisteredCustomTools(promise: Promise) {
        activeCustomToolIds.clear()
        customToolRegistrations.clear()
        promise.resolve(mapOf("cleared" to true).toWritableMap())
    }

    @ReactMethod
    fun resolveToolCall(requestId: String, resultMap: ReadableMap, promise: Promise) {
        val pending = pendingToolCalls[requestId]
        if (pending == null) {
            promise.resolve(mapOf("requestId" to requestId, "accepted" to false).toWritableMap())
            return
        }

        val success = resultMap.booleanValue("success", true)
        val message = resultMap.stringValue("message")
        val errorCode = resultMap.stringValue("errorCode")
        val payload = resultPayload(resultMap)
        pending.result = if (success) {
            AndroidToolResult.success(payload, message)
        } else {
            AndroidToolResult.failure(message ?: "JavaScript tool failed.", errorCode, payload)
        }
        pending.latch.countDown()
        promise.resolve(mapOf("requestId" to requestId, "accepted" to true).toWritableMap())
    }

    @ReactMethod
    fun queueBinaryTransfer(requestId: String, base64Data: String, chunkBytes: Int, promise: Promise) {
        runCatching {
            val pending = pendingToolCalls[requestId.trim()]
                ?: return@runCatching operationResultMap(OperationResult.failure("Binary transfer requires an active JavaScript tool request.")).apply {
                    putString("errorCode", "artifact_request_unavailable")
                }
            val transport = pending.context.transport
                ?: return@runCatching operationResultMap(OperationResult.failure("Binary transfers require an active pairing session.")).apply {
                    putString("errorCode", "artifact_transfer_unavailable")
                }
            val bytes = Base64.decode(base64Data, Base64.DEFAULT)
            val normalizedChunkBytes = chunkBytes.coerceIn(1024, 512 * 1024)
            val transferId = PairingFileTransferWireProtocol.newTransferId()
            backgroundExecutor.execute {
                transport.sendBinaryTransfer(transferId, bytes, normalizedChunkBytes)
            }

            operationResultMap(OperationResult.success("Binary transfer queued.")).apply {
                putString("transferId", transferId)
                putString("deliveryMode", "websocket_binary")
                putString("wireProtocol", PairingFileTransferWireProtocol.ProtocolName)
                putString("status", "queued")
                putInt("chunkBytes", normalizedChunkBytes)
                putInt("sizeBytes", bytes.size)
            }
        }.resolve(promise)
    }

    private fun executeJavaScriptTool(
        toolId: String,
        arguments: Map<String, String>,
        context: AndroidToolExecutionContext,
        timeoutMilliseconds: Long,
    ): AndroidToolResult {
        if (toolId !in activeCustomToolIds) {
            return AndroidToolResult.failure("Tool '$toolId' is no longer registered in JavaScript.", "javascript_tool_unregistered")
        }
        if (listenerCount.get() <= 0 || !reactContext.hasActiveCatalystInstance()) {
            return AndroidToolResult.failure("React Native JavaScript bridge is not listening for Ansight tool calls.", "javascript_bridge_unavailable")
        }

        val requestId = "android.${UUID.randomUUID().toString().replace("-", "")}"
        val pending = PendingToolCall(context = context)
        pendingToolCalls[requestId] = pending
        emitToolCall(requestId, toolId, arguments, context)

        val completed = pending.latch.await(timeoutMilliseconds, TimeUnit.MILLISECONDS)
        pendingToolCalls.remove(requestId)
        if (!completed) {
            return AndroidToolResult.failure("JavaScript handler for tool '$toolId' timed out.", "javascript_tool_timeout")
        }
        return pending.result ?: AndroidToolResult.failure("JavaScript handler for tool '$toolId' returned no result.", "javascript_tool_empty_result")
    }

    private fun installRegisteredCustomTools() {
        customToolRegistrations.values.forEach { registration ->
            installCustomTool(registration)
        }
    }

    private fun installCustomTool(registration: CustomToolRegistration) {
        val definition = registration.definition
        activeCustomToolIds.add(definition.id)
        AnsightRuntime.registerTool(
            FunctionAndroidTool(definition) { arguments, context ->
                executeJavaScriptTool(definition.id, arguments, context, registration.timeoutMilliseconds)
            },
            replaceExisting = true,
        )
    }

    private fun emitToolCall(
        requestId: String,
        toolId: String,
        arguments: Map<String, String>,
        context: AndroidToolExecutionContext,
    ) {
        val event = Arguments.createMap()
        event.putString("requestId", requestId)
        event.putString("toolId", toolId)
        event.putString("platform", "android")
        event.putString("sessionId", context.sessionId)
        event.putString("nativeRequestId", context.requestId)
        event.putMap("arguments", arguments.toWritableMap())
        UiThreadUtil.runOnUiThread {
            reactContext
                .getJSModule(DeviceEventManagerModule.RCTDeviceEventEmitter::class.java)
                .emit("AnsightToolCall", event)
        }
    }

    private fun emitLogEvent(level: AnsightLogLevel, message: String, throwable: Throwable?) {
        if (listenerCount.get() <= 0) {
            return
        }
        val event = Arguments.createMap()
        event.putString("level", level.name.lowercase(Locale.US))
        event.putString("message", message)
        event.putString("platform", "android")
        throwable?.message?.let { event.putString("error", it) }
        UiThreadUtil.runOnUiThread {
            reactContext
                .getJSModule(DeviceEventManagerModule.RCTDeviceEventEmitter::class.java)
                .emit("AnsightLog", event)
        }
    }

    private fun configureReactNativeMemoryProfiling(map: ReadableMap?) {
        val options = reactNativeMemoryProfilingOptions(map)
        currentReactNativeMemoryOptions = options
        reactNativeMemoryProfiler.register(options)
    }

    private fun buildOptions(map: ReadableMap?): AnsightOptions {
        val useNativeAllInOneDefaults = map.booleanValue("useNativeAllInOneDefaults", false)
        val clientName = map.stringValue("clientName")
        var options = if (useNativeAllInOneDefaults) {
            AnsightDeveloperMode.options(
                clientName = clientName,
            )
        } else {
            AnsightOptions()
        }
        if (useNativeAllInOneDefaults && !map.hasString("toolGuard")) {
            options = options.copy(toolGuard = AnsightToolGuard.ReadOnly)
        }

        if (map.hasNumber("sampleFrequencyMilliseconds")) {
            options = options.copy(sampleFrequencyMilliseconds = map.intValue("sampleFrequencyMilliseconds", options.sampleFrequencyMilliseconds))
        }
        if (map.hasNumber("retentionPeriodSeconds")) {
            options = options.copy(retentionPeriodSeconds = map.intValue("retentionPeriodSeconds", options.retentionPeriodSeconds))
        }
        if (map.hasBoolean("enableFramesPerSecond")) {
            options = options.copy(enableFramesPerSecond = map.booleanValue("enableFramesPerSecond", options.enableFramesPerSecond))
        }
        if (map.hasBoolean("enableBatteryLevel")) {
            options = options.copy(enableBatteryLevel = map.booleanValue("enableBatteryLevel", options.enableBatteryLevel))
        }
        if (map.hasMap("defaultMemoryChannels")) {
            val memory = map.getMapOrNull("defaultMemoryChannels")
            val managedHeap = if (memory.hasBoolean("managedHeap")) {
                memory.booleanValue("managedHeap", options.defaultMemoryChannels.javaHeap)
            } else {
                memory.booleanValue("javaHeap", options.defaultMemoryChannels.javaHeap)
            }
            val residentSetSize = if (memory.hasBoolean("residentSetSize")) {
                memory.booleanValue("residentSetSize", options.defaultMemoryChannels.rss)
            } else {
                memory.booleanValue("rss", options.defaultMemoryChannels.rss)
            }
            options = options.copy(
                defaultMemoryChannels = DefaultMemoryChannels(
                    javaHeap = managedHeap,
                    nativeHeap = memory.booleanValue("nativeHeap", options.defaultMemoryChannels.nativeHeap),
                    rss = residentSetSize,
                ),
            )
        }
        if (map.hasArray("additionalChannels")) {
            val channels = mutableListOf<AnsightChannel>()
            val array = map.getArrayOrNull("additionalChannels")
            if (array != null) {
                for (index in 0 until array.size()) {
                    val channel = array.getMap(index)
                    channels += AnsightChannel(
                        id = channel.intValue("id", -1),
                        name = channel.stringValue("name") ?: "",
                        unit = channel.stringValue("unit"),
                        type = channel.stringValue("type") ?: "custom",
                        colorHex = channel.stringValue("colorHex"),
                        source = channel.stringValue("source"),
                        group = channel.stringValue("group"),
                        kind = channel.stringValue("kind"),
                    )
                }
            }
            options = options.copy(additionalChannels = channels)
        }
        if (map.hasKey("sessionJpegCapture")) {
            options = options.copy(
                sessionJpegCapture = if (map.isFalse("sessionJpegCapture")) {
                    null
                } else {
                    val jpeg = map.getMapOrNull("sessionJpegCapture")
                    AnsightSessionJpegCaptureOptions(
                        intervalMilliseconds = jpeg.intValue(
                            "intervalMilliseconds",
                            AnsightSessionJpegCaptureOptions.DefaultIntervalMilliseconds,
                        ),
                        quality = jpeg.intValue("quality", AnsightSessionJpegCaptureOptions.DefaultQuality),
                        maxWidth = jpeg.optionalInt("maxWidth") ?: AnsightSessionJpegCaptureOptions.DefaultMaxWidth,
                        captureGpuBackedSurfaces = jpeg.booleanValue(
                            "captureGpuBackedSurfaces",
                            AnsightSessionJpegCaptureOptions.DefaultCaptureGpuBackedSurfaces,
                        ),
                    )
                },
            )
        }
        if (map.hasKey("touchCapture")) {
            options = options.copy(
                touchCapture = if (map.isFalse("touchCapture")) {
                    null
                } else {
                    val touch = map.getMapOrNull("touchCapture")
                    AnsightTouchCaptureOptions(
                        moveCaptureDistanceThreshold = touch.doubleValue("moveCaptureDistanceThreshold", 8.0),
                        moveCaptureFramesPerSecond = touch.intValue("moveCaptureFramesPerSecond", 20),
                    )
                },
            )
        }
        if (map.hasString("toolGuard")) {
            options = options.copy(toolGuard = toolGuard(map.stringValue("toolGuard")))
        }
        if (map.hasMap("customProperties")) {
            options = options.copy(customProperties = map.getMapOrNull("customProperties").toGroupedStringMap())
        }
        if (map.hasMap("hostAutoProbe")) {
            val autoProbe = map.getMapOrNull("hostAutoProbe")
            options = options.copy(
                hostAutoProbe = AnsightHostAutoProbeOptions(
                    enabled = autoProbe.booleanValue("enabled", options.hostAutoProbe.enabled),
                    initialDelayMilliseconds = autoProbe.longValue("initialDelayMilliseconds", options.hostAutoProbe.initialDelayMilliseconds),
                    probeIntervalMilliseconds = autoProbe.longValue("probeIntervalMilliseconds", options.hostAutoProbe.probeIntervalMilliseconds),
                    reconnectDelayMilliseconds = autoProbe.longValue("reconnectDelayMilliseconds", options.hostAutoProbe.reconnectDelayMilliseconds),
                    clientName = autoProbe.stringValue("clientName") ?: options.hostAutoProbe.clientName,
                ),
            )
        }
        if (map.hasMap("hostConnection")) {
            val host = map.getMapOrNull("hostConnection")
            options = options.copy(
                hostConnection = AnsightHostConnectionOptions(
                    savedConfigKey = host.stringValue("savedConfigKey") ?: options.hostConnection.savedConfigKey,
                    bundledConfigJson = host.stringValue("bundledConfigJson") ?: options.hostConnection.bundledConfigJson,
                    discoveryPort = host.optionalInt("discoveryPort") ?: options.hostConnection.discoveryPort,
                    allowCellularConnections = host.booleanValue(
                        "allowCellularConnections",
                        options.hostConnection.allowCellularConnections,
                    ),
                    connectionProfileRetentionSeconds = host.longValue(
                        "connectionProfileRetentionSeconds",
                        options.hostConnection.connectionProfileRetentionSeconds,
                    ),
                ),
            )
        }
        if (map.hasMap("secureStorage")) {
            val secure = map.getMapOrNull("secureStorage")
            options = options.copy(
                secureStorage = AnsightSecureStorageOptions(
                    preferencesName = secure.stringValue("preferencesName") ?: options.secureStorage.preferencesName,
                    allowedKeys = secure.getStringSet("allowedKeys"),
                    allowedPrefixes = secure.getStringSet("allowedPrefixes"),
                ),
            )
        }
        return options.withNativeToolOptions(
            map,
            enableVisualTreeToolsByDefault = useNativeAllInOneDefaults,
        )
    }

    private fun AnsightOptions.withNativeToolOptions(
        map: ReadableMap?,
        enableVisualTreeToolsByDefault: Boolean,
    ): AnsightOptions {
        val remoteTools = map.getMapOrNull("remoteTools")
        val builder = AnsightOptions.createBuilder(this)
        if (remoteTools.toolSuiteEnabled("visualTree", enableVisualTreeToolsByDefault)) {
            builder.withVisualTreeTools()
        }
        builder.withDatabaseTools(databaseToolsOptions(remoteTools.getMapOrNull("database")))
        builder.withFileSystemTools(fileSystemToolsOptions(remoteTools.getMapOrNull("fileSystem")))
        builder.withPreferencesTools(preferencesToolsOptions(remoteTools.getMapOrNull("preferences")))
        builder.withReflectionTools(reflectionToolsOptions(remoteTools.getMapOrNull("reflection")))
        builder.withSecureStorageTools(
            secureStorageToolsOptions(
                remoteTools.getMapOrNull("secureStorage") ?: map.getMapOrNull("secureStorage"),
                secureStorage,
            ),
        )
        return builder.build()
    }

    private fun fileSystemToolsOptions(map: ReadableMap?): AndroidFileSystemToolsOptions {
        return AndroidFileSystemToolsOptions(
            additionalRoots = rootOptions(map.getArrayOrNull("additionalRoots")).map { root ->
                AndroidFileSystemRoot(root.alias, root.path)
            },
        ).validated()
    }

    private fun databaseToolsOptions(map: ReadableMap?): AndroidDatabaseToolsOptions {
        return AndroidDatabaseToolsOptions(
            additionalRoots = rootOptions(map.getArrayOrNull("additionalRoots")).map { root ->
                AndroidDatabaseRoot(root.alias, root.path)
            },
            includePlatformRoots = map.booleanValue("includePlatformRoots", true),
        ).validated()
    }

    private fun preferencesToolsOptions(map: ReadableMap?): AndroidPreferencesToolsOptions {
        return AndroidPreferencesToolsOptions(
            defaultStore = map.stringValue("defaultStore"),
            allowedStores = map.getStringSet("allowedStores"),
            allowedKeys = map.getStringSet("allowedKeys"),
            allowedKeyPrefixes = map.getStringSet("allowedKeyPrefixes"),
        ).validated()
    }

    private fun reflectionToolsOptions(map: ReadableMap?): AndroidReflectionToolsOptions {
        return AndroidReflectionToolsOptions(
            includeBuiltInRoots = map.booleanValue("includeBuiltInRoots", true),
            allowedRootIds = map.getStringSet("allowedRootIds"),
            allowedTypePrefixes = map.getStringSet("allowedTypePrefixes"),
        ).validated()
    }

    private fun secureStorageToolsOptions(
        map: ReadableMap?,
        fallback: AnsightSecureStorageOptions,
    ): AnsightSecureStorageOptions {
        val allowedPrefixes = map.getStringSet("allowedKeyPrefixes") + map.getStringSet("allowedPrefixes")
        return AnsightSecureStorageOptions(
            preferencesName = map.stringValue("preferencesName") ?: fallback.preferencesName,
            allowedKeys = map.getStringSet("allowedKeys"),
            allowedPrefixes = allowedPrefixes,
        ).validated()
    }

    private data class NativeToolRoot(val alias: String, val path: String)

    private fun rootOptions(array: ReadableArray?): List<NativeToolRoot> {
        if (array == null) {
            return emptyList()
        }
        val roots = mutableListOf<NativeToolRoot>()
        for (index in 0 until array.size()) {
            if (!array.isNull(index) && array.getType(index) == ReadableType.Map) {
                val root = array.getMap(index)
                val alias = root.stringValue("alias")
                val path = root.stringValue("path")
                if (alias != null && path != null) {
                    roots += NativeToolRoot(alias, path)
                }
            }
        }
        return roots
    }

    private fun pairingOpenOptions(map: ReadableMap?): PairingOpenOptions =
        PairingOpenOptions(
            clientName = map.stringValue("clientName") ?: "React Native",
            expectedAppId = map.stringValue("expectedAppId"),
            hostAddressOverride = map.stringValue("hostAddressOverride"),
        )

    private fun sessionJpegCaptureOptions(map: ReadableMap?): AnsightSessionJpegCaptureOptions? {
        if (map == null) {
            return null
        }
        return AnsightSessionJpegCaptureOptions(
            intervalMilliseconds = map.intValue(
                "intervalMilliseconds",
                AnsightSessionJpegCaptureOptions.DefaultIntervalMilliseconds,
            ),
            quality = map.intValue("quality", AnsightSessionJpegCaptureOptions.DefaultQuality),
            maxWidth = map.optionalInt("maxWidth") ?: AnsightSessionJpegCaptureOptions.DefaultMaxWidth,
            captureGpuBackedSurfaces = map.booleanValue(
                "captureGpuBackedSurfaces",
                AnsightSessionJpegCaptureOptions.DefaultCaptureGpuBackedSurfaces,
            ),
        )
    }

    private fun toolDefinition(map: ReadableMap): ToolDefinition =
        ToolDefinition(
            id = map.stringValue("id") ?: "",
            name = map.stringValue("name") ?: map.stringValue("id") ?: "",
            description = map.stringValue("description") ?: "",
            category = map.stringValue("category") ?: "custom",
            scope = toolScope(map.stringValue("scope")),
            keywords = when {
                map.hasArray("keywords") -> map.getArray("keywords").toStringList().joinToString(" ")
                else -> map.stringValue("keywords") ?: "react native custom tool"
            },
            argumentsSchema = schemaFrom(map.getMapOrNull("argumentsSchema")),
            resultSchema = schemaFrom(map.getMapOrNull("resultSchema")),
            security = toolSecurity(map.getMapOrNull("security")),
        ).validated()

    private fun schemaFrom(map: ReadableMap?): ToolSchema {
        if (map == null) {
            return ToolSchema.obj(additionalProperties = true)
        }
        val type = when {
            map.hasArray("type") -> map.getArray("type").toStringList().firstOrNull { it != "null" } ?: "object"
            else -> map.stringValue("type") ?: "object"
        }
        val properties = mutableMapOf<String, ToolSchema>()
        map.getMapOrNull("properties")?.let { props ->
            val iterator = props.keySetIterator()
            while (iterator.hasNextKey()) {
                val key = iterator.nextKey()
                props.getMapOrNull(key)?.let { properties[key] = schemaFrom(it) }
            }
        }
        return ToolSchema(
            type = type,
            description = map.stringValue("description"),
            properties = properties,
            required = map.getArrayOrNull("required").toStringList(),
            items = schemaFrom(map.getMapOrNull("items")).takeIf { map.hasMap("items") },
            enumValues = map.getArrayOrNull("enum").toStringList(),
            additionalProperties = map.booleanValue("additionalProperties", false),
            nullable = map.hasArray("type") && map.getArray("type").toStringList().contains("null"),
            format = map.stringValue("format"),
        )
    }

    private fun toolSecurity(map: ReadableMap?): ToolSecurity {
        if (map == null) {
            return ToolSecurity.Unspecified
        }
        return ToolSecurity(
            level = when (map.stringValue("level")?.trim()?.lowercase()) {
                "medium", "moderate" -> ToolSecurityLevel.Medium
                "high" -> ToolSecurityLevel.High
                "critical" -> ToolSecurityLevel.Critical
                else -> ToolSecurityLevel.Low
            },
            implications = map.getArrayOrNull("implications").toStringList(),
        )
    }

    private fun resultPayload(map: ReadableMap): JSONObject? {
        if (!map.hasKey("result") || map.isNull("result")) {
            return null
        }
        return when (map.getType("result")) {
            ReadableType.Map -> map.getMap("result")?.toJSONObject()
            ReadableType.Array -> JSONObject().put("value", map.getArray("result")?.toJSONArray() ?: JSONArray())
            ReadableType.String -> JSONObject().put("value", map.getString("result"))
            ReadableType.Number -> JSONObject().put("value", map.getDouble("result"))
            ReadableType.Boolean -> JSONObject().put("value", map.getBoolean("result"))
            else -> null
        }
    }

    private fun snapshotMap(): WritableMap {
        val snapshot = AnsightRuntime.snapshot()
        return Arguments.createMap().apply {
            putBoolean("initialized", snapshot.initialized)
            putBoolean("active", snapshot.active)
            putBoolean("sessionOpen", snapshot.sessionOpen)
            putString("lifecycleState", snapshot.lifecycleState.wireName)
            putString("lifecycleChangedAtUtc", snapshot.lifecycleChangedAtUtc)
            putInt("metricsRecorded", snapshot.metricsRecorded)
            putInt("eventsRecorded", snapshot.eventsRecorded)
            putInt("touchesRecorded", snapshot.touchesRecorded)
            putInt("registeredTools", snapshot.registeredTools)
            putString("sessionMessage", snapshot.sessionMessage)
            putMap("connectionStatus", hostConnectionStatusMap(snapshot.connectionStatus))
            putArray("channels", channelsArray(snapshot.channels))
            snapshot.lastMetric?.let { putMap("lastMetric", metricMap(it)) }
            snapshot.lastEvent?.let { putMap("lastEvent", eventMap(it)) }
            snapshot.currentScreen?.let { screen ->
                putMap("currentScreen", Arguments.createMap().apply {
                    putString("name", screen.name)
                    putString("capturedAtUtc", screen.capturedAtUtc)
                    putMap("details", screen.details.toWritableMap())
                })
            }
        }
    }

    private fun hostConnectionStatusMap(status: HostConnectionStatus): WritableMap =
        Arguments.createMap().apply {
            putBoolean("isRuntimeActive", status.isRuntimeActive)
            putBoolean("isConnected", status.isConnected)
            putString("connectionState", status.connectionState.name)
            putBoolean("hasCachedSession", status.hasCachedSession)
            putBoolean("hasSavedConfig", status.hasSavedConfig)
            putBoolean("hasBundledConfig", status.hasBundledConfig)
            putString("summaryKind", status.summaryKind.name)
            putString("summaryMessage", status.summaryMessage)
        }

    private fun hostConnectionCapabilitiesMap(capabilities: HostConnectionCapabilities): WritableMap =
        Arguments.createMap().apply {
            putBoolean("canConnectUsingSavedConfig", capabilities.canConnectUsingSavedConfig)
            putBoolean("canConnectUsingBundledConfig", capabilities.canConnectUsingBundledConfig)
            putBoolean("canChooseConfigFile", capabilities.canChooseConfigFile)
            putBoolean("canScanConfigQrCode", capabilities.canScanConfigQrCode)
            putBoolean("canClearSavedConfigs", capabilities.canClearSavedConfigs)
        }

    private fun openSessionResultMap(result: OpenSessionResult): WritableMap =
        operationResultMap(OperationResult(result.success, result.message)).apply {
            putBoolean("accepted", result.accepted)
            putString("sessionId", result.sessionId)
            putString("configId", result.configId)
            putString("appId", result.appId)
            putString("resolvedHostAddress", result.resolvedHostAddress)
            putString("discoverySource", result.discoverySource)
            putString("reasonCode", result.reasonCode)
            putString("hostId", result.hostId)
            putString("hostName", result.hostName)
        }

    private fun hostConnectionResultMap(result: HostConnectionResult): WritableMap =
        operationResultMap(result).apply {
            putString("kind", result.kind.name)
            putString("source", result.source.name)
            putString("reasonCode", result.reasonCode ?: result.openSession?.reasonCode)
            result.openSession?.let { session ->
                putString("sessionId", session.sessionId)
                putString("configId", session.configId)
                putString("appId", session.appId)
                putString("resolvedHostAddress", session.resolvedHostAddress)
                putString("hostId", session.hostId)
                putString("hostName", session.hostName)
                putBoolean("accepted", session.accepted)
                putString("discoverySource", session.discoverySource)
            }
        }

    private fun operationResultMap(result: OperationResult): WritableMap =
        Arguments.createMap().apply {
            putBoolean("success", result.success)
            putString("message", result.message)
        }

    private fun operationResultMap(result: HostConnectionResult): WritableMap =
        Arguments.createMap().apply {
            putBoolean("success", result.success)
            putString("message", result.message)
        }

    private fun optionsMap(options: AnsightOptions): WritableMap =
        Arguments.createMap().apply {
            putInt("sampleFrequencyMilliseconds", options.sampleFrequencyMilliseconds)
            putInt("retentionPeriodSeconds", options.retentionPeriodSeconds)
            putBoolean("enableFramesPerSecond", options.enableFramesPerSecond)
            putBoolean("enableBatteryLevel", options.enableBatteryLevel)
            putMap("defaultMemoryChannels", mapOf(
                "managedHeap" to options.defaultMemoryChannels.javaHeap,
                "javaHeap" to options.defaultMemoryChannels.javaHeap,
                "nativeHeap" to options.defaultMemoryChannels.nativeHeap,
                "residentSetSize" to options.defaultMemoryChannels.rss,
                "rss" to options.defaultMemoryChannels.rss,
                "physicalFootprint" to false,
            ).toWritableMap())
            putMap("reactNativeMemory", currentReactNativeMemoryOptions.toMap().toWritableMap())
            putArray("additionalChannels", channelsArray(options.additionalChannels))
            options.sessionJpegCapture?.let { capture ->
                putMap("sessionJpegCapture", mapOf(
                    "intervalMilliseconds" to capture.intervalMilliseconds,
                    "quality" to capture.quality,
                    "maxWidth" to capture.maxWidth,
                    "captureGpuBackedSurfaces" to capture.captureGpuBackedSurfaces,
                ).toWritableMap())
            } ?: putNull("sessionJpegCapture")
            options.touchCapture?.let { touch ->
                putMap("touchCapture", mapOf(
                    "moveCaptureDistanceThreshold" to touch.moveCaptureDistanceThreshold,
                    "moveCaptureFramesPerSecond" to touch.moveCaptureFramesPerSecond,
                ).toWritableMap())
            } ?: putNull("touchCapture")
            putString("toolGuard", toolGuardName(options.toolGuard))
            putMap("customProperties", options.customProperties.toGroupedWritableMap())
            putMap("hostAutoProbe", mapOf(
                "enabled" to options.hostAutoProbe.enabled,
                "initialDelayMilliseconds" to options.hostAutoProbe.initialDelayMilliseconds,
                "probeIntervalMilliseconds" to options.hostAutoProbe.probeIntervalMilliseconds,
                "reconnectDelayMilliseconds" to options.hostAutoProbe.reconnectDelayMilliseconds,
                "clientName" to options.hostAutoProbe.clientName,
            ).toWritableMap())
            putMap("hostConnection", mapOf(
                "savedConfigKey" to options.hostConnection.savedConfigKey,
                "hasBundledConfigJson" to (options.hostConnection.bundledConfigJson != null),
                "discoveryPort" to options.hostConnection.discoveryPort,
                "allowCellularConnections" to options.hostConnection.allowCellularConnections,
                "connectionProfileRetentionSeconds" to options.hostConnection.connectionProfileRetentionSeconds,
            ).toWritableMap())
            putMap("secureStorage", mapOf(
                "preferencesName" to options.secureStorage.preferencesName,
                "allowedKeys" to options.secureStorage.allowedKeys.sorted().joinToString(","),
                "allowedPrefixes" to options.secureStorage.allowedPrefixes.sorted().joinToString(","),
            ).toWritableMap())
        }

    private fun channelMap(channel: AnsightChannel): WritableMap =
        Arguments.createMap().apply {
            putInt("id", channel.id)
            putString("name", channel.name)
            putString("unit", channel.unit)
            putString("type", channel.type)
            putString("colorHex", channel.colorHex)
            putString("source", channel.source)
            putString("group", channel.group)
            putString("kind", channel.kind)
        }

    private fun channelsArray(channels: List<AnsightChannel>): WritableArray =
        Arguments.createArray().apply {
            channels.forEach { pushMap(channelMap(it)) }
        }

    private fun metricsArray(metrics: List<RecordedMetric>): WritableArray =
        Arguments.createArray().apply {
            metrics.forEach { pushMap(metricMap(it)) }
        }

    private fun eventsArray(events: List<RecordedEvent>): WritableArray =
        Arguments.createArray().apply {
            events.forEach { pushMap(eventMap(it)) }
        }

    private fun metricMap(metric: RecordedMetric): WritableMap =
        Arguments.createMap().apply {
            putDouble("value", metric.value.toDouble())
            putString("capturedAtUtc", metric.capturedAtUtc)
            putDouble("capturedAtEpochMs", metric.capturedAtEpochMs.toDouble())
            putInt("channel", metric.channel)
            putDouble("sequence", metric.sequence.toDouble())
        }

    private fun eventMap(event: RecordedEvent): WritableMap =
        Arguments.createMap().apply {
            putString("id", event.id)
            putString("label", event.label)
            putString("type", event.type.wireName)
            putString("details", event.details)
            putString("capturedAtUtc", event.capturedAtUtc)
            putDouble("capturedAtEpochMs", event.capturedAtEpochMs.toDouble())
            putString("externalId", event.externalId)
            putInt("channel", event.channel)
            putDouble("sequence", event.sequence.toDouble())
        }

    private fun application(): Application =
        reactContext.applicationContext as? Application
            ?: error("React Native application context is not an Android Application.")

    private fun bindCurrentActivity() {
        val activity: Activity = reactContext.currentActivity ?: return
        AnsightRuntime.bindActivity(activity)
    }
}

private fun <T> Result<T>.resolve(promise: Promise) {
    fold(
        onSuccess = { promise.resolve(it) },
        onFailure = { promise.reject("ansight_error", it.message, it) },
    )
}

private fun eventType(raw: String?): ai.ansight.runtime.AnsightEventType =
    when (raw?.trim()?.lowercase()) {
        "event" -> ai.ansight.runtime.AnsightEventType.Event
        "debug" -> ai.ansight.runtime.AnsightEventType.Debug
        "warning", "warn" -> ai.ansight.runtime.AnsightEventType.Warning
        "error" -> ai.ansight.runtime.AnsightEventType.Error
        "exception" -> ai.ansight.runtime.AnsightEventType.Exception
        "gc" -> ai.ansight.runtime.AnsightEventType.Gc
        "navigation" -> ai.ansight.runtime.AnsightEventType.Navigation
        "screenviewed", "screen_viewed" -> ai.ansight.runtime.AnsightEventType.ScreenViewed
        "lifecycle" -> ai.ansight.runtime.AnsightEventType.Lifecycle
        else -> ai.ansight.runtime.AnsightEventType.Info
    }

private fun lifecycleState(raw: String): AppLifecycleState =
    when (raw.trim().lowercase()) {
        "foreground", "active" -> AppLifecycleState.Foreground
        "background", "inactive" -> AppLifecycleState.Background
        else -> AppLifecycleState.Unknown
    }

private fun toolGuard(raw: String?): AnsightToolGuard =
    when (raw?.trim()?.lowercase()) {
        "readonly", "read_only", "read" -> AnsightToolGuard.ReadOnly
        "readwrite", "read_write", "write" -> AnsightToolGuard.ReadWrite
        "full", "fullaccess", "full_access" -> AnsightToolGuard.FullAccess
        else -> AnsightToolGuard.Disabled
    }

private fun toolGuardName(guard: AnsightToolGuard): String =
    when (guard) {
        AnsightToolGuard.Disabled -> "disabled"
        AnsightToolGuard.ReadOnly -> "readOnly"
        AnsightToolGuard.ReadWrite -> "readWrite"
        AnsightToolGuard.FullAccess -> "fullAccess"
    }

private fun toolScope(raw: String?): ToolScope =
    when (raw?.trim()?.lowercase()) {
        "write" -> ToolScope.Write
        "delete" -> ToolScope.Delete
        else -> ToolScope.Read
    }

private fun ReadableMap?.hasKey(name: String): Boolean = this?.hasKey(name) == true
private fun ReadableMap?.hasString(name: String): Boolean = this?.hasKey(name) == true && this.getType(name) == ReadableType.String
private fun ReadableMap?.hasNumber(name: String): Boolean = this?.hasKey(name) == true && this.getType(name) == ReadableType.Number
private fun ReadableMap?.hasBoolean(name: String): Boolean = this?.hasKey(name) == true && this.getType(name) == ReadableType.Boolean
private fun ReadableMap?.hasMap(name: String): Boolean = this?.hasKey(name) == true && this.getType(name) == ReadableType.Map
private fun ReadableMap?.hasArray(name: String): Boolean = this?.hasKey(name) == true && this.getType(name) == ReadableType.Array
private fun ReadableMap?.isFalse(name: String): Boolean = this?.hasBoolean(name) == true && !this.getBoolean(name)
private fun ReadableMap?.toolSuiteEnabled(name: String, defaultValue: Boolean = false): Boolean {
    val map = this ?: return defaultValue
    if (!map.hasKey(name)) {
        return defaultValue
    }
    if (map.isNull(name)) {
        return false
    }
    return when (map.getType(name)) {
        ReadableType.Boolean -> map.getBoolean(name)
        ReadableType.Map -> map.getMap(name).booleanValue("enabled", true)
        else -> false
    }
}
private fun ReadableMap?.stringValue(name: String): String? =
    if (this?.hasKey(name) == true && !isNull(name) && getType(name) == ReadableType.String) getString(name)?.trim()?.ifBlank { null } else null
private fun ReadableMap?.booleanValue(name: String, default: Boolean): Boolean =
    if (this?.hasKey(name) == true && !isNull(name) && getType(name) == ReadableType.Boolean) getBoolean(name) else default
private fun ReadableMap?.doubleValue(name: String, default: Double): Double =
    if (this?.hasKey(name) == true && !isNull(name) && getType(name) == ReadableType.Number) getDouble(name) else default
private fun ReadableMap?.intValue(name: String, default: Int): Int =
    if (this?.hasKey(name) == true && !isNull(name) && getType(name) == ReadableType.Number) getDouble(name).toInt() else default
private fun ReadableMap?.longValue(name: String, default: Long): Long =
    if (this?.hasKey(name) == true && !isNull(name) && getType(name) == ReadableType.Number) getDouble(name).toLong() else default
private fun ReadableMap?.optionalInt(name: String): Int? =
    if (this?.hasKey(name) == true && !isNull(name) && getType(name) == ReadableType.Number) getDouble(name).toInt() else null
private fun ReadableMap?.getMapOrNull(name: String): ReadableMap? =
    if (this?.hasKey(name) == true && !isNull(name) && getType(name) == ReadableType.Map) getMap(name) else null
private fun ReadableMap?.getArrayOrNull(name: String): ReadableArray? =
    if (this?.hasKey(name) == true && !isNull(name) && getType(name) == ReadableType.Array) getArray(name) else null

private fun ReadableMap?.toStringMap(): Map<String, String> {
    if (this == null) {
        return emptyMap()
    }
    val result = linkedMapOf<String, String>()
    val iterator = keySetIterator()
    while (iterator.hasNextKey()) {
        val key = iterator.nextKey()
        if (!isNull(key)) {
            result[key] = when (getType(key)) {
                ReadableType.String -> getString(key).orEmpty()
                ReadableType.Number -> getDouble(key).toString()
                ReadableType.Boolean -> getBoolean(key).toString()
                else -> ""
            }
        }
    }
    return result
}

private fun ReadableMap?.toGroupedStringMap(): Map<String, Map<String, String>> {
    if (this == null) {
        return emptyMap()
    }
    val result = linkedMapOf<String, Map<String, String>>()
    val iterator = keySetIterator()
    while (iterator.hasNextKey()) {
        val key = iterator.nextKey()
        if (!isNull(key) && getType(key) == ReadableType.Map) {
            result[key] = getMap(key).toStringMap()
        }
    }
    return result
}

private fun ReadableMap?.getStringSet(name: String): Set<String> =
    getArrayOrNull(name).toStringList().toSet()

private fun ReadableArray?.toStringList(): List<String> {
    if (this == null) {
        return emptyList()
    }
    val result = mutableListOf<String>()
    for (index in 0 until size()) {
        if (!isNull(index)) {
            result += when (getType(index)) {
                ReadableType.String -> getString(index).orEmpty()
                ReadableType.Number -> getDouble(index).toString()
                ReadableType.Boolean -> getBoolean(index).toString()
                else -> ""
            }
        }
    }
    return result.filter { it.isNotBlank() }
}

private fun ReadableMap.toJSONObject(): JSONObject {
    val result = JSONObject()
    val iterator = keySetIterator()
    while (iterator.hasNextKey()) {
        val key = iterator.nextKey()
        if (isNull(key)) {
            result.put(key, JSONObject.NULL)
        } else {
            when (getType(key)) {
                ReadableType.Map -> result.put(key, getMap(key)?.toJSONObject() ?: JSONObject.NULL)
                ReadableType.Array -> result.put(key, getArray(key)?.toJSONArray() ?: JSONObject.NULL)
                ReadableType.String -> result.put(key, getString(key))
                ReadableType.Number -> result.put(key, getDouble(key))
                ReadableType.Boolean -> result.put(key, getBoolean(key))
                else -> result.put(key, JSONObject.NULL)
            }
        }
    }
    return result
}

private fun ReadableArray.toJSONArray(): JSONArray {
    val result = JSONArray()
    for (index in 0 until size()) {
        if (isNull(index)) {
            result.put(JSONObject.NULL)
        } else {
            when (getType(index)) {
                ReadableType.Map -> result.put(getMap(index)?.toJSONObject() ?: JSONObject.NULL)
                ReadableType.Array -> result.put(getArray(index)?.toJSONArray() ?: JSONObject.NULL)
                ReadableType.String -> result.put(getString(index))
                ReadableType.Number -> result.put(getDouble(index))
                ReadableType.Boolean -> result.put(getBoolean(index))
                else -> result.put(JSONObject.NULL)
            }
        }
    }
    return result
}

private fun Map<String, *>.toWritableMap(): WritableMap =
    Arguments.createMap().also { map ->
        entries.sortedBy { it.key }.forEach { (key, value) ->
            when (value) {
                is Boolean -> map.putBoolean(key, value)
                is Int -> map.putInt(key, value)
                is Double -> map.putDouble(key, value)
                is Number -> map.putDouble(key, value.toDouble())
                is String -> map.putString(key, value)
                null -> map.putNull(key)
                else -> map.putString(key, value.toString())
            }
        }
    }

private fun Map<String, Map<String, String>>.toGroupedWritableMap(): WritableMap =
    Arguments.createMap().also { map ->
        entries.sortedBy { it.key }.forEach { (key, value) ->
            map.putMap(key, value.toWritableMap())
        }
    }
