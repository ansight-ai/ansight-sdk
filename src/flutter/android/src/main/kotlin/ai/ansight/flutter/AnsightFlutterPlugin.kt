package ai.ansight.flutter

import ai.ansight.Ansight
import ai.ansight.pairing.AnsightPairing
import ai.ansight.runtime.AndroidToolExecutionContext
import ai.ansight.runtime.AndroidToolResult
import ai.ansight.runtime.AnsightChannel
import ai.ansight.runtime.AnsightChannels
import ai.ansight.runtime.AnsightCrashCaptureOptions
import ai.ansight.runtime.AnsightDeveloperMode
import ai.ansight.runtime.AnsightHostAutoProbeOptions
import ai.ansight.runtime.AnsightHostConnectionOptions
import ai.ansight.runtime.AnsightLogCallback
import ai.ansight.runtime.AnsightLogger
import ai.ansight.runtime.AnsightNetworkHeader
import ai.ansight.runtime.AnsightNetworkBody
import ai.ansight.runtime.AnsightNetworkRequest
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
import ai.ansight.runtime.HostConnectionRequest
import ai.ansight.runtime.HostConnectionRequestKind
import ai.ansight.runtime.HostConnectionResult
import ai.ansight.runtime.HostConnectionStatus
import ai.ansight.runtime.HostConnectionStatusSubscription
import ai.ansight.runtime.OperationResult
import ai.ansight.runtime.OpenSessionResult
import ai.ansight.runtime.PairingFileTransferWireProtocol
import ai.ansight.runtime.PairingOpenOptions
import ai.ansight.runtime.RecordedEvent
import ai.ansight.runtime.RecordedMetric
import ai.ansight.runtime.ToolDefinition
import ai.ansight.runtime.ToolSchema
import ai.ansight.runtime.ToolPolicy
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
import ai.ansight.tools.visualtree.AndroidVisualTreeProvider
import ai.ansight.tools.visualtree.AndroidVisualTreeProviderRegistry
import ai.ansight.tools.visualtree.AndroidVisualTreeInteractionProvider
import ai.ansight.tools.visualtree.AndroidVisualTreeActionRequest
import ai.ansight.tools.visualtree.withVisualTreeTools
import android.app.Activity
import android.app.Application
import android.content.Context
import android.os.Handler
import android.os.Looper
import io.flutter.embedding.engine.plugins.FlutterPlugin
import io.flutter.embedding.engine.plugins.activity.ActivityAware
import io.flutter.embedding.engine.plugins.activity.ActivityPluginBinding
import org.json.JSONArray
import org.json.JSONObject
import java.util.Locale
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.CountDownLatch
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicReference

class AnsightFlutterPlugin : FlutterPlugin, ActivityAware, AnsightNativeHostApi {
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
    private var application: Application? = null
    private var activity: Activity? = null
    private var dartApi: AnsightDartApi? = null
    private var messenger: io.flutter.plugin.common.BinaryMessenger? = null
    private var hostConnectionStatusSubscription: HostConnectionStatusSubscription? = null

    private val logCallback = AnsightLogCallback { level, message, throwable ->
        val payload = JSONObject()
            .putValue("level", level.name.lowercase(Locale.US))
            .putValue("message", message)
            .putValue("platform", "android")
            .putValue("error", throwable?.message)
        sendEvent("log", payload)
    }

    override fun onAttachedToEngine(binding: FlutterPlugin.FlutterPluginBinding) {
        application = binding.applicationContext.applicationContext as? Application
            ?: error("Flutter application context is not an Android Application.")
        messenger = binding.binaryMessenger
        dartApi = AnsightDartApi(binding.binaryMessenger)
        AnsightNativeHostApi.setUp(binding.binaryMessenger, this)
        AnsightLogger.registerCallback(logCallback)
        hostConnectionStatusSubscription = AnsightRuntime.addHostConnectionStatusListener(
            listener = { status, _ -> sendEvent("connectionStatus", hostConnectionStatus(status)) },
        )
    }

    override fun onDetachedFromEngine(binding: FlutterPlugin.FlutterPluginBinding) {
        AnsightLogger.removeCallback(logCallback)
        hostConnectionStatusSubscription?.remove()
        hostConnectionStatusSubscription = null
        AnsightNativeHostApi.setUp(binding.binaryMessenger, null)
        pendingToolCalls.values.forEach { it.latch.countDown() }
        pendingToolCalls.clear()
        dartApi = null
        messenger = null
        application = null
    }

    override fun onAttachedToActivity(binding: ActivityPluginBinding) {
        activity = binding.activity
        bindCurrentActivity()
    }

    override fun onDetachedFromActivityForConfigChanges() {
        activity = null
    }

    override fun onReattachedToActivityForConfigChanges(binding: ActivityPluginBinding) {
        onAttachedToActivity(binding)
    }

    override fun onDetachedFromActivity() {
        activity = null
    }

    override fun invoke(
        method: String,
        argumentsJson: String?,
        callback: (Result<String>) -> Unit,
    ) {
        executor.execute {
            runCatching {
                dispatch(method, argumentsJson?.let(::JSONObject) ?: JSONObject()).toString()
            }.fold(
                onSuccess = { callback(Result.success(it)) },
                onFailure = { callback(Result.failure(it)) },
            )
        }
    }

