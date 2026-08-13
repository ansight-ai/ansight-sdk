package ai.ansight.capacitor

import ai.ansight.Ansight
import ai.ansight.runtime.AndroidToolExecutionContext
import ai.ansight.runtime.AndroidToolResult
import ai.ansight.runtime.AnsightChannel
import ai.ansight.runtime.AnsightChannels
import ai.ansight.runtime.AnsightCrashCaptureOptions
import ai.ansight.runtime.AnsightDeveloperMode
import ai.ansight.runtime.AnsightHostAutoProbeOptions
import ai.ansight.runtime.AnsightHostConnectionOptions
import ai.ansight.runtime.AnsightLogCallback
import ai.ansight.runtime.AnsightLogLevel
import ai.ansight.runtime.AnsightLogger
import ai.ansight.runtime.AnsightOptions
import ai.ansight.runtime.AnsightRuntime
import ai.ansight.runtime.AnsightSecureStorageOptions
import ai.ansight.runtime.AnsightSessionJpegCaptureOptions
import ai.ansight.runtime.AnsightSessionJpegCaptureMode
import ai.ansight.runtime.AnsightToolGuard
import ai.ansight.runtime.AnsightTouchCaptureOptions
import ai.ansight.runtime.AppLifecycleState
import ai.ansight.runtime.DefaultMemoryChannels
import ai.ansight.runtime.FunctionAndroidTool
import ai.ansight.runtime.HostConnectionCapabilities
import ai.ansight.runtime.HostConnectionActionKind
import ai.ansight.runtime.HostConnectionRequest
import ai.ansight.runtime.HostConnectionRequestKind
import ai.ansight.runtime.HostConnectionResult
import ai.ansight.runtime.HostConnectionSource
import ai.ansight.runtime.HostConnectionStatus
import ai.ansight.runtime.OperationResult
import ai.ansight.runtime.OpenSessionResult
import ai.ansight.runtime.PairingFileTransferWireProtocol
import ai.ansight.runtime.PairingOpenOptions
import ai.ansight.runtime.RecordedEvent
import ai.ansight.runtime.RecordedMetric
import ai.ansight.runtime.ToolDefinition
import ai.ansight.runtime.ToolSchema
import ai.ansight.runtime.ToolScope
import ai.ansight.runtime.ToolSecurity
import ai.ansight.runtime.ToolSecurityLevel
import ai.ansight.runtime.sendBinaryTransfer
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
import ai.ansight.pairing.AnsightPairing
import android.app.Application
import android.os.Handler
import android.os.Looper
import android.util.Base64
import com.getcapacitor.JSObject
import com.getcapacitor.Plugin
import com.getcapacitor.PluginCall
import com.getcapacitor.PluginMethod
import com.getcapacitor.annotation.CapacitorPlugin
import org.json.JSONArray
import org.json.JSONObject
import java.util.Locale
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.CountDownLatch
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit

@CapacitorPlugin(name = "Ansight")
class AnsightCapacitorPlugin : Plugin() {
    private data class PendingToolCall(
        val context: AndroidToolExecutionContext,
        val latch: CountDownLatch = CountDownLatch(1),
        @Volatile var result: AndroidToolResult? = null,
    )

    private data class CustomToolRegistration(
        val definition: ToolDefinition,
        val timeoutMilliseconds: Long,
    )

    private val executor = Executors.newCachedThreadPool()
    private val pendingToolCalls = ConcurrentHashMap<String, PendingToolCall>()
    private val customToolRegistrations = ConcurrentHashMap<String, CustomToolRegistration>()
    private val activeCustomToolIds = ConcurrentHashMap.newKeySet<String>()
    private val mainHandler = Handler(Looper.getMainLooper())
    private val logCallback = AnsightLogCallback { level, message, throwable ->
        val event = JSObject()
            .putValue("level", level.name.lowercase(Locale.US))
            .putValue("message", message)
            .putValue("platform", "android")
        throwable?.message?.let { event.put("error", it) }
        mainHandler.post { notifyListeners("ansightLog", event) }
    }

    override fun load() {
        AnsightLogger.registerCallback(logCallback)
    }

    override fun handleOnDestroy() {
        AnsightLogger.removeCallback(logCallback)
        executor.shutdownNow()
        super.handleOnDestroy()
    }

    @PluginMethod
    fun initialize(call: PluginCall) = resolve(call) {
        Ansight.initialize(application(), buildOptions(call.data))
        installRegisteredCustomTools()
        snapshot()
    }

    @PluginMethod
    fun initializeAndActivate(call: PluginCall) = resolve(call) {
        Ansight.initializeAndActivate(application(), buildOptions(call.data))
        bindCurrentActivity()
        installRegisteredCustomTools()
        snapshot()
    }

    @PluginMethod
    fun activate(call: PluginCall) = resolve(call) {
        AnsightRuntime.activate()
        bindCurrentActivity()
        snapshot()
    }

    @PluginMethod
    fun deactivate(call: PluginCall) = resolve(call) {
        AnsightRuntime.deactivate()
        snapshot()
    }

    @PluginMethod
    fun clear(call: PluginCall) = resolve(call) {
        AnsightRuntime.clear()
        snapshot()
    }

    @PluginMethod
    fun registerMetricChannel(call: PluginCall) = resolve(call) {
        val channel = call.data.objectValue("channel")
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
        snapshot()
    }

    @PluginMethod
    fun recordMetric(call: PluginCall) = resolve(call) {
        AnsightRuntime.metric(
            call.data.doubleValue("value", 0.0).toLong(),
            call.data.intValue("channel", AnsightChannels.Unspecified),
        )
        snapshot()
    }

    @PluginMethod
    fun recordEvent(call: PluginCall) = resolve(call) {
        AnsightRuntime.event(
            label = call.getString("label").orEmpty(),
            type = eventType(call.getString("type")),
            details = call.getString("details"),
            channel = call.data.intValue("channel", AnsightChannels.Unspecified),
        )
        snapshot()
    }

    @PluginMethod
    fun recordCrashCandidate(call: PluginCall) = resolve(call) {
        val candidateId = AnsightRuntime.recordCrashCandidate(
            runtime = call.getString("runtime") ?: "capacitor-javascript",
            kind = call.getString("kind") ?: "unhandled_javascript_error",
            message = call.getString("message"),
            stack = call.getString("stack"),
            fatal = call.getBoolean("fatal", false) ?: false,
            metadataJson = call.getString("metadata"),
        )
        JSObject().putValue("candidateId", candidateId)
    }

    @PluginMethod
    fun screenViewed(call: PluginCall) = resolve(call) {
        AnsightRuntime.screenViewed(
            call.getString("name").orEmpty(),
            call.data.objectValue("details").toStringMap(),
        )
        snapshot()
    }

    @PluginMethod
    fun setAppLifecycleState(call: PluginCall) = resolve(call) {
        AnsightRuntime.setAppLifecycleState(lifecycleState(call.getString("state").orEmpty()))
        snapshot()
    }

    @PluginMethod
    fun connect(call: PluginCall) = background(call) {
        val pairingPayload = call.getString("pairingPayload")
        val request = HostConnectionRequest(
            kind = if (pairingPayload.isNullOrBlank()) HostConnectionRequestKind.Auto else HostConnectionRequestKind.Payload,
            payload = pairingPayload,
            clientName = call.getString("clientName"),
            expectedAppId = call.getString("expectedAppId"),
            hostAddressOverride = call.getString("hostAddressOverride"),
        )
        hostConnectionResult(AnsightRuntime.connect(request))
    }

    @PluginMethod
    fun scanPairingQrCode(call: PluginCall) {
        val currentActivity = activity
        if (currentActivity == null) {
            call.reject(
                "QR pairing is unavailable because no Android activity is available.",
                "ansight_qr_unavailable",
            )
            return
        }

        AnsightPairing.scanQrCode(
            activity = currentActivity,
            onPayload = { payload ->
                if (payload.isNullOrBlank()) {
                    call.resolve(
                        hostConnectionResult(
                            HostConnectionResult.failure(
                                message = "QR pairing canceled.",
                                kind = HostConnectionActionKind.Connect,
                                source = HostConnectionSource.ConfigReader,
                                reasonCode = "pairing_canceled",
                            ),
                        ),
                    )
                    return@scanQrCode
                }

                executor.execute {
                    resolve(call) {
                        hostConnectionResult(
                            AnsightRuntime.connect(
                                HostConnectionRequest(
                                    kind = HostConnectionRequestKind.QrCode,
                                    payload = payload,
                                    clientName = call.getString("clientName"),
                                    expectedAppId = call.getString("expectedAppId"),
                                    hostAddressOverride = call.getString("hostAddressOverride"),
                                ),
                            ),
                        )
                    }
                }
            },
            onError = {
                call.reject(
                    it.message ?: "QR pairing failed.",
                    "ansight_qr_error",
                    it as? Exception,
                )
            },
        )
    }

    @PluginMethod
    fun openSession(call: PluginCall) = background(call) {
        openSessionResult(
            AnsightRuntime.openSession(
                call.getString("pairingPayload").orEmpty(),
                pairingOpenOptions(call.data),
            ),
        )
    }

    @PluginMethod
    fun disconnect(call: PluginCall) = background(call) {
        hostConnectionResult(AnsightRuntime.disconnect())
    }

    @PluginMethod
    fun completeSession(call: PluginCall) = background(call) {
        AnsightRuntime.completeSession()
        operationResult(OperationResult.success("Session completed."))
    }

    @PluginMethod
    fun closeSession(call: PluginCall) = background(call) {
        AnsightRuntime.closeSession()
        operationResult(OperationResult.success("Session closed."))
    }

    @PluginMethod
    fun savePairingConfig(call: PluginCall) = background(call) {
        hostConnectionResult(
            AnsightRuntime.savePairingConfig(
                call.getString("pairingPayload").orEmpty(),
                call.getString("expectedAppId"),
            ),
        )
    }

    @PluginMethod
    fun clearSavedPairing(call: PluginCall) = resolve(call) {
        hostConnectionResult(AnsightRuntime.clearSavedPairingConfig())
    }

    @PluginMethod
    fun clearCachedSession(call: PluginCall) = resolve(call) {
        operationResult(AnsightRuntime.clearCachedSession())
    }

    @PluginMethod
    fun notifyHostConnectionConfigChanged(call: PluginCall) = resolve(call) {
        hostConnectionResult(AnsightRuntime.notifyHostConnectionConfigChanged())
    }