    override fun queueBinaryTransfer(
        requestId: String,
        data: ByteArray,
        chunkBytes: Long,
        callback: (Result<String>) -> Unit,
    ) {
        executor.execute {
            runCatching {
                queueTransfer(requestId, data, chunkBytes.toInt()).toString()
            }.fold(
                onSuccess = { callback(Result.success(it)) },
                onFailure = { callback(Result.failure(it)) },
            )
        }
    }

    override fun recordNetworkRequest(
        request: AnsightNetworkRequestMessage,
        callback: (Result<String>) -> Unit,
    ) {
        executor.execute {
            runCatching {
                if (request.schema != AnsightNetworkRequest.SchemaName) {
                    return@runCatching operationResult(
                        OperationResult.failure("Network request must use the ansight.network-request.v1 schema."),
                    ).toString()
                }
                operationResult(
                    AnsightRuntime.recordNetworkRequest(
                        AnsightNetworkRequest(
                            id = request.id,
                            source = request.source,
                            startedAtUtc = request.startedAtUtc,
                            completedAtUtc = request.completedAtUtc,
                            durationMilliseconds = request.durationMilliseconds,
                            method = request.method,
                            url = request.url,
                            protocol = request.protocolName,
                            requestHeaders = request.requestHeaders.map {
                                AnsightNetworkHeader(it.name, it.value)
                            },
                            requestBodySizeBytes = request.requestBodySizeBytes,
                            requestBody = request.requestBody?.let {
                                AnsightNetworkBody(
                                    contentType = it.contentType,
                                    encoding = it.encoding,
                                    data = it.data,
                                    capturedBytes = it.capturedBytes,
                                    totalBytes = it.totalBytes,
                                    truncated = it.truncated,
                                )
                            },
                            statusCode = request.statusCode?.toInt(),
                            reasonPhrase = request.reasonPhrase,
                            responseHeaders = request.responseHeaders.map {
                                AnsightNetworkHeader(it.name, it.value)
                            },
                            responseBodySizeBytes = request.responseBodySizeBytes,
                            responseBody = request.responseBody?.let {
                                AnsightNetworkBody(
                                    contentType = it.contentType,
                                    encoding = it.encoding,
                                    data = it.data,
                                    capturedBytes = it.capturedBytes,
                                    totalBytes = it.totalBytes,
                                    truncated = it.truncated,
                                )
                            },
                            errorType = request.errorType,
                            errorMessage = request.errorMessage,
                        ),
                    ),
                ).toString()
            }.fold(
                onSuccess = { callback(Result.success(it)) },
                onFailure = { callback(Result.failure(it)) },
            )
        }
    }

    private fun dispatch(method: String, map: JSONObject): JSONObject = when (method) {
        "initialize" -> {
            Ansight.initialize(requireApplication(), buildOptions(map))
            installRegisteredCustomTools()
            snapshot()
        }
        "initializeAndActivate" -> {
            Ansight.initializeAndActivate(requireApplication(), buildOptions(map))
            bindCurrentActivity()
            installRegisteredCustomTools()
            snapshot()
        }
        "activate" -> {
            AnsightRuntime.activate()
            bindCurrentActivity()
            snapshot()
        }
        "deactivate" -> {
            AnsightRuntime.deactivate()
            snapshot()
        }
        "clear" -> {
            AnsightRuntime.clear()
            snapshot()
        }
        "registerMetricChannel" -> {
            AnsightRuntime.registerMetricChannel(channel(map))
            snapshot()
        }
        "recordMetric" -> {
            AnsightRuntime.metric(
                map.doubleValue("value", 0.0).toLong(),
                map.intValue("channel", AnsightChannels.Unspecified),
            )
            snapshot()
        }
        "recordEvent" -> {
            AnsightRuntime.event(
                map.stringValue("label").orEmpty(),
                eventType(map.stringValue("type")),
                map.stringValue("details"),
                map.intValue("channel", AnsightChannels.Unspecified),
            )
            snapshot()
        }
        "recordCrashCandidate" -> JSONObject()
            .putValue(
                "candidateId",
                AnsightRuntime.recordCrashCandidate(
                    runtime = map.stringValue("runtime") ?: "flutter-dart",
                    kind = map.stringValue("kind") ?: "unhandled_flutter_error",
                    message = map.stringValue("message"),
                    stack = map.stringValue("stack"),
                    fatal = map.booleanValue("fatal", false),
                    metadataJson = map.objectValueOrNull("metadata")?.toString(),
                ),
            )
        "screenViewed" -> {
            AnsightRuntime.screenViewed(
                map.stringValue("name").orEmpty(),
                map.objectValue("details").toStringMap(),
            )
            snapshot()
        }
        "setAppLifecycleState" -> {
            AnsightRuntime.setAppLifecycleState(lifecycleState(map.stringValue("state").orEmpty()))
            snapshot()
        }
        "connect" -> hostConnectionResult(
            AnsightRuntime.connect(
                HostConnectionRequest(
                    kind = if (map.stringValue("pairingPayload").isNullOrBlank()) {
                        HostConnectionRequestKind.Auto
                    } else {
                        HostConnectionRequestKind.Payload
                    },
                    payload = map.stringValue("pairingPayload"),
                    clientName = map.stringValue("clientName"),
                    expectedAppId = map.stringValue("expectedAppId"),
                    hostAddressOverride = map.stringValue("hostAddressOverride"),
                ),
            ),
        )
        "scanPairingQrCode" -> scanPairingQrCode(map)
        "openSession" -> openSessionResult(
            AnsightRuntime.openSession(
                map.stringValue("pairingPayload").orEmpty(),
                PairingOpenOptions(
                    clientName = map.stringValue("clientName") ?: "Flutter",
                    expectedAppId = map.stringValue("expectedAppId"),
                    hostAddressOverride = map.stringValue("hostAddressOverride"),
                ),
            ),
        )
        "disconnect" -> hostConnectionResult(AnsightRuntime.disconnect())
        "completeSession" -> {
            AnsightRuntime.completeSession()
            operationResult(OperationResult.success("Session completed."))
        }
        "closeSession" -> {
            AnsightRuntime.closeSession()
            operationResult(OperationResult.success("Session closed."))
        }
        "savePairingConfig" -> hostConnectionResult(
            AnsightRuntime.savePairingConfig(
                map.stringValue("pairingPayload").orEmpty(),
                map.stringValue("expectedAppId"),
            ),
        )
        "clearSavedPairing" -> hostConnectionResult(AnsightRuntime.clearSavedPairingConfig())
        "clearCachedSession" -> operationResult(AnsightRuntime.clearCachedSession())
        "notifyHostConnectionConfigChanged" ->
            hostConnectionResult(AnsightRuntime.notifyHostConnectionConfigChanged())
        "status", "snapshot" -> snapshot()
        "hostConnectionStatus" -> hostConnectionStatus(AnsightRuntime.hostConnectionStatus())
        "hostConnectionCapabilities" ->
            hostConnectionCapabilities(AnsightRuntime.hostConnectionCapabilities())
        "currentOptions" -> options(AnsightRuntime.options())
        "recordedMetrics" -> JSONObject().put(
            "items",
            JSONArray(AnsightRuntime.recordedMetrics().takeLastLimit(map.intValue("limit", 0)).map(::metric)),
        )
        "recordedEvents" -> JSONObject().put(
            "items",
            JSONArray(AnsightRuntime.recordedEvents().takeLastLimit(map.intValue("limit", 0)).map(::event)),
        )
        "sendClientLog" ->
            operationResult(AnsightRuntime.sendClientLog(map.stringValue("line").orEmpty()))
        "captureBuiltInTelemetrySample" -> {
            AnsightRuntime.captureBuiltInTelemetrySample()
            snapshot()
        }
        "isFramesPerSecondEnabled" ->
            JSONObject().put("value", AnsightRuntime.isFramesPerSecondEnabled())
        "enableFramesPerSecond" -> {
            AnsightRuntime.enableFramesPerSecond()
            snapshot()
        }
        "disableFramesPerSecond" -> {
            AnsightRuntime.disableFramesPerSecond()
            snapshot()
        }
        "captureScreenFrame" -> {
            bindCurrentActivity()
            operationResult(AnsightRuntime.captureScreenFrame(sessionJpegCaptureOptions(map)))
        }
        "enableTouchCapture" -> operationResult(AnsightRuntime.enableTouchCapture())
        "disableTouchCapture" -> operationResult(AnsightRuntime.disableTouchCapture())
        "updateSessionProperties" -> operationResult(
            AnsightRuntime.updateCustomProperties(map.objectValue("properties").toGroupedStringMap()),
        )
        "clearSessionProperties" -> operationResult(AnsightRuntime.clearCustomProperties())
        "registerCustomProperty" -> operationResult(
            AnsightRuntime.registerCustomProperty(
                map.stringValue("group").orEmpty(),
                map.stringValue("key").orEmpty(),
                map.stringValue("value").orEmpty(),
            ),
        )
        "removeCustomProperty" -> operationResult(
            AnsightRuntime.removeCustomProperty(
                map.stringValue("group").orEmpty(),
                map.stringValue("key").orEmpty(),
            ),
        )
        "registerCustomTool" -> registerCustomTool(map.objectValue("definition"))
        "unregisterCustomTool" -> unregisterCustomTool(map.stringValue("id").orEmpty())
        "clearRegisteredCustomTools" -> clearRegisteredCustomTools()
        "resolveToolCall" -> resolveToolCall(map)
        "enableFlutterVisualTreeProvider" -> enableFlutterVisualTreeProvider()
        else -> error("Unknown Ansight Flutter method '$method'.")
    }

    private fun registerCustomTool(map: JSONObject): JSONObject {
        val definition = toolDefinition(map)
        val registration = CustomToolRegistration(
            definition,
            map.intValue("timeoutMilliseconds", 30_000).toLong().coerceAtLeast(250L),
        )
        customToolRegistrations[definition.id] = registration
        activeCustomToolIds.add(definition.id)
        if (AnsightRuntime.snapshot().initialized) installCustomTool(registration)
        return operationResult(OperationResult.success("Dart tool registered."))
            .putValue("id", definition.id)
    }

    private fun unregisterCustomTool(id: String): JSONObject {
        val normalized = id.trim()
        activeCustomToolIds.remove(normalized)
        customToolRegistrations.remove(normalized)
        return operationResult(OperationResult.success("Dart tool unregistered."))
            .putValue("id", normalized)
    }