    @PluginMethod
    fun status(call: PluginCall) = resolve(call) { snapshot() }

    @PluginMethod
    fun snapshot(call: PluginCall) = resolve(call) { snapshot() }

    @PluginMethod
    fun hostConnectionStatus(call: PluginCall) = resolve(call) {
        hostConnectionStatus(AnsightRuntime.hostConnectionStatus())
    }

    @PluginMethod
    fun hostConnectionCapabilities(call: PluginCall) = resolve(call) {
        hostConnectionCapabilities(AnsightRuntime.hostConnectionCapabilities())
    }

    @PluginMethod
    fun currentOptions(call: PluginCall) = resolve(call) {
        options(AnsightRuntime.options())
    }

    @PluginMethod
    fun recordedMetrics(call: PluginCall) = resolve(call) {
        val metrics = AnsightRuntime.recordedMetrics()
        val limit = call.data.intValue("limit", 0)
        JSObject().putValue(
            "items",
            JSONArray(metrics.takeLast(if (limit > 0) limit else metrics.size).map(::metric)),
        )
    }

    @PluginMethod
    fun recordedEvents(call: PluginCall) = resolve(call) {
        val events = AnsightRuntime.recordedEvents()
        val limit = call.data.intValue("limit", 0)
        JSObject().putValue(
            "items",
            JSONArray(events.takeLast(if (limit > 0) limit else events.size).map(::event)),
        )
    }

    @PluginMethod
    fun sendClientLog(call: PluginCall) = resolve(call) {
        operationResult(AnsightRuntime.sendClientLog(call.getString("line").orEmpty()))
    }

    @PluginMethod
    fun captureBuiltInTelemetrySample(call: PluginCall) = resolve(call) {
        AnsightRuntime.captureBuiltInTelemetrySample()
        snapshot()
    }

    @PluginMethod
    fun isFramesPerSecondEnabled(call: PluginCall) = resolve(call) {
        JSObject().putValue("value", AnsightRuntime.isFramesPerSecondEnabled())
    }

    @PluginMethod
    fun enableFramesPerSecond(call: PluginCall) = resolve(call) {
        AnsightRuntime.enableFramesPerSecond()
        snapshot()
    }

    @PluginMethod
    fun disableFramesPerSecond(call: PluginCall) = resolve(call) {
        AnsightRuntime.disableFramesPerSecond()
        snapshot()
    }

    @PluginMethod
    fun captureScreenFrame(call: PluginCall) = background(call) {
        bindCurrentActivity()
        operationResult(AnsightRuntime.captureScreenFrame(sessionJpegCaptureOptions(call.data)))
    }

    @PluginMethod
    fun enableTouchCapture(call: PluginCall) = resolve(call) {
        operationResult(AnsightRuntime.enableTouchCapture())
    }

    @PluginMethod
    fun disableTouchCapture(call: PluginCall) = resolve(call) {
        operationResult(AnsightRuntime.disableTouchCapture())
    }

    @PluginMethod
    fun updateSessionProperties(call: PluginCall) = resolve(call) {
        operationResult(
            AnsightRuntime.updateCustomProperties(
                call.data.objectValue("properties").toGroupedStringMap(),
            ),
        )
    }

    @PluginMethod
    fun clearSessionProperties(call: PluginCall) = resolve(call) {
        operationResult(AnsightRuntime.clearCustomProperties())
    }

    @PluginMethod
    fun registerCustomProperty(call: PluginCall) = resolve(call) {
        operationResult(
            AnsightRuntime.registerCustomProperty(
                call.getString("group").orEmpty(),
                call.getString("key").orEmpty(),
                call.getString("value").orEmpty(),
            ),
        )
    }

    @PluginMethod
    fun removeCustomProperty(call: PluginCall) = resolve(call) {
        operationResult(
            AnsightRuntime.removeCustomProperty(
                call.getString("group").orEmpty(),
                call.getString("key").orEmpty(),
            ),
        )
    }

    @PluginMethod
    fun registerCustomTool(call: PluginCall) = resolve(call) {
        val map = call.data.objectValue("definition")
        val definition = toolDefinition(map)
        val registration = CustomToolRegistration(
            definition,
            map.intValue("timeoutMilliseconds", 30_000).toLong().coerceAtLeast(250L),
        )
        customToolRegistrations[definition.id] = registration
        activeCustomToolIds.add(definition.id)
        if (AnsightRuntime.snapshot().initialized) installCustomTool(registration)
        operationResult(OperationResult.success("Tool registered."))
            .putValue("id", definition.id)
    }

    @PluginMethod
    fun unregisterCustomTool(call: PluginCall) = resolve(call) {
        val id = call.getString("id").orEmpty().trim()
        activeCustomToolIds.remove(id)
        customToolRegistrations.remove(id)
        operationResult(OperationResult.success("Tool unregistered.")).putValue("id", id)
    }

    @PluginMethod
    fun clearRegisteredCustomTools(call: PluginCall) = resolve(call) {
        activeCustomToolIds.clear()
        customToolRegistrations.clear()
        operationResult(OperationResult.success("JavaScript tools cleared."))
    }