    private fun clearRegisteredCustomTools(): JSONObject {
        activeCustomToolIds.clear()
        customToolRegistrations.clear()
        return operationResult(OperationResult.success("Dart tools cleared."))
    }

    private fun resolveToolCall(map: JSONObject): JSONObject {
        val pending = pendingToolCalls[map.stringValue("requestId").orEmpty()]
            ?: return operationResult(OperationResult.failure("Tool request is no longer pending."))
                .putValue("accepted", false)
        val result = map.objectValue("result")
        val payload = resultPayload(result)
        pending.result = if (result.booleanValue("success", true)) {
            AndroidToolResult.success(payload, result.stringValue("message"))
        } else {
            AndroidToolResult.failure(
                result.stringValue("message") ?: "Dart tool failed.",
                result.stringValue("errorCode"),
                payload,
            )
        }
        pending.latch.countDown()
        return operationResult(OperationResult.success("Tool result accepted."))
            .putValue("accepted", true)
    }

    private fun executeDartTool(
        toolId: String,
        arguments: Map<String, String>,
        context: AndroidToolExecutionContext,
        timeoutMilliseconds: Long,
        requireRegistration: Boolean = true,
    ): AndroidToolResult {
        if (requireRegistration && toolId !in activeCustomToolIds) {
            return AndroidToolResult.failure(
                "Tool '$toolId' is no longer registered in Dart.",
                "dart_tool_unregistered",
            )
        }
        val requestId = "android.flutter.${UUID.randomUUID().toString().replace("-", "")}"
        val pending = PendingToolCall(context)
        pendingToolCalls[requestId] = pending
        val request = JSONObject()
            .putValue("requestId", requestId)
            .putValue("toolId", toolId)
            .putValue("platform", "android")
            .putValue("sessionId", context.sessionId)
            .putValue("nativeRequestId", context.requestId)
            .putValue("arguments", arguments.toJSONObject())
        mainHandler.post {
            dartApi?.onToolCall(request.toString()) { result ->
                result.exceptionOrNull()?.let {
                    pending.result = AndroidToolResult.failure(
                        "Could not dispatch Dart tool '$toolId': ${it.message}",
                        "dart_tool_dispatch_failed",
                    )
                    pending.latch.countDown()
                }
            }
        }
        val completed = pending.latch.await(timeoutMilliseconds, TimeUnit.MILLISECONDS)
        pendingToolCalls.remove(requestId)
        if (!completed) {
            return AndroidToolResult.failure(
                "Dart handler for '$toolId' timed out.",
                "dart_tool_timeout",
            )
        }
        return pending.result ?: AndroidToolResult.failure(
            "Dart handler returned no result.",
            "dart_tool_result_missing",
        )
    }

    private fun installRegisteredCustomTools() {
        customToolRegistrations.values.forEach(::installCustomTool)
    }

    private fun installCustomTool(registration: CustomToolRegistration) {
        activeCustomToolIds.add(registration.definition.id)
        AnsightRuntime.registerTool(
            FunctionAndroidTool(registration.definition) { arguments, context ->
                executeDartTool(
                    registration.definition.id,
                    arguments,
                    context,
                    registration.timeoutMilliseconds,
                )
            },
            replaceExisting = true,
        )
    }

    private fun enableFlutterVisualTreeProvider(): JSONObject {
        AndroidVisualTreeProviderRegistry.register(
            object : AndroidVisualTreeProvider, AndroidVisualTreeInteractionProvider {
                override val source = "flutter"
                override val displayName = "Flutter"

                override fun getVisualTree(
                    arguments: Map<String, String>,
                    context: AndroidToolExecutionContext,
                ): AndroidToolResult = executeDartTool(
                    "__ansight_flutter.visual_tree",
                    arguments,
                    context,
                    30_000,
                    requireRegistration = false,
                )

                override fun inspectNode(
                    arguments: Map<String, String>,
                    context: AndroidToolExecutionContext,
                ): AndroidToolResult = executeDartTool(
                    "__ansight_flutter.inspect_node",
                    arguments,
                    context,
                    30_000,
                    requireRegistration = false,
                )

                override fun performAction(
                    request: AndroidVisualTreeActionRequest,
                    context: AndroidToolExecutionContext,
                ): AndroidToolResult = executeDartTool(
                    "__ansight_flutter.perform_action",
                    buildMap {
                        put("nodeId", request.nodeId)
                        put("action", request.action)
                        request.value?.let { put("value", it.toString()) }
                    },
                    context,
                    30_000,
                    requireRegistration = false,
                )
            },
            replaceExisting = true,
        )
        return operationResult(OperationResult.success("Flutter visual-tree provider registered."))
            .putValue("source", "flutter")
    }

    private fun queueTransfer(requestId: String, bytes: ByteArray, chunkBytes: Int): JSONObject {
        val pending = pendingToolCalls[requestId]
            ?: return operationResult(
                OperationResult.failure("Binary transfer requires an active Dart tool request."),
            ).putValue("errorCode", "artifact_request_unavailable")
        val transport = pending.context.transport
            ?: return operationResult(
                OperationResult.failure("Binary transfers require an active pairing session."),
            ).putValue("errorCode", "artifact_transfer_unavailable")
        val size = chunkBytes.coerceIn(1_024, 512 * 1_024)
        val transferId = PairingFileTransferWireProtocol.newTransferId()
        executor.execute { transport.sendBinaryTransfer(transferId, bytes, size) }
        return operationResult(OperationResult.success("Binary transfer queued."))
            .putValue("transferId", transferId)
            .putValue("deliveryMode", "websocket_binary")
            .putValue("wireProtocol", PairingFileTransferWireProtocol.ProtocolName)
            .putValue("status", "queued")
            .putValue("chunkBytes", size)
            .putValue("sizeBytes", bytes.size)
    }

    private fun scanPairingQrCode(map: JSONObject): JSONObject {
        val currentActivity = activity
            ?: return hostConnectionResult(
                HostConnectionResult.failure(
                    "QR pairing is unavailable because no Android activity is available.",
                ),
            )
        val payload = AtomicReference<String?>()
        val failure = AtomicReference<Throwable?>()
        val completed = CountDownLatch(1)
        mainHandler.post {
            AnsightPairing.scanQrCode(
                activity = currentActivity,
                onPayload = {
                    payload.set(it)
                    completed.countDown()
                },
                onError = {
                    failure.set(it)
                    completed.countDown()
                },
            )
        }
        if (!completed.await(5, TimeUnit.MINUTES)) {
            return hostConnectionResult(
                HostConnectionResult.failure(
                    "QR pairing timed out before a payload was scanned.",
                ),
            )
        }
        failure.get()?.let { throw it }
        val pairingPayload = payload.get()
        if (pairingPayload.isNullOrBlank()) {
            return hostConnectionResult(
                HostConnectionResult.failure("QR pairing canceled."),
            )
        }
        return hostConnectionResult(
            AnsightRuntime.connect(
                HostConnectionRequest(
                    kind = HostConnectionRequestKind.QrCode,
                    payload = pairingPayload,
                    clientName = map.stringValue("clientName"),
                    expectedAppId = map.stringValue("expectedAppId"),
                    hostAddressOverride = map.stringValue("hostAddressOverride"),
                ),
            ),
        )
    }