    @PluginMethod
    fun resolveToolCall(call: PluginCall) {
        val requestId = call.getString("requestId").orEmpty()
        val pending = pendingToolCalls[requestId]
        if (pending == null) {
            call.resolve(
                operationResult(OperationResult.failure("Tool request is no longer pending."))
                    .putValue("accepted", false),
            )
            return
        }
        val result = call.data.objectValue("result")
        val payload = resultPayload(result)
        pending.result = if (result.booleanValue("success", true)) {
            AndroidToolResult.success(payload, result.stringValue("message"))
        } else {
            AndroidToolResult.failure(
                result.stringValue("message") ?: "JavaScript tool failed.",
                result.stringValue("errorCode"),
                payload,
            )
        }
        pending.latch.countDown()
        call.resolve(
            operationResult(OperationResult.success("Tool result accepted."))
                .putValue("accepted", true),
        )
    }

    @PluginMethod
    fun queueBinaryTransfer(call: PluginCall) = resolve(call) {
        val requestId = call.getString("requestId").orEmpty().trim()
        val pending = pendingToolCalls[requestId]
            ?: return@resolve operationResult(
                OperationResult.failure("Binary transfer requires an active JavaScript tool request."),
            ).putValue("errorCode", "artifact_request_unavailable")
        val transport = pending.context.transport
            ?: return@resolve operationResult(
                OperationResult.failure("Binary transfers require an active pairing session."),
            ).putValue("errorCode", "artifact_transfer_unavailable")
        val bytes = Base64.decode(call.getString("base64Data").orEmpty(), Base64.DEFAULT)
        val chunkBytes = call.data.intValue("chunkBytes", 65_536).coerceIn(1_024, 512 * 1_024)
        val transferId = PairingFileTransferWireProtocol.newTransferId()
        executor.execute { transport.sendBinaryTransfer(transferId, bytes, chunkBytes) }
        operationResult(OperationResult.success("Binary transfer queued."))
            .putValue("transferId", transferId)
            .putValue("deliveryMode", "websocket_binary")
            .putValue("wireProtocol", PairingFileTransferWireProtocol.ProtocolName)
            .putValue("status", "queued")
            .putValue("chunkBytes", chunkBytes)
            .putValue("sizeBytes", bytes.size)
    }

    private fun executeJavaScriptTool(
        toolId: String,
        arguments: Map<String, String>,
        context: AndroidToolExecutionContext,
        timeoutMilliseconds: Long,
    ): AndroidToolResult {
        if (toolId !in activeCustomToolIds) {
            return AndroidToolResult.failure(
                "Tool '$toolId' is no longer registered in JavaScript.",
                "javascript_tool_unregistered",
            )
        }
        val requestId = "android.capacitor.${UUID.randomUUID().toString().replace("-", "")}"
        val pending = PendingToolCall(context)
        pendingToolCalls[requestId] = pending
        val event = JSObject()
            .putValue("requestId", requestId)
            .putValue("toolId", toolId)
            .putValue("platform", "android")
            .putValue("sessionId", context.sessionId)
            .putValue("nativeRequestId", context.requestId)
            .putValue("arguments", arguments.toJSObject())
        mainHandler.post { notifyListeners("ansightToolCall", event) }
        val completed = pending.latch.await(timeoutMilliseconds, TimeUnit.MILLISECONDS)
        pendingToolCalls.remove(requestId)
        if (!completed) {
            return AndroidToolResult.failure(
                "JavaScript handler for '$toolId' timed out.",
                "javascript_tool_timeout",
            )
        }
        return pending.result ?: AndroidToolResult.failure(
            "JavaScript handler returned no result.",
            "javascript_tool_result_missing",
        )
    }

    private fun installRegisteredCustomTools() {
        customToolRegistrations.values.forEach(::installCustomTool)
    }

    private fun installCustomTool(registration: CustomToolRegistration) {
        activeCustomToolIds.add(registration.definition.id)
        AnsightRuntime.registerTool(
            FunctionAndroidTool(registration.definition) { arguments, context ->
                executeJavaScriptTool(
                    registration.definition.id,
                    arguments,
                    context,
                    registration.timeoutMilliseconds,
                )
            },
            replaceExisting = true,
        )
    }

    private fun buildOptions(map: JSObject): AnsightOptions {
        val useDefaults = map.booleanValue("useNativeAllInOneDefaults", false)
        var options = if (useDefaults) {
            AnsightDeveloperMode.options(
                clientName = map.stringValue("clientName"),
            )
        } else {
            AnsightOptions()
        }
        if (map.has("sampleFrequencyMilliseconds")) {
            options = options.copy(
                sampleFrequencyMilliseconds = map.intValue(
                    "sampleFrequencyMilliseconds",
                    options.sampleFrequencyMilliseconds,
                ),
            )
        }
        if (map.has("retentionPeriodSeconds")) {
            options = options.copy(
                retentionPeriodSeconds = map.intValue(
                    "retentionPeriodSeconds",
                    options.retentionPeriodSeconds,
                ),
            )
        }
        if (map.has("enableFramesPerSecond")) {
            options = options.copy(
                enableFramesPerSecond = map.booleanValue(
                    "enableFramesPerSecond",
                    options.enableFramesPerSecond,
                ),
            )
        }
        if (map.has("enableBatteryLevel")) {
            options = options.copy(
                enableBatteryLevel = map.booleanValue("enableBatteryLevel", options.enableBatteryLevel),
            )
        }
        if (map.has("enableOpenFileHandleTracking")) {
            options = options.copy(
                enableOpenFileHandleTracking = map.booleanValue(
                    "enableOpenFileHandleTracking",
                    options.enableOpenFileHandleTracking,
                ),
            )
        }
        if (map.has("enableJniReferenceCountTracking")) {
            options = options.copy(
                enableJniReferenceCountTracking = map.booleanValue(
                    "enableJniReferenceCountTracking",
                    options.enableJniReferenceCountTracking,
                ),
            )
        }
        map.objectValueOrNull("defaultMemoryChannels")?.let { memory ->
            options = options.copy(
                defaultMemoryChannels = DefaultMemoryChannels(
                    javaHeap = memory.booleanValue(
                        "managedHeap",
                        memory.booleanValue("javaHeap", options.defaultMemoryChannels.javaHeap),
                    ),
                    nativeHeap = memory.booleanValue(
                        "nativeHeap",
                        options.defaultMemoryChannels.nativeHeap,
                    ),
                    rss = memory.booleanValue(
                        "residentSetSize",
                        memory.booleanValue("rss", options.defaultMemoryChannels.rss),
                    ),
                ),
            )
        }
        map.arrayValue("additionalChannels")?.let { channels ->
            options = options.copy(
                additionalChannels = (0 until channels.length()).mapNotNull { index ->
                    channels.optJSONObject(index)?.let(::channel)
                },
            )
        }
        if (map.has("sessionJpegCapture")) {
            options = options.copy(
                sessionJpegCapture = if (map.opt("sessionJpegCapture") == false) {
                    null
                } else {
                    sessionJpegCaptureOptions(map.objectValue("sessionJpegCapture"))
                },
            )
        }
        if (map.has("touchCapture")) {
            options = options.copy(
                touchCapture = if (map.opt("touchCapture") == false) {
                    null
                } else {
                    val touch = map.objectValue("touchCapture")
                    AnsightTouchCaptureOptions(
                        moveCaptureDistanceThreshold = touch.doubleValue(
                            "moveCaptureDistanceThreshold",
                            8.0,
                        ),
                        moveCaptureFramesPerSecond = touch.intValue(
                            "moveCaptureFramesPerSecond",
                            20,
                        ),
                    )
                },
            )
        }
        if (map.has("crashCapture")) {
            options = options.copy(
                crashCapture = if (map.opt("crashCapture") == false) {
                    options.crashCapture.copy(enabled = false)
                } else {
                    val crash = map.objectValue("crashCapture")
                    AnsightCrashCaptureOptions(
                        enabled = crash.booleanValue("enabled", true),
                        studioHandoffEnabled = crash.booleanValue("studioHandoffEnabled", true),
                        offlineCaptureAttachmentEnabled = crash.booleanValue("offlineCaptureAttachmentEnabled", true),
                        maximumPendingReports = crash.intValue("maximumPendingReports", 8),
                        retentionDays = crash.intValue("retentionDays", 7),
                        maximumBreadcrumbs = crash.intValue("maximumBreadcrumbs", 64),
                        maximumTraceBytes = crash.intValue("maximumTraceBytes", 1_048_576),
                    )
                },
            )
        }
        map.stringValue("toolGuard")?.let { options = options.copy(toolGuard = toolGuard(it)) }
        map.objectValueOrNull("customProperties")?.let {
            options = options.copy(customProperties = it.toGroupedStringMap())
        }
        map.objectValueOrNull("hostAutoProbe")?.let { autoProbe ->
            options = options.copy(
                hostAutoProbe = AnsightHostAutoProbeOptions(
                    enabled = autoProbe.booleanValue("enabled", options.hostAutoProbe.enabled),
                    initialDelayMilliseconds = autoProbe.longValue(
                        "initialDelayMilliseconds",
                        options.hostAutoProbe.initialDelayMilliseconds,
                    ),
                    probeIntervalMilliseconds = autoProbe.longValue(
                        "probeIntervalMilliseconds",
                        options.hostAutoProbe.probeIntervalMilliseconds,
                    ),
                    reconnectDelayMilliseconds = autoProbe.longValue(
                        "reconnectDelayMilliseconds",
                        options.hostAutoProbe.reconnectDelayMilliseconds,
                    ),
                    clientName = autoProbe.stringValue("clientName") ?: options.hostAutoProbe.clientName,
                ),
            )
        }
        map.objectValueOrNull("hostConnection")?.let { host ->
            options = options.copy(
                hostConnection = AnsightHostConnectionOptions(
                    savedConfigKey = host.stringValue("savedConfigKey")
                        ?: options.hostConnection.savedConfigKey,
                    bundledConfigJson = host.stringValue("bundledConfigJson")
                        ?: options.hostConnection.bundledConfigJson,
                    discoveryPort = host.optionalInt("discoveryPort")
                        ?: options.hostConnection.discoveryPort,
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
        map.objectValueOrNull("secureStorage")?.let { secure ->
            options = options.copy(
                secureStorage = AnsightSecureStorageOptions(
                    preferencesName = secure.stringValue("preferencesName")
                        ?: options.secureStorage.preferencesName,
                    allowedKeys = secure.stringSet("allowedKeys"),
                    allowedPrefixes = secure.stringSet("allowedPrefixes"),
                ),
            )
        }
        return options.withNativeTools(map, useDefaults)
    }

    private fun AnsightOptions.withNativeTools(map: JSObject, enableVisualTreeByDefault: Boolean): AnsightOptions {
        val remote = map.objectValue("remoteTools")
        val builder = AnsightOptions.createBuilder(this)
        if (remote.toolSuiteEnabled("visualTree", enableVisualTreeByDefault)) {
            builder.withVisualTreeTools()
        }
        val database = remote.objectValue("database")
        builder.withDatabaseTools(
            AndroidDatabaseToolsOptions(
                additionalRoots = roots(database.arrayValue("additionalRoots")).map {
                    AndroidDatabaseRoot(it.first, it.second)
                },
                includePlatformRoots = database.booleanValue("includePlatformRoots", true),
            ).validated(),
        )
        val files = remote.objectValue("fileSystem")
        builder.withFileSystemTools(
            AndroidFileSystemToolsOptions(
                additionalRoots = roots(files.arrayValue("additionalRoots")).map {
                    AndroidFileSystemRoot(it.first, it.second)
                },
            ).validated(),
        )
        val preferences = remote.objectValue("preferences")
        builder.withPreferencesTools(
            AndroidPreferencesToolsOptions(
                defaultStore = preferences.stringValue("defaultStore"),
                allowedStores = preferences.stringSet("allowedStores"),
                allowedKeys = preferences.stringSet("allowedKeys"),
                allowedKeyPrefixes = preferences.stringSet("allowedKeyPrefixes"),
            ).validated(),
        )
        val reflection = remote.objectValue("reflection")
        builder.withReflectionTools(
            AndroidReflectionToolsOptions(
                includeBuiltInRoots = reflection.booleanValue("includeBuiltInRoots", true),
                allowedRootIds = reflection.stringSet("allowedRootIds"),
                allowedTypePrefixes = reflection.stringSet("allowedTypePrefixes"),
            ).validated(),
        )
        val secure = remote.objectValueOrNull("secureStorage") ?: map.objectValue("secureStorage")
        builder.withSecureStorageTools(
            AnsightSecureStorageOptions(
                preferencesName = secure.stringValue("preferencesName") ?: secureStorage.preferencesName,
                allowedKeys = secure.stringSet("allowedKeys"),
                allowedPrefixes = secure.stringSet("allowedKeyPrefixes") + secure.stringSet("allowedPrefixes"),
            ).validated(),
        )
        return builder.build()
    }

    private fun toolDefinition(map: JSObject): ToolDefinition =
        ToolDefinition(
            id = map.stringValue("id") ?: "",
            name = map.stringValue("name") ?: map.stringValue("id") ?: "",
            description = map.stringValue("description") ?: "",
            category = map.stringValue("category") ?: "custom",
            scope = toolScope(map.stringValue("scope")),
            keywords = map.stringValue("keywords")
                ?: map.stringList("keywords").joinToString(" ").ifBlank { "capacitor javascript custom tool" },
            argumentsSchema = schema(map.objectValueOrNull("argumentsSchema")),
            resultSchema = schema(map.objectValueOrNull("resultSchema")),
            security = toolSecurity(map.objectValueOrNull("security")),
        ).validated()

    private fun schema(map: JSObject?): ToolSchema {
        if (map == null) return ToolSchema.obj(additionalProperties = true)
        val properties = mutableMapOf<String, ToolSchema>()
        map.objectValueOrNull("properties")?.let { values ->
            values.keys().forEach { key ->
                values.objectValueOrNull(key)?.let { properties[key] = schema(it) }
            }
        }
        return ToolSchema(
            type = map.stringValue("type") ?: map.stringList("type").firstOrNull { it != "null" } ?: "object",
            description = map.stringValue("description"),
            properties = properties,
            required = map.stringList("required"),
            items = map.objectValueOrNull("items")?.let(::schema),
            enumValues = map.stringList("enum"),
            additionalProperties = map.booleanValue("additionalProperties", false),
            nullable = "null" in map.stringList("type"),
            format = map.stringValue("format"),
        )
    }

    private fun toolSecurity(map: JSObject?): ToolSecurity {
        if (map == null) return ToolSecurity.Unspecified
        return ToolSecurity(
            level = when (map.stringValue("level")?.lowercase()) {
                "medium", "moderate" -> ToolSecurityLevel.Medium
                "high" -> ToolSecurityLevel.High
                "critical" -> ToolSecurityLevel.Critical
                else -> ToolSecurityLevel.Low
            },
            implications = map.stringList("implications"),
        )
    }

    private fun resultPayload(map: JSObject): JSONObject? {
        if (!map.has("result") || map.isNull("result")) return null
        return when (val value = map.opt("result")) {
            is JSONObject -> value
            is JSONArray -> JSONObject().put("value", value)
            else -> JSONObject().put("value", value)
        }
    }

    private fun snapshot(): JSObject {
        val value = AnsightRuntime.snapshot()
        return JSObject()
            .putValue("initialized", value.initialized)
            .putValue("active", value.active)
            .putValue("sessionOpen", value.sessionOpen)
            .putValue("lifecycleState", value.lifecycleState.wireName)
            .putValue("lifecycleChangedAtUtc", value.lifecycleChangedAtUtc)
            .putValue("metricsRecorded", value.metricsRecorded)
            .putValue("eventsRecorded", value.eventsRecorded)
            .putValue("touchesRecorded", value.touchesRecorded)
            .putValue("registeredTools", value.registeredTools)
            .putValue("sessionMessage", value.sessionMessage)
            .putValue("connectionStatus", hostConnectionStatus(value.connectionStatus))
            .putValue("channels", JSONArray(value.channels.map(::channel)))
            .apply {
                value.lastMetric?.let { put("lastMetric", metric(it)) }
                value.lastEvent?.let { put("lastEvent", event(it)) }
                value.currentScreen?.let {
                    put(
                        "currentScreen",
                        JSObject()
                            .putValue("name", it.name)
                            .putValue("capturedAtUtc", it.capturedAtUtc)
                            .putValue("details", it.details.toJSObject()),
                    )
                }
            }
    }

    private fun hostConnectionStatus(value: HostConnectionStatus): JSObject =
        JSObject()
            .putValue("isRuntimeActive", value.isRuntimeActive)
            .putValue("isConnected", value.isConnected)
            .putValue("connectionState", value.connectionState.name)
            .putValue("hasCachedSession", value.hasCachedSession)
            .putValue("hasSavedConfig", value.hasSavedConfig)
            .putValue("hasBundledConfig", value.hasBundledConfig)
            .putValue("summaryKind", value.summaryKind.name)
            .putValue("summaryMessage", value.summaryMessage)

    private fun hostConnectionCapabilities(value: HostConnectionCapabilities): JSObject =
        JSObject()
            .putValue("canConnectUsingSavedConfig", value.canConnectUsingSavedConfig)
            .putValue("canConnectUsingBundledConfig", value.canConnectUsingBundledConfig)
            .putValue("canChooseConfigFile", value.canChooseConfigFile)
            .putValue("canScanConfigQrCode", value.canScanConfigQrCode)
            .putValue("canClearSavedConfigs", value.canClearSavedConfigs)

    private fun openSessionResult(value: OpenSessionResult): JSObject =
        operationResult(OperationResult(value.success, value.message))
            .putValue("accepted", value.accepted)
            .putValue("sessionId", value.sessionId)
            .putValue("configId", value.configId)
            .putValue("appId", value.appId)
            .putValue("resolvedHostAddress", value.resolvedHostAddress)
            .putValue("discoverySource", value.discoverySource)
            .putValue("reasonCode", value.reasonCode)
            .putValue("hostId", value.hostId)
            .putValue("hostName", value.hostName)

    private fun hostConnectionResult(value: HostConnectionResult): JSObject =
        JSObject()
            .putValue("success", value.success)
            .putValue("message", value.message)
            .putValue("kind", value.kind.name)
            .putValue("source", value.source.name)
            .putValue("reasonCode", value.reasonCode ?: value.openSession?.reasonCode)
            .apply {
                value.openSession?.let {
                    put("sessionId", it.sessionId)
                    put("configId", it.configId)
                    put("appId", it.appId)
                    put("resolvedHostAddress", it.resolvedHostAddress)
                    put("hostId", it.hostId)
                    put("hostName", it.hostName)
                    put("accepted", it.accepted)
                    put("discoverySource", it.discoverySource)
                }
            }

    private fun operationResult(value: OperationResult): JSObject =
        JSObject().putValue("success", value.success).putValue("message", value.message)

    private fun options(value: AnsightOptions): JSObject =
        JSObject()
            .putValue("sampleFrequencyMilliseconds", value.sampleFrequencyMilliseconds)
            .putValue("retentionPeriodSeconds", value.retentionPeriodSeconds)
            .putValue("enableFramesPerSecond", value.enableFramesPerSecond)
            .putValue("enableBatteryLevel", value.enableBatteryLevel)
            .putValue("enableOpenFileHandleTracking", value.enableOpenFileHandleTracking)
            .putValue("enableJniReferenceCountTracking", value.enableJniReferenceCountTracking)
            .putValue("toolGuard", toolGuardName(value.toolGuard))
            .putValue("customProperties", value.customProperties.toGroupedJSObject())
            .putValue("additionalChannels", JSONArray(value.additionalChannels.map(::channel)))

    private fun channel(value: JSONObject): AnsightChannel =
        AnsightChannel(
            id = value.optInt("id", -1),
            name = value.optString("name"),
            unit = value.stringValue("unit"),
            type = value.stringValue("type") ?: "custom",
            colorHex = value.stringValue("colorHex"),
            source = value.stringValue("source"),
            group = value.stringValue("group"),
            kind = value.stringValue("kind"),
        )

    private fun channel(value: AnsightChannel): JSObject =
        JSObject()
            .putValue("id", value.id)
            .putValue("name", value.name)
            .putValue("unit", value.unit)
            .putValue("type", value.type)
            .putValue("colorHex", value.colorHex)
            .putValue("source", value.source)
            .putValue("group", value.group)
            .putValue("kind", value.kind)

    private fun metric(value: RecordedMetric): JSObject =
        JSObject()
            .putValue("value", value.value)
            .putValue("capturedAtUtc", value.capturedAtUtc)
            .putValue("capturedAtEpochMs", value.capturedAtEpochMs)
            .putValue("channel", value.channel)
            .putValue("sequence", value.sequence)

    private fun event(value: RecordedEvent): JSObject =
        JSObject()
            .putValue("id", value.id)
            .putValue("label", value.label)
            .putValue("type", value.type.wireName)
            .putValue("details", value.details)
            .putValue("capturedAtUtc", value.capturedAtUtc)
            .putValue("capturedAtEpochMs", value.capturedAtEpochMs)
            .putValue("externalId", value.externalId)
            .putValue("channel", value.channel)
            .putValue("sequence", value.sequence)

    private fun pairingOpenOptions(map: JSObject): PairingOpenOptions =
        PairingOpenOptions(
            clientName = map.stringValue("clientName") ?: "Capacitor",
            expectedAppId = map.stringValue("expectedAppId"),
            hostAddressOverride = map.stringValue("hostAddressOverride"),
        )

    private fun sessionJpegCaptureOptions(map: JSObject): AnsightSessionJpegCaptureOptions =
        AnsightSessionJpegCaptureOptions(
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
            mode = if (map.stringValue("mode") == "screenshotAndVisualTree") {
                AnsightSessionJpegCaptureMode.ScreenshotAndVisualTree
            } else {
                AnsightSessionJpegCaptureMode.ScreenshotOnly
            },
        )

    private fun application(): Application =
        context.applicationContext as? Application
            ?: error("Capacitor application context is not an Android Application.")

    private fun bindCurrentActivity() {
        activity?.let { AnsightRuntime.bindActivity(it) }
    }

    private fun resolve(call: PluginCall, block: () -> JSObject) {
        runCatching(block).fold(
            onSuccess = call::resolve,
            onFailure = { call.reject(it.message ?: "Ansight operation failed.", "ansight_error", it as? Exception) },
        )
    }

    private fun background(call: PluginCall, block: () -> JSObject) {
        executor.execute { resolve(call, block) }
    }
}

private fun JSObject.putValue(key: String, value: Any?): JSObject = apply {
    if (value == null) put(key, JSONObject.NULL) else put(key, value)
}

private fun JSObject.stringValue(key: String): String? =
    optString(key).takeIf { has(key) && !isNull(key) && it.isNotBlank() }

private fun JSONObject.stringValue(key: String): String? =
    optString(key).takeIf { has(key) && !isNull(key) && it.isNotBlank() }

private fun JSObject.booleanValue(key: String, default: Boolean): Boolean =
    if (has(key) && !isNull(key)) optBoolean(key, default) else default

private fun JSObject.intValue(key: String, default: Int): Int =
    if (has(key) && !isNull(key)) optInt(key, default) else default

private fun JSObject.longValue(key: String, default: Long): Long =
    if (has(key) && !isNull(key)) optLong(key, default) else default

private fun JSObject.doubleValue(key: String, default: Double): Double =
    if (has(key) && !isNull(key)) optDouble(key, default) else default

private fun JSObject.optionalInt(key: String): Int? =
    if (has(key) && !isNull(key)) optInt(key) else null

private fun JSObject.objectValue(key: String): JSObject =
    objectValueOrNull(key) ?: JSObject()

private fun JSObject.objectValueOrNull(key: String): JSObject? {
    val value = optJSONObject(key) ?: return null
    return if (value is JSObject) value else JSObject(value.toString())
}

private fun JSObject.arrayValue(key: String): JSONArray? = optJSONArray(key)

private fun JSObject.stringList(key: String): List<String> {
    val array = optJSONArray(key) ?: return emptyList()
    return (0 until array.length()).mapNotNull { array.optString(it).takeIf(String::isNotBlank) }
}

private fun JSObject.stringSet(key: String): Set<String> = stringList(key).toSet()

private fun JSObject.toolSuiteEnabled(key: String, default: Boolean): Boolean =
    when (val value = opt(key)) {
        is Boolean -> value
        is JSONObject -> value.optBoolean("enabled", true)
        else -> default
    }

private fun JSObject.toStringMap(): Map<String, String> =
    keys().asSequence().associateWith { key -> opt(key)?.takeUnless { it == JSONObject.NULL }?.toString().orEmpty() }

private fun JSObject.toGroupedStringMap(): Map<String, Map<String, String>> =
    keys().asSequence().mapNotNull { key ->
        objectValueOrNull(key)?.let { key to it.toStringMap() }
    }.toMap()

private fun Map<String, *>.toJSObject(): JSObject =
    JSObject().also { result -> forEach { (key, value) -> result.putValue(key, value) } }

private fun Map<String, Map<String, String>>.toGroupedJSObject(): JSObject =
    JSObject().also { result -> forEach { (key, value) -> result.put(key, value.toJSObject()) } }

private fun roots(array: JSONArray?): List<Pair<String, String>> {
    if (array == null) return emptyList()
    return (0 until array.length()).mapNotNull { index ->
        val value = array.optJSONObject(index) ?: return@mapNotNull null
        val alias = value.stringValue("alias") ?: return@mapNotNull null
        val path = value.stringValue("path") ?: return@mapNotNull null
        alias to path
    }
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

private fun toolGuard(raw: String): AnsightToolGuard =
    when (raw.trim().lowercase()) {
        "readonly", "read_only", "read" -> AnsightToolGuard.ReadOnly
        "readwrite", "read_write", "write" -> AnsightToolGuard.ReadWrite
        "full", "fullaccess", "full_access" -> AnsightToolGuard.FullAccess
        else -> AnsightToolGuard.Disabled
    }

private fun toolGuardName(value: AnsightToolGuard): String =
    when (value) {
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