    private fun buildOptions(map: JSONObject): AnsightOptions {
        val useDefaults = map.booleanValue("useNativeAllInOneDefaults", false)
        var result = if (useDefaults) {
            AnsightDeveloperMode.options(
                clientName = map.stringValue("clientName"),
            )
        } else {
            AnsightOptions()
        }
        if (map.hasValue("sampleFrequencyMilliseconds")) {
            result = result.copy(
                sampleFrequencyMilliseconds =
                    map.intValue("sampleFrequencyMilliseconds", result.sampleFrequencyMilliseconds),
            )
        }
        if (map.hasValue("retentionPeriodSeconds")) {
            result = result.copy(
                retentionPeriodSeconds =
                    map.intValue("retentionPeriodSeconds", result.retentionPeriodSeconds),
            )
        }
        if (map.hasValue("enableFramesPerSecond")) {
            result = result.copy(
                enableFramesPerSecond =
                    map.booleanValue("enableFramesPerSecond", result.enableFramesPerSecond),
            )
        }
        if (map.hasValue("enableBatteryLevel")) {
            result = result.copy(
                enableBatteryLevel =
                    map.booleanValue("enableBatteryLevel", result.enableBatteryLevel),
            )
        }
        if (map.hasValue("enableOpenFileHandleTracking")) {
            result = result.copy(
                enableOpenFileHandleTracking = map.booleanValue(
                    "enableOpenFileHandleTracking",
                    result.enableOpenFileHandleTracking,
                ),
            )
        }
        if (map.hasValue("enableJniReferenceCountTracking")) {
            result = result.copy(
                enableJniReferenceCountTracking = map.booleanValue(
                    "enableJniReferenceCountTracking",
                    result.enableJniReferenceCountTracking,
                ),
            )
        }
        map.objectValueOrNull("defaultMemoryChannels")?.let { memory ->
            result = result.copy(
                defaultMemoryChannels = DefaultMemoryChannels(
                    javaHeap = memory.booleanValue(
                        "managedHeap",
                        memory.booleanValue("javaHeap", result.defaultMemoryChannels.javaHeap),
                    ),
                    nativeHeap = memory.booleanValue(
                        "nativeHeap",
                        result.defaultMemoryChannels.nativeHeap,
                    ),
                    rss = memory.booleanValue(
                        "residentSetSize",
                        memory.booleanValue("rss", result.defaultMemoryChannels.rss),
                    ),
                ),
            )
        }
        map.arrayValue("additionalChannels")?.let { channels ->
            result = result.copy(
                additionalChannels = (0 until channels.length())
                    .mapNotNull { channels.optJSONObject(it)?.let(::channel) },
            )
        }
        if (map.has("sessionJpegCapture")) {
            result = result.copy(
                sessionJpegCapture = if (map.opt("sessionJpegCapture") == false) {
                    null
                } else {
                    sessionJpegCaptureOptions(map.objectValue("sessionJpegCapture"))
                },
            )
        }
        if (map.has("touchCapture")) {
            result = result.copy(
                touchCapture = if (map.opt("touchCapture") == false) {
                    null
                } else {
                    val touch = map.objectValue("touchCapture")
                    AnsightTouchCaptureOptions(
                        moveCaptureDistanceThreshold =
                            touch.doubleValue("moveCaptureDistanceThreshold", 8.0),
                        moveCaptureFramesPerSecond =
                            touch.intValue("moveCaptureFramesPerSecond", 20),
                    )
                },
            )
        }
        if (map.has("crashCapture")) {
            result = result.copy(
                crashCapture = if (map.opt("crashCapture") == false) {
                    result.crashCapture.copy(enabled = false)
                } else {
                    val crash = map.objectValue("crashCapture")
                    AnsightCrashCaptureOptions(
                        enabled = crash.booleanValue("enabled", true),
                        hostHandoffEnabled = crash.booleanValue("hostHandoffEnabled", true),
                        offlineCaptureAttachmentEnabled = crash.booleanValue("offlineCaptureAttachmentEnabled", true),
                        maximumPendingReports = crash.intValue("maximumPendingReports", 8),
                        retentionDays = crash.intValue("retentionDays", 7),
                        maximumBreadcrumbs = crash.intValue("maximumBreadcrumbs", 64),
                        maximumTraceBytes = crash.intValue("maximumTraceBytes", 1_048_576),
                    )
                },
            )
        }
        map.stringValue("toolGuard")?.let { result = result.copy(toolGuard = toolGuard(it)) }
        map.objectValueOrNull("customProperties")?.let {
            result = result.copy(customProperties = it.toGroupedStringMap())
        }
        map.objectValueOrNull("hostAutoProbe")?.let { probe ->
            result = result.copy(
                hostAutoProbe = AnsightHostAutoProbeOptions(
                    enabled = probe.booleanValue("enabled", result.hostAutoProbe.enabled),
                    initialDelayMilliseconds = probe.longValue(
                        "initialDelayMilliseconds",
                        result.hostAutoProbe.initialDelayMilliseconds,
                    ),
                    probeIntervalMilliseconds = probe.longValue(
                        "probeIntervalMilliseconds",
                        result.hostAutoProbe.probeIntervalMilliseconds,
                    ),
                    reconnectDelayMilliseconds = probe.longValue(
                        "reconnectDelayMilliseconds",
                        result.hostAutoProbe.reconnectDelayMilliseconds,
                    ),
                    clientName = probe.stringValue("clientName") ?: result.hostAutoProbe.clientName,
                ),
            )
        }
        map.objectValueOrNull("hostConnection")?.let { host ->
            result = result.copy(
                hostConnection = AnsightHostConnectionOptions(
                    savedConfigKey =
                        host.stringValue("savedConfigKey") ?: result.hostConnection.savedConfigKey,
                    bundledConfigJson =
                        host.stringValue("bundledConfigJson") ?: result.hostConnection.bundledConfigJson,
                    discoveryPort =
                        host.optionalInt("discoveryPort") ?: result.hostConnection.discoveryPort,
                    allowCellularConnections = host.booleanValue(
                        "allowCellularConnections",
                        result.hostConnection.allowCellularConnections,
                    ),
                    connectionProfileRetentionSeconds = host.longValue(
                        "connectionProfileRetentionSeconds",
                        result.hostConnection.connectionProfileRetentionSeconds,
                    ),
                ),
            )
        }
        map.objectValueOrNull("secureStorage")?.let { secure ->
            result = result.copy(
                secureStorage = AnsightSecureStorageOptions(
                    preferencesName =
                        secure.stringValue("preferencesName") ?: result.secureStorage.preferencesName,
                    allowedKeys = secure.stringSet("allowedKeys"),
                    allowedPrefixes =
                        secure.stringSet("allowedPrefixes") + secure.stringSet("allowedKeyPrefixes"),
                ),
            )
        }
        return result.withNativeTools(map, useDefaults)
    }

    private fun AnsightOptions.withNativeTools(
        map: JSONObject,
        enableVisualTreeByDefault: Boolean,
    ): AnsightOptions {
        val remote = map.objectValue("remoteTools")
        val builder = AnsightOptions.createBuilder(this)
        if (remote.toolSuiteEnabled("visualTree", enableVisualTreeByDefault)) {
            builder.withVisualTreeTools()
        }
        val database = remote.objectValue("database")
        builder.withDatabaseTools(
            AndroidDatabaseToolsOptions(
                additionalRoots = roots(database.arrayValue("additionalRoots"))
                    .map { AndroidDatabaseRoot(it.first, it.second) },
                includePlatformRoots = database.booleanValue("includePlatformRoots", true),
            ).validated(),
        )
        val files = remote.objectValue("fileSystem")
        builder.withFileSystemTools(
            AndroidFileSystemToolsOptions(
                additionalRoots = roots(files.arrayValue("additionalRoots"))
                    .map { AndroidFileSystemRoot(it.first, it.second) },
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
                preferencesName =
                    secure.stringValue("preferencesName") ?: secureStorage.preferencesName,
                allowedKeys = secure.stringSet("allowedKeys"),
                allowedPrefixes =
                    secure.stringSet("allowedPrefixes") + secure.stringSet("allowedKeyPrefixes"),
            ).validated(),
        )
        return builder.build()
    }

    private fun toolDefinition(map: JSONObject): ToolDefinition = ToolDefinition(
        id = map.stringValue("id").orEmpty(),
        name = map.stringValue("name") ?: map.stringValue("id").orEmpty(),
        description = map.stringValue("description").orEmpty(),
        category = map.stringValue("category") ?: "custom",
        policy = toolPolicy(map.stringValue("policy")),
        keywords = map.stringValue("keywords")
            ?: map.stringList("keywords").joinToString(" ").ifBlank { "flutter dart custom tool" },
        argumentsSchema = schema(map.objectValueOrNull("argumentsSchema")),
        resultSchema = schema(map.objectValueOrNull("resultSchema")),
        prerequisiteToolIds = map.stringList("prerequisiteToolIds"),
    ).validated()

    private fun schema(map: JSONObject?): ToolSchema {
        if (map == null || map.length() == 0) return ToolSchema.obj(additionalProperties = true)
        val properties = mutableMapOf<String, ToolSchema>()
        map.objectValueOrNull("properties")?.let { values ->
            values.keys().forEach { key ->
                values.objectValueOrNull(key)?.let { properties[key] = schema(it) }
            }
        }
        return ToolSchema(
            type = map.stringValue("type")
                ?: map.stringList("type").firstOrNull { it != "null" }
                ?: "object",
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

    private fun snapshot(): JSONObject {
        val value = AnsightRuntime.snapshot()
        return JSONObject()
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
                        JSONObject()
                            .putValue("name", it.name)
                            .putValue("capturedAtUtc", it.capturedAtUtc)
                            .putValue("details", it.details.toJSONObject()),
                    )
                }
            }
    }

    private fun hostConnectionStatus(value: HostConnectionStatus): JSONObject = JSONObject()
        .putValue("isRuntimeActive", value.isRuntimeActive)
        .putValue("isConnected", value.isConnected)
        .putValue("connectionState", value.connectionState.name)
        .putValue("hasCachedSession", value.hasCachedSession)
        .putValue("hasSavedConfig", value.hasSavedConfig)
        .putValue("hasBundledConfig", value.hasBundledConfig)
        .putValue("summaryKind", value.summaryKind.name)
        .putValue("summaryMessage", value.summaryMessage)

    private fun hostConnectionCapabilities(value: HostConnectionCapabilities): JSONObject = JSONObject()
        .putValue("canConnectUsingSavedConfig", value.canConnectUsingSavedConfig)
        .putValue("canConnectUsingBundledConfig", value.canConnectUsingBundledConfig)
        .putValue("canChooseConfigFile", value.canChooseConfigFile)
        .putValue("canScanConfigQrCode", value.canScanConfigQrCode)
        .putValue("canClearSavedConfigs", value.canClearSavedConfigs)

    private fun openSessionResult(value: OpenSessionResult): JSONObject =
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

    private fun hostConnectionResult(value: HostConnectionResult): JSONObject = JSONObject()
        .putValue("success", value.success)
        .putValue("message", value.message)
        .putValue("kind", value.kind.name)
        .putValue("source", value.source.name)
        .putValue("reasonCode", value.reasonCode ?: value.openSession?.reasonCode)
        .apply {
            value.openSession?.let {
                putValue("sessionId", it.sessionId)
                putValue("configId", it.configId)
                putValue("appId", it.appId)
                putValue("resolvedHostAddress", it.resolvedHostAddress)
                putValue("hostId", it.hostId)
                putValue("hostName", it.hostName)
                putValue("accepted", it.accepted)
                putValue("discoverySource", it.discoverySource)
            }
        }

    private fun operationResult(value: OperationResult): JSONObject =
        JSONObject().putValue("success", value.success).putValue("message", value.message)

    private fun options(value: AnsightOptions): JSONObject = JSONObject()
        .putValue("sampleFrequencyMilliseconds", value.sampleFrequencyMilliseconds)
        .putValue("retentionPeriodSeconds", value.retentionPeriodSeconds)
        .putValue("enableFramesPerSecond", value.enableFramesPerSecond)
        .putValue("enableBatteryLevel", value.enableBatteryLevel)
        .putValue("enableOpenFileHandleTracking", value.enableOpenFileHandleTracking)
        .putValue("enableJniReferenceCountTracking", value.enableJniReferenceCountTracking)
        .putValue("toolGuard", toolGuardName(value.toolGuard))
        .putValue("customProperties", value.customProperties.toGroupedJSONObject())
        .putValue("additionalChannels", JSONArray(value.additionalChannels.map(::channel)))

    private fun channel(value: JSONObject): AnsightChannel = AnsightChannel(
        id = value.optInt("id", -1),
        name = value.optString("name"),
        unit = value.stringValue("unit"),
        type = value.stringValue("type") ?: "custom",
        colorHex = value.stringValue("colorHex"),
        source = value.stringValue("source"),
        group = value.stringValue("group"),
        kind = value.stringValue("kind"),
    )

    private fun channel(value: AnsightChannel): JSONObject = JSONObject()
        .putValue("id", value.id)
        .putValue("name", value.name)
        .putValue("unit", value.unit)
        .putValue("type", value.type)
        .putValue("colorHex", value.colorHex)
        .putValue("source", value.source)
        .putValue("group", value.group)
        .putValue("kind", value.kind)

    private fun metric(value: RecordedMetric): JSONObject = JSONObject()
        .putValue("value", value.value)
        .putValue("capturedAtUtc", value.capturedAtUtc)
        .putValue("capturedAtEpochMs", value.capturedAtEpochMs)
        .putValue("channel", value.channel)
        .putValue("sequence", value.sequence)

    private fun event(value: RecordedEvent): JSONObject = JSONObject()
        .putValue("id", value.id)
        .putValue("label", value.label)
        .putValue("type", value.type.wireName)
        .putValue("details", value.details)
        .putValue("capturedAtUtc", value.capturedAtUtc)
        .putValue("capturedAtEpochMs", value.capturedAtEpochMs)
        .putValue("externalId", value.externalId)
        .putValue("channel", value.channel)
        .putValue("sequence", value.sequence)

    private fun sessionJpegCaptureOptions(map: JSONObject): AnsightSessionJpegCaptureOptions =
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
            mode = when (map.stringValue("mode")) {
                "screenshotAndVisualTree" -> AnsightSessionJpegCaptureMode.ScreenshotAndVisualTree
                "screenshotWithVisualTreeOnTouch" -> AnsightSessionJpegCaptureMode.ScreenshotWithVisualTreeOnTouch
                else -> AnsightSessionJpegCaptureMode.ScreenshotOnly
            },
            captureKeyboardPresence = map.booleanValue(
                "captureKeyboardPresence",
                AnsightSessionJpegCaptureOptions.DefaultCaptureKeyboardPresence,
            ),
        )

    private fun resultPayload(map: JSONObject): JSONObject? {
        if (!map.has("result") || map.isNull("result")) return null
        return when (val value = map.opt("result")) {
            is JSONObject -> value
            else -> JSONObject().put("value", value)
        }
    }

    private fun sendEvent(name: String, payload: JSONObject) {
        mainHandler.post {
            dartApi?.onNativeEvent(name, payload.toString()) { }
        }
    }

    private fun bindCurrentActivity() {
        activity?.let(AnsightRuntime::bindActivity)
    }

    private fun requireApplication(): Application =
        application ?: error("Ansight Flutter plugin is not attached to an Android application.")
}

private fun JSONObject.putValue(key: String, value: Any?): JSONObject = apply {
    put(key, value ?: JSONObject.NULL)
}

private fun JSONObject.hasValue(key: String): Boolean = has(key) && !isNull(key)

private fun JSONObject.stringValue(key: String): String? =
    optString(key).takeIf { hasValue(key) && it.isNotBlank() }

private fun JSONObject.booleanValue(key: String, default: Boolean): Boolean =
    if (hasValue(key)) optBoolean(key, default) else default

private fun JSONObject.intValue(key: String, default: Int): Int =
    if (hasValue(key)) optInt(key, default) else default

private fun JSONObject.longValue(key: String, default: Long): Long =
    if (hasValue(key)) optLong(key, default) else default

private fun JSONObject.doubleValue(key: String, default: Double): Double =
    if (hasValue(key)) optDouble(key, default) else default

private fun JSONObject.optionalInt(key: String): Int? =
    if (hasValue(key)) optInt(key) else null

private fun JSONObject.objectValue(key: String): JSONObject =
    objectValueOrNull(key) ?: JSONObject()

private fun JSONObject.objectValueOrNull(key: String): JSONObject? = optJSONObject(key)

private fun JSONObject.arrayValue(key: String): JSONArray? = optJSONArray(key)

private fun JSONObject.stringList(key: String): List<String> {
    val array = optJSONArray(key) ?: return emptyList()
    return (0 until array.length()).mapNotNull {
        array.optString(it).trim().takeIf(String::isNotEmpty)
    }
}

private fun JSONObject.stringSet(key: String): Set<String> = stringList(key).toSet()

private fun JSONObject.toolSuiteEnabled(key: String, default: Boolean): Boolean =
    when (val value = opt(key)) {
        is Boolean -> value
        is JSONObject -> value.optBoolean("enabled", true)
        else -> default
    }

private fun JSONObject.toStringMap(): Map<String, String> =
    keys().asSequence().associateWith { key ->
        opt(key)?.takeUnless { it == JSONObject.NULL }?.toString().orEmpty()
    }

private fun JSONObject.toGroupedStringMap(): Map<String, Map<String, String>> =
    keys().asSequence().mapNotNull { key ->
        objectValueOrNull(key)?.let { key to it.toStringMap() }
    }.toMap()

private fun Map<String, *>.toJSONObject(): JSONObject =
    JSONObject().also { result -> forEach { (key, value) -> result.putValue(key, value) } }

private fun Map<String, Map<String, String>>.toGroupedJSONObject(): JSONObject =
    JSONObject().also { result ->
        forEach { (key, value) -> result.put(key, value.toJSONObject()) }
    }

private fun roots(array: JSONArray?): List<Pair<String, String>> {
    if (array == null) return emptyList()
    return (0 until array.length()).mapNotNull { index ->
        val value = array.optJSONObject(index) ?: return@mapNotNull null
        val alias = value.stringValue("alias") ?: return@mapNotNull null
        val path = value.stringValue("path") ?: return@mapNotNull null
        alias to path
    }
}

private fun <T> List<T>.takeLastLimit(limit: Int): List<T> =
    takeLast(if (limit > 0) limit.coerceAtMost(size) else size)

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
        else -> "custom"
    }

private fun toolPolicy(raw: String?): ToolPolicy =
    when (raw?.trim()?.lowercase()) {
        "write" -> ToolPolicy.Write
        "critical", "delete" -> ToolPolicy.Critical
        else -> ToolPolicy.Read
    }
