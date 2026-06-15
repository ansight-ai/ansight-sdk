package ai.ansight.runtime

import android.app.Activity
import android.app.Application
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.view.Choreographer
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.util.UUID
import java.util.concurrent.Executors
import java.util.concurrent.ScheduledExecutorService
import java.util.concurrent.ScheduledFuture
import java.util.concurrent.TimeUnit

object AnsightRuntime {
    private val lock = Any()
    private val connector = PairingSessionConnector()

    private var application: Application? = null
    private var options: AnsightOptions = AnsightOptions()
    private var initialized = false
    private var active = false
    private var sessionOpen = false
    private var sessionId: String? = null
    private var connectionState = HostConnectionState.Disconnected
    private var sessionMessage: String? = null
    private var liveTransport: PairingLiveSessionTransport? = null
    private var hostId: String? = null
    private var hostName: String? = null
    private var resolvedHostAddress: String? = null
    private var currentLifecycleState = AppLifecycleState.Unknown
    private var currentLifecycleChangedAtUtc: String? = null
    private var currentScreen: RecordedScreenView? = null
    private var profileSequence = 0
    private var nextMetricSequence = 0L
    private var nextEventSequence = 0L
    private var nextTouchSequence = 0L
    private var lastStreamedMetricSequence = 0L
    private var lastStreamedEventSequence = 0L
    private var lastStreamedTouchSequence = 0L
    private val announcedMetricChannelIds = mutableSetOf<Int>()
    private var telemetryStreamLoopActive = false
    private var telemetryExecutor: ScheduledExecutorService? = null
    private var telemetryTask: ScheduledFuture<*>? = null
    private var sessionJpegExecutor: ScheduledExecutorService? = null
    private var sessionJpegTask: ScheduledFuture<*>? = null
    private var autoProbeExecutor: ScheduledExecutorService? = null
    private var autoProbeTask: ScheduledFuture<*>? = null
    private var lifecycleCallbacks: Application.ActivityLifecycleCallbacks? = null
    private var startedActivityCount = 0
    private val frameRateSampler = AndroidFrameRateSampler()

    private val metrics = mutableListOf<RecordedMetric>()
    private val events = mutableListOf<RecordedEvent>()
    private val touches = mutableListOf<RecordedTouch>()
    private val channels = linkedMapOf<Int, AnsightChannel>()
    private val tools = linkedMapOf<String, AnsightToolDescriptor>()
    private val toolRegistry = AndroidToolRegistry()

    fun initialize(application: Application, options: AnsightOptions = AnsightOptions()) {
        val validated = options.validated()
        synchronized(lock) {
            deactivateLocked(closeTransport = true)
            this.application = application
            this.options = validated
            channels.clear()
            channels.putAll(makeChannelDictionary(validated))
            metrics.clear()
            events.clear()
            touches.clear()
            tools.clear()
            toolRegistry.clear()
            AndroidStandardTools.create().forEach(toolRegistry::register)
            initialized = true
            active = false
            sessionOpen = false
            sessionId = null
            connectionState = HostConnectionState.Disconnected
            currentLifecycleState = AppLifecycleState.Unknown
            currentLifecycleChangedAtUtc = null
            currentScreen = null
            profileSequence = 0
            nextMetricSequence = 0
            nextEventSequence = 0
            nextTouchSequence = 0
            lastStreamedMetricSequence = 0
            lastStreamedEventSequence = 0
            lastStreamedTouchSequence = 0
            announcedMetricChannelIds.clear()
            telemetryStreamLoopActive = false
            hostId = null
            hostName = null
            resolvedHostAddress = null
            sessionMessage = "Runtime initialized."
        }
    }

    fun initializeAndActivate(application: Application, options: AnsightOptions = AnsightOptions()) {
        initialize(application, options)
        activate()
    }

    fun activate() {
        val app = synchronized(lock) {
            require(initialized) { "AnsightRuntime must be initialized before activation." }
            if (active) {
                return
            }

            active = true
            sessionMessage = "Runtime activated."
            application ?: error("AnsightRuntime has no application context.")
        }

        startLifecycleCapture(app)
        startTelemetrySampling()
        if (options.enableFramesPerSecond) {
            frameRateSampler.start()
        }
        AndroidUiEvidence.setTouchCaptureEnabled(options.touchCapture != null, ::onTouchCaptured)
        startAutoProbeIfNeeded(app)
    }

    fun deactivate() {
        synchronized(lock) {
            deactivateLocked(closeTransport = true)
            sessionMessage = "Runtime deactivated."
        }
    }

    fun clear() {
        synchronized(lock) {
            metrics.clear()
            events.clear()
            touches.clear()
            currentScreen = null
            currentLifecycleState = AppLifecycleState.Unknown
            currentLifecycleChangedAtUtc = null
            nextMetricSequence = 0
            nextEventSequence = 0
            nextTouchSequence = 0
            lastStreamedMetricSequence = 0
            lastStreamedEventSequence = 0
            lastStreamedTouchSequence = 0
            announcedMetricChannelIds.clear()
            sessionMessage = "Runtime buffers cleared."
        }
    }

    fun registerMetricChannel(channel: AnsightChannel) {
        val validated = channel.validated()
        require(validated.id !in AnsightChannels.reservedIds) { "Channel id ${validated.id} is reserved." }
        synchronized(lock) {
            channels[validated.id] = validated
            sessionMessage = "Registered metric channel ${validated.id}."
        }
    }

    fun metric(value: Long, channel: Int = AnsightChannels.Unspecified) {
        synchronized(lock) {
            require(initialized) { "AnsightRuntime must be initialized before recording metrics." }
            recordMetricLocked(value, validateChannel(channel))
            sessionMessage = "Recorded metric $value."
        }
        streamPendingTelemetry()
    }

    fun event(
        label: String,
        type: AnsightEventType = AnsightEventType.Info,
        details: String? = null,
        channel: Int = AnsightChannels.Unspecified,
        id: String = UUID.randomUUID().toString(),
        externalId: String? = null,
    ) {
        val trimmedLabel = label.trim()
        require(trimmedLabel.isNotBlank()) { "Event label must not be blank." }

        synchronized(lock) {
            require(initialized) { "AnsightRuntime must be initialized before recording events." }
            recordEventLocked(
                RecordedEvent(
                    id = id,
                    label = trimmedLabel,
                    type = type,
                    details = details?.trim(),
                    channel = validateChannel(channel),
                    externalId = externalId,
                    sequence = ++nextEventSequence,
                ),
            )
            sessionMessage = "Recorded event $trimmedLabel."
        }
        streamPendingTelemetry()
    }

    fun screenViewed(name: String, details: Map<String, String> = emptyMap()) {
        val trimmedName = name.trim()
        require(trimmedName.isNotBlank()) { "Screen name must not be blank." }

        synchronized(lock) {
            require(initialized) { "AnsightRuntime must be initialized before recording screen views." }
            val sanitizedDetails = details
                .mapKeys { it.key.trim() }
                .filterKeys { it.isNotBlank() }
                .mapValues { it.value.trim() }
            currentScreen = RecordedScreenView(trimmedName, sanitizedDetails)
            val detailsText = if (sanitizedDetails.isEmpty()) null else JSONObject(sanitizedDetails).toString()
            recordEventLocked(
                RecordedEvent(
                    label = trimmedName,
                    type = AnsightEventType.ScreenViewed,
                    details = detailsText,
                    channel = AnsightChannels.Lifecycle,
                    sequence = ++nextEventSequence,
                ),
            )
            sessionMessage = "Recorded screen view $trimmedName."
        }
        streamPendingTelemetry()
    }

    fun setAppLifecycleState(state: AppLifecycleState, changedAtUtc: String = AnsightClock.isoNow()) {
        val shouldSend = synchronized(lock) {
            if (!initialized || currentLifecycleState == state) {
                return
            }

            currentLifecycleState = state
            currentLifecycleChangedAtUtc = changedAtUtc
            recordEventLocked(
                RecordedEvent(
                    label = "lifecycle.${state.wireName}",
                    type = AnsightEventType.Lifecycle,
                    details = null,
                    channel = AnsightChannels.Lifecycle,
                    capturedAtUtc = changedAtUtc,
                    sequence = ++nextEventSequence,
                ),
            )
            sessionMessage = "Lifecycle state changed to ${state.wireName}."
            sessionOpen && liveTransport?.isOpen == true
        }

        streamPendingTelemetry()
        if (shouldSend) {
            sendCurrentAppState()
        }
    }

    internal fun onTouchCaptured(touch: CapturedTouch) {
        synchronized(lock) {
            if (!initialized || !active || options.touchCapture == null) {
                return
            }

            val recorded = RecordedTouch(
                id = touch.id,
                action = touch.action,
                pointerId = touch.pointerId,
                pointerIndex = touch.pointerIndex,
                pointerCount = touch.pointerCount,
                x = touch.x,
                y = touch.y,
                surfaceWidth = touch.surfaceWidth,
                surfaceHeight = touch.surfaceHeight,
                coordinateUnit = touch.coordinateUnit,
                surfaceScale = touch.surfaceScale,
                normalizedX = touch.normalizedX,
                normalizedY = touch.normalizedY,
                capturedAtUtc = touch.capturedAtUtc,
                capturedAtEpochMs = touch.capturedAtEpochMs,
                sequence = ++nextTouchSequence,
            )
            touches += recorded
            trimTouchesLocked()
        }
        streamPendingTelemetry()
    }

    fun openSession(pairingJson: String, options: PairingOpenOptions): OpenSessionResult {
        val result = connect(
            HostConnectionRequest(
                kind = HostConnectionRequestKind.Payload,
                payload = pairingJson,
                clientName = options.clientName,
                expectedAppId = options.expectedAppId,
                hostAddressOverride = options.hostAddressOverride,
            ),
        )
        return result.openSession ?: OpenSessionResult(
            success = result.success,
            accepted = result.success,
            message = result.message,
            reasonCode = result.reasonCode,
        )
    }

    fun connect(request: HostConnectionRequest = HostConnectionRequest()): HostConnectionResult {
        synchronized(lock) {
            if (!initialized) {
                return HostConnectionResult.failure(
                    "AnsightRuntime must be initialized before connecting to a host.",
                    kind = HostConnectionActionKind.Connect,
                    source = sourceFor(request.kind),
                )
            }
            if (!active) {
                return HostConnectionResult.failure(
                    "AnsightRuntime must be active before connecting to a host.",
                    kind = HostConnectionActionKind.Connect,
                    source = sourceFor(request.kind),
                )
            }
        }

        return runConnectionOffCallingThread {
            connectInternal(request)
        }
    }

    fun disconnect(): HostConnectionResult {
        val transport = synchronized(lock) {
            connectionState = HostConnectionState.Disconnecting
            stopSessionJpegCaptureLocked()
            liveTransport.also {
                liveTransport = null
                sessionOpen = false
                sessionId = null
                connectionState = HostConnectionState.Disconnected
                sessionMessage = "Disconnected from Ansight host."
            }
        }
        transport?.close(notify = false)
        return HostConnectionResult.success(
            "Disconnected from Ansight host.",
            kind = HostConnectionActionKind.Disconnect,
            source = HostConnectionSource.HostConnection,
        )
    }

    fun completeSession() {
        liveTransport?.sendControlRequest(
            PairingControlActions.SessionComplete,
            JSONObject().put("reason", "client log stream complete"),
            timeoutMilliseconds = 10_000,
        )
        closeSession()
    }

    fun closeSession() {
        disconnect()
    }

    fun clearSavedPairingConfig(): HostConnectionResult {
        val app = synchronized(lock) { application }
            ?: return HostConnectionResult.failure("AnsightRuntime is not initialized.", HostConnectionActionKind.ClearSavedConfig)
        app.getSharedPreferences(options.hostConnection.savedConfigKey, Application.MODE_PRIVATE)
            .edit()
            .clear()
            .apply()
        synchronized(lock) {
            stopAutoProbeLocked()
            sessionMessage = "Saved pairing config cleared."
        }
        return HostConnectionResult.success(
            "Saved pairing config cleared.",
            kind = HostConnectionActionKind.ClearSavedConfig,
            source = HostConnectionSource.SavedConfig,
        )
    }

    fun savePairingConfig(pairingJson: String): HostConnectionResult {
        val app = synchronized(lock) { application }
            ?: return HostConnectionResult.failure("AnsightRuntime is not initialized.", HostConnectionActionKind.Connect)

        return try {
            val expectedAppId = app.packageName
            PairingConfigDocumentService.parseAndValidateDocument(pairingJson, expectedAppId)
            savePairingConfigLocked(app, pairingJson)
            startAutoProbeIfNeeded(app)
            HostConnectionResult.success("Saved pairing config.", source = HostConnectionSource.SavedConfig)
        } catch (ex: Exception) {
            HostConnectionResult.failure(
                ex.message ?: "Pairing config could not be saved.",
                source = HostConnectionSource.SavedConfig,
                reasonCode = reasonCodeFor(ex),
            )
        }
    }

    fun registerTool(tool: AnsightToolDescriptor) {
        require(tool.id.isNotBlank()) { "Tool id must not be blank." }

        synchronized(lock) {
            tools[tool.id] = tool
            toolRegistry.register(
                FunctionAndroidTool(
                    ToolDefinition(
                        id = tool.id,
                        name = tool.name,
                        description = "Custom app tool '${tool.name}'.",
                        category = "custom",
                        scope = tool.scope.toToolScope(),
                        keywords = "custom app tool",
                    ),
                ) { _, _ ->
                    AndroidToolResult.failure("Tool '${tool.id}' was registered as metadata only and has no Android handler.", "tool_handler_missing")
                },
            )
            sessionMessage = "Registered tool ${tool.id}."
        }
    }

    fun registerTool(tool: AndroidTool) {
        synchronized(lock) {
            toolRegistry.register(tool)
            tools[tool.definition.id] = AnsightToolDescriptor(
                id = tool.definition.id,
                name = tool.definition.name,
                scope = tool.definition.scope.name,
            )
            sessionMessage = "Registered tool ${tool.definition.id}."
        }
    }

    fun sendClientLog(logLine: String): OperationResult {
        val trimmed = logLine.trim()
        if (trimmed.isEmpty()) {
            return OperationResult.failure("Enter log text before sending.")
        }
        val transport = synchronized(lock) { liveTransport }
            ?: return OperationResult.failure("WebSocket session is not open.")
        return transport.sendControlRequest(
            PairingControlActions.ClientLog,
            JSONObject().put("data", trimmed),
        )
    }

    fun snapshot(): AnsightDebugSnapshot {
        synchronized(lock) {
            return AnsightDebugSnapshot(
                initialized = initialized,
                active = active,
                sessionOpen = sessionOpen,
                connectionStatus = hostConnectionStatusLocked(),
                lifecycleState = currentLifecycleState,
                lifecycleChangedAtUtc = currentLifecycleChangedAtUtc,
                currentScreen = currentScreen,
                metricsRecorded = metrics.size,
                eventsRecorded = events.size,
                touchesRecorded = touches.size,
                channels = channels.values.toList(),
                registeredTools = toolRegistry.size,
                lastMetric = metrics.lastOrNull(),
                lastEvent = events.lastOrNull(),
                deviceProfile = application?.let { nextDeviceProfileLocked(it, increment = false) },
                sessionMessage = sessionMessage,
            )
        }
    }

    fun options(): AnsightOptions {
        synchronized(lock) {
            return options
        }
    }

    fun hostConnectionStatus(): HostConnectionStatus {
        synchronized(lock) {
            return hostConnectionStatusLocked()
        }
    }

    private fun connectInternal(request: HostConnectionRequest): HostConnectionResult {
        val app = synchronized(lock) { application ?: error("AnsightRuntime has no application context.") }
        val candidates = resolveConnectionCandidates(app, request)
        if (candidates.isEmpty()) {
            return HostConnectionResult.failure(
                "No pairing config is available.",
                kind = HostConnectionActionKind.Connect,
                source = sourceFor(request.kind),
                reasonCode = PairingFailureCodes.PairingRequired,
            )
        }

        var lastResult: HostConnectionResult? = null
        for (candidate in candidates) {
            val result = connectCandidate(app, request, candidate)
            if (result.success) {
                return result
            }
            lastResult = result
            if (request.kind != HostConnectionRequestKind.Auto) {
                return result
            }
        }

        return lastResult ?: HostConnectionResult.failure(
            "No pairing config is available.",
            kind = HostConnectionActionKind.Connect,
            source = sourceFor(request.kind),
            reasonCode = PairingFailureCodes.PairingRequired,
        )
    }

    private fun connectCandidate(
        app: Application,
        originalRequest: HostConnectionRequest,
        candidate: ResolvedConnectionCandidate,
    ): HostConnectionResult {
        val clientName = originalRequest.clientName?.trim()?.ifBlank { null }
            ?: options.hostAutoProbe.clientName?.trim()?.ifBlank { null }
            ?: DeviceAppProfileCollector.collect(app, profileSeq = 0).app.appName
            ?: app.packageName
        val expectedAppId = originalRequest.expectedAppId?.trim()?.ifBlank { null } ?: app.packageName

        val document = try {
            PairingConfigDocumentService.parseAndValidateDocument(candidate.payload, expectedAppId)
        } catch (ex: Exception) {
            synchronized(lock) {
                connectionState = HostConnectionState.Failed
                sessionMessage = ex.message
            }
            return HostConnectionResult.failure(
                ex.message ?: "Pairing config is invalid.",
                kind = HostConnectionActionKind.Connect,
                source = candidate.source,
                reasonCode = reasonCodeFor(ex),
            )
        }

        synchronized(lock) {
            connectionState = HostConnectionState.Connecting
            sessionOpen = false
            sessionId = null
            sessionMessage = "Connecting to Ansight host."
        }

        val attempt = connector.connect(
            document = document,
            clientName = clientName,
            options = PairingConnectionOptions(
                hostAddressOverride = originalRequest.hostAddressOverride,
                discoveryPort = options.hostConnection.discoveryPort,
            ),
        )

        if (!attempt.success || attempt.transport == null || attempt.connectResponse == null) {
            synchronized(lock) {
                connectionState = HostConnectionState.Disconnected
                sessionOpen = false
                sessionId = null
                sessionMessage = attempt.message
            }
            val open = OpenSessionResult(
                success = false,
                accepted = attempt.accepted,
                message = attempt.message,
                configId = document.config.configId,
                appId = document.config.appId,
                resolvedHostAddress = attempt.hostAddress,
                usedEmbeddedDeveloperPairing = candidate.usedEmbeddedDeveloperPairing,
                discoverySource = document.discoveryHint?.source,
                reasonCode = attempt.failureCode ?: attempt.connectResponse?.reason,
                hostId = attempt.connectResponse?.hostId,
                hostName = attempt.connectResponse?.hostName,
            )
            return HostConnectionResult.failure(
                attempt.message,
                kind = HostConnectionActionKind.Connect,
                source = candidate.source,
                reasonCode = open.reasonCode,
                openSession = open,
            )
        }

        val transport = attempt.transport
        transport.textMessageHandler = { text -> handleTransportText(text) }
        val openPayload = JSONObject()
            .put("clientName", clientName)
            .put("configId", document.config.configId)
            .put("appId", document.config.appId)
            .put("openedAtUtc", AnsightClock.isoNow())
        if (options.customProperties.isNotEmpty()) {
            openPayload.put("customProperties", options.customProperties.toJSONObject())
        }

        val sessionOpenResult = transport.sendControlRequest(PairingControlActions.SessionOpen, openPayload)
        if (!sessionOpenResult.success) {
            transport.close(notify = false)
            synchronized(lock) {
                connectionState = HostConnectionState.Disconnected
                sessionMessage = sessionOpenResult.message
            }
            return HostConnectionResult.failure(
                sessionOpenResult.message,
                kind = HostConnectionActionKind.Connect,
                source = HostConnectionSource.Transport,
                reasonCode = PairingFailureCodes.WebSocketHandshakeFailed,
            )
        }

        val profile = synchronized(lock) { nextDeviceProfileLocked(app, increment = true) }
        val profileResult = transport.sendControlRequest(PairingControlActions.DeviceProfile, profile.toJson())
        if (!profileResult.success) {
            transport.close(notify = false)
            synchronized(lock) {
                connectionState = HostConnectionState.Disconnected
                sessionMessage = profileResult.message
            }
            return HostConnectionResult.failure(
                profileResult.message,
                kind = HostConnectionActionKind.Connect,
                source = HostConnectionSource.HostConnection,
                reasonCode = PairingFailureCodes.WebSocketHandshakeFailed,
            )
        }

        synchronized(lock) {
            liveTransport = transport
            connectionState = HostConnectionState.Connected
            sessionOpen = true
            sessionId = ProcessSessionIdentity.current
            hostId = attempt.connectResponse.hostId
            hostName = attempt.connectResponse.hostName
            resolvedHostAddress = attempt.hostAddress
            lastStreamedMetricSequence = 0
            lastStreamedEventSequence = 0
            lastStreamedTouchSequence = 0
            announcedMetricChannelIds.clear()
            telemetryStreamLoopActive = false
            sessionMessage = "Connected to Ansight host."
        }

        sendCurrentAppState()
        sendSessionProperties()
        sendMetricChannelDefinitions()
        startSessionJpegCaptureIfNeeded()
        streamPendingTelemetry()

        if (candidate.shouldSaveOnSuccess) {
            savePairingConfigLocked(app, candidate.payload)
        }

        val open = OpenSessionResult(
            success = true,
            accepted = true,
            message = "Connected to Ansight host.",
            sessionId = ProcessSessionIdentity.current,
            configId = document.config.configId,
            appId = document.config.appId,
            resolvedHostAddress = attempt.hostAddress,
            usedEmbeddedDeveloperPairing = candidate.usedEmbeddedDeveloperPairing,
            discoverySource = document.discoveryHint?.source,
            reasonCode = attempt.connectResponse.reason,
            hostId = attempt.connectResponse.hostId,
            hostName = attempt.connectResponse.hostName,
        )
        return HostConnectionResult.success(
            "Connected to Ansight host.",
            kind = HostConnectionActionKind.Connect,
            source = candidate.source,
            openSession = open,
        )
    }

    private fun resolveConnectionCandidates(app: Application, request: HostConnectionRequest): List<ResolvedConnectionCandidate> {
        val saved = { loadSavedPairingConfig(app) }
        return when (request.kind) {
            HostConnectionRequestKind.Payload,
            HostConnectionRequestKind.QrCode,
            HostConnectionRequestKind.Config -> listOfNotNull(
                request.payload?.trim()?.ifBlank { null }?.let {
                    ResolvedConnectionCandidate(it, sourceFor(request.kind), shouldSaveOnSuccess = true)
                },
            )
            HostConnectionRequestKind.File -> listOfNotNull(
                request.payload?.trim()?.ifBlank { null }?.let { path ->
                    runCatching { File(path).readText() }.getOrNull()
                }?.let { ResolvedConnectionCandidate(it, HostConnectionSource.ConfigReader, shouldSaveOnSuccess = true) },
            )
            HostConnectionRequestKind.SavedConfig -> listOfNotNull(
                saved()?.let { ResolvedConnectionCandidate(it, HostConnectionSource.SavedConfig) },
            )
            HostConnectionRequestKind.BundledConfig -> listOfNotNull(
                options.hostConnection.bundledDeveloperConfigJson?.let {
                    ResolvedConnectionCandidate(it, HostConnectionSource.BundledDeveloperConfig, usedEmbeddedDeveloperPairing = true)
                },
                options.hostConnection.bundledConfigJson?.let { ResolvedConnectionCandidate(it, HostConnectionSource.BundledConfig) },
            )
            HostConnectionRequestKind.Auto -> listOfNotNull(
                saved()?.let { ResolvedConnectionCandidate(it, HostConnectionSource.SavedConfig) },
                options.hostConnection.bundledDeveloperConfigJson?.let {
                    ResolvedConnectionCandidate(it, HostConnectionSource.BundledDeveloperConfig, usedEmbeddedDeveloperPairing = true)
                },
                options.hostConnection.bundledConfigJson?.let { ResolvedConnectionCandidate(it, HostConnectionSource.BundledConfig) },
            )
        }
    }

    private fun sendCurrentAppState(): OperationResult {
        val state = synchronized(lock) {
            JSONObject()
                .put("state", currentLifecycleState.wireName)
                .put("changedAtUtc", currentLifecycleChangedAtUtc ?: AnsightClock.isoNow())
        }
        val transport = synchronized(lock) { liveTransport } ?: return OperationResult.failure("WebSocket session is not open.")
        return transport.sendControlRequest(PairingControlActions.AppState, state)
    }

    private fun sendSessionProperties(): OperationResult {
        val payload = synchronized(lock) {
            JSONObject()
                .put("schema", "ansight.session-properties.v1")
                .put("platform", "android")
                .put("sdk", "ansight-runtime-android")
                .put("sessionId", ProcessSessionIdentity.current)
                .put("toolGuard", options.toolGuard.toProtocolJson())
                .put("capabilities", JSONArray(listOf(
                    "runtime",
                    "pairing",
                    "hostConnection",
                    "liveTransport",
                    "deviceProfile",
                    "telemetry",
                    "events",
                    "touches",
                    "binaryTransfer",
                    ToolProtocol.Capability,
                    "ui.get_visual_tree",
                    "ui.get_screenshot",
                    "files",
                    "prefs",
                    "secure",
                    "data",
                )))
                .put("tools", JSONObject()
                    .put("count", toolRegistry.visible(options.toolGuard).size)
                    .put("ids", JSONArray(toolRegistry.visible(options.toolGuard).map { it.definition.id })))
        }
        val transport = synchronized(lock) { liveTransport } ?: return OperationResult.failure("WebSocket session is not open.")
        return transport.sendControlRequest(PairingControlActions.SessionProperties, payload)
    }

    private fun handleTransportText(text: String) {
        val json = try {
            JSONObject(text)
        } catch (_: Exception) {
            return
        }
        when (json.optionalString("type")) {
            ToolProtocol.QueryType -> sendToolCatalog(json)
            ToolProtocol.CallType -> executeToolCall(json)
        }
    }

    private fun sendToolCatalog(request: JSONObject) {
        val response = synchronized(lock) {
            if (!options.toolGuard.canDiscover(ToolScope.Read)) {
                return@synchronized toolErrorEnvelope(request, "tool_discovery_disabled", "Tool discovery is disabled by the current guard policy.")
            }
            val visibleTools = toolRegistry.visible(options.toolGuard).map { it.definition.toJson() }
            toolEnvelope(
                type = ToolProtocol.CatalogType,
                request = request,
                payload = JSONObject()
                    .put("guard", options.toolGuard.toProtocolJson())
                    .put("tools", JSONArray(visibleTools))
                    .put("count", visibleTools.size),
            )
        }
        synchronized(lock) { liveTransport }?.sendText(response.toString())
    }

    private fun executeToolCall(request: JSONObject) {
        Thread {
            val response = try {
                executeToolCallCore(request)
            } catch (ex: Exception) {
                toolErrorEnvelope(request, "tool_execution_exception", ex.message ?: "Tool execution failed.")
            }
            synchronized(lock) { liveTransport }?.sendText(response.toString())
        }.apply {
            name = "AnsightAndroidToolCall"
            isDaemon = true
            start()
        }
    }

    private fun executeToolCallCore(request: JSONObject): JSONObject {
        val payload = request.optJSONObject("payload")
            ?: return toolErrorEnvelope(request, "tool_call_payload_invalid", "Tool call payload must be a JSON object.")
        val toolId = payload.optionalString("toolId")
            ?: return toolErrorEnvelope(request, "tool_call_missing_id", "Tool call payload must include 'toolId'.")
        val app = synchronized(lock) { application }
            ?: return toolErrorEnvelope(request, "runtime_unavailable", "AnsightRuntime is not initialized.")
        val tool = synchronized(lock) { toolRegistry.get(toolId) }
            ?: return toolErrorEnvelope(request, "tool_not_found", "Tool '$toolId' is not registered.")
        val guard = synchronized(lock) { options.toolGuard }
        if (!guard.canExecute(tool.definition.scope)) {
            return toolErrorEnvelope(request, "tool_execution_denied", "Tool scope '${tool.definition.scope}' is not enabled by the current guard policy.")
        }

        val args = mutableMapOf<String, String>()
        payload.optJSONObject("arguments")?.let { argumentObject ->
            argumentObject.keys().forEach { key ->
                val value = argumentObject.opt(key)
                if (value != null && value != JSONObject.NULL) {
                    args[key] = value.toString()
                }
            }
        }
        val context = synchronized(lock) {
            AndroidToolExecutionContext(
                application = app,
                transport = liveTransport,
                sessionId = sessionId,
                requestId = request.optionalString("id"),
                options = options,
            )
        }
        val result = tool.execute(args, context)
        if (!result.success) {
            return toolErrorEnvelope(
                request = request,
                code = result.errorCode ?: "tool_execution_failed",
                message = result.message ?: "Tool '$toolId' failed.",
                details = result.payload,
            )
        }
        return toolEnvelope(
            type = ToolProtocol.ResultType,
            request = request,
            payload = JSONObject()
                .put("toolId", toolId)
                .put("success", true)
                .putNullable("message", result.message)
                .putNullable("result", result.payload),
        )
    }

    private fun toolEnvelope(type: String, request: JSONObject, payload: JSONObject): JSONObject = JSONObject()
        .put("type", type)
        .put("id", "android.${UUID.randomUUID().toString().replace("-", "")}")
        .putNullable("replyTo", request.optionalString("id"))
        .putNullable("sessionId", request.optionalString("sessionId") ?: sessionId)
        .put("sentAt", AnsightClock.isoNow())
        .put("capability", ToolProtocol.Capability)
        .put("payload", payload)

    private fun toolErrorEnvelope(request: JSONObject, code: String, message: String, details: JSONObject? = null): JSONObject = toolEnvelope(
        type = ToolProtocol.ErrorType,
        request = request,
        payload = JSONObject()
            .put("success", false)
            .put("errorCode", code)
            .put("message", message)
            .put("retryable", false)
            .putNullable("details", details),
    )

    private fun sendMetricChannelDefinitions(channelIds: Set<Int>? = null): OperationResult {
        val state = synchronized(lock) {
            val selected = channels.values
                .filter { channelIds?.contains(it.id) ?: true }
                .filter { it.id !in announcedMetricChannelIds }
                .sortedBy { it.id }
            if (selected.isEmpty()) {
                return OperationResult.success("Metric channels already announced.")
            }

            val payload = JSONObject()
                .put("source", "client")
                .put("type", "CLIENT_METRIC_CHANNELS")
                .put("sentAtUtc", AnsightClock.isoNow())
                .put(
                    "channels",
                    JSONArray(selected.map { channel ->
                        JSONObject()
                            .put("id", channel.id)
                            .put("name", channel.name)
                            .putNullable("unit", channel.unit)
                            .put("type", channel.type)
                            .putNullable("color", channel.colorHex)
                    }),
                )
            selected.map { it.id }.toSet() to payload
        }

        val transport = synchronized(lock) { liveTransport } ?: return OperationResult.failure("WebSocket session is not open.")
        val result = transport.sendText(state.second.toString())
        if (result.success) {
            synchronized(lock) {
                announcedMetricChannelIds.addAll(state.first)
            }
        }
        return result
    }

    private fun streamPendingTelemetry() {
        val shouldStart = synchronized(lock) {
            if (liveTransport?.isOpen != true || !initialized || !active || !sessionOpen || telemetryStreamLoopActive) {
                false
            } else {
                telemetryStreamLoopActive = true
                true
            }
        }
        if (!shouldStart) {
            return
        }

        Thread {
            try {
                runTelemetryStreamLoop()
            } finally {
                val shouldRestart = synchronized(lock) {
                    telemetryStreamLoopActive = false
                    liveTransport?.isOpen == true &&
                        initialized &&
                        active &&
                        sessionOpen &&
                        ((metrics.lastOrNull()?.sequence ?: 0) > lastStreamedMetricSequence ||
                            (events.lastOrNull()?.sequence ?: 0) > lastStreamedEventSequence ||
                            (touches.lastOrNull()?.sequence ?: 0) > lastStreamedTouchSequence)
                }
                if (shouldRestart) {
                    streamPendingTelemetry()
                }
            }
        }.start()
    }

    private fun runTelemetryStreamLoop() {
        while (true) {
            val batch = synchronized(lock) {
                if (liveTransport?.isOpen != true || !initialized || !active || !sessionOpen) {
                    return
                }
                val newMetrics = metrics.filter { it.sequence > lastStreamedMetricSequence }.take(160)
                val newEvents = events.filter { it.sequence > lastStreamedEventSequence }.take(160)
                val newTouches = touches.filter { it.sequence > lastStreamedTouchSequence }.take(200)
                TelemetryBatch(newMetrics, newEvents, newTouches)
            }

            if (batch.metrics.isEmpty() && batch.events.isEmpty() && batch.touches.isEmpty()) {
                return
            }

            if (batch.metrics.isNotEmpty()) {
                val channelResult = sendMetricChannelDefinitions(batch.metrics.map { it.channel }.toSet())
                if (!channelResult.success) {
                    synchronized(lock) { sessionMessage = channelResult.message }
                    return
                }
                val result = synchronized(lock) { liveTransport }?.sendText(makeMetricsPayload(batch.metrics).toString())
                    ?: OperationResult.failure("WebSocket session is not open.")
                if (!result.success) {
                    synchronized(lock) { sessionMessage = result.message }
                    return
                }
                synchronized(lock) {
                    lastStreamedMetricSequence = maxOf(lastStreamedMetricSequence, batch.metrics.last().sequence)
                }
            }

            if (batch.events.isNotEmpty()) {
                val result = synchronized(lock) { liveTransport }?.sendText(makeEventsPayload(batch.events).toString())
                    ?: OperationResult.failure("WebSocket session is not open.")
                if (!result.success) {
                    synchronized(lock) { sessionMessage = result.message }
                    return
                }
                synchronized(lock) {
                    lastStreamedEventSequence = maxOf(lastStreamedEventSequence, batch.events.last().sequence)
                }
            }

            if (batch.touches.isNotEmpty()) {
                val result = synchronized(lock) { liveTransport }?.sendText(makeTouchPayload(batch.touches).toString())
                    ?: OperationResult.failure("WebSocket session is not open.")
                if (!result.success) {
                    synchronized(lock) { sessionMessage = result.message }
                    return
                }
                synchronized(lock) {
                    lastStreamedTouchSequence = maxOf(lastStreamedTouchSequence, batch.touches.last().sequence)
                }
            }
        }
    }

    private fun makeMetricsPayload(batch: List<RecordedMetric>): JSONObject = JSONObject()
        .put("source", "client")
        .put("type", "CLIENT_METRICS")
        .put("sentAtUtc", AnsightClock.isoNow())
        .put(
            "metrics",
            JSONArray(batch.map { metric ->
                JSONObject()
                    .put("channel", metric.channel)
                    .put("value", metric.value)
                    .put("capturedAtUtc", metric.capturedAtUtc)
            }),
        )

    private fun makeTouchPayload(batch: List<RecordedTouch>): JSONObject {
        val ordered = batch.sortedBy { it.capturedAtEpochMs }
        val first = ordered.first()
        return JSONObject()
            .put("type", "CLIENT_TOUCH_INPUT")
            .put("schema", "ansight.touches.v1")
            .put("t0", first.capturedAtUtc)
            .put("space", "w")
            .put("unit", if (first.coordinateUnit.equals("pt", ignoreCase = true)) "pt" else "px")
            .put("surface", JSONArray(listOf(first.surfaceWidth, first.surfaceHeight, first.surfaceScale)))
            .put(
                "rows",
                JSONArray(ordered.map { touch ->
                    val delta = (touch.capturedAtEpochMs - first.capturedAtEpochMs).coerceAtLeast(0)
                    JSONArray(
                        listOf(
                            delta,
                            when (touch.action) {
                                "Down" -> 0
                                "Move" -> 1
                                "Up" -> 2
                                "Cancel" -> 3
                                else -> 4
                            },
                            touch.pointerId,
                            touch.x,
                            touch.y,
                            touch.pointerIndex,
                            touch.pointerCount,
                        ),
                    )
                }),
            )
    }

    private fun makeEventsPayload(batch: List<RecordedEvent>): JSONObject = JSONObject()
        .put("source", "client")
        .put("type", "CLIENT_EVENTS")
        .put("sentAtUtc", AnsightClock.isoNow())
        .put(
            "events",
            JSONArray(batch.map { event ->
                JSONObject()
                    .put("id", event.id)
                    .put("label", event.label)
                    .put("eventType", event.type.wireName)
                    .putNullable("details", event.details)
                    .put("capturedAtUtc", event.capturedAtUtc)
                    .put("channel", event.channel)
            }),
        )

    private fun startTelemetrySampling() {
        synchronized(lock) {
            telemetryTask?.cancel(false)
            telemetryExecutor?.shutdownNow()
            telemetryExecutor = Executors.newSingleThreadScheduledExecutor { runnable ->
                Thread(runnable, "AnsightAndroidTelemetry").apply { isDaemon = true }
            }
            val executor = telemetryExecutor ?: return
            telemetryTask = executor.scheduleAtFixedRate(
                {
                    sampleBuiltInTelemetry()
                },
                0,
                options.sampleFrequencyMilliseconds.toLong(),
                TimeUnit.MILLISECONDS,
            )
        }
    }

    private fun sampleBuiltInTelemetry() {
        synchronized(lock) {
            if (!initialized || !active) {
                return
            }

            if (options.defaultMemoryChannels.javaHeap) {
                recordMetricLocked(AndroidMetricSampler.javaHeapBytes(), AnsightChannels.JavaHeap)
            }
            if (options.defaultMemoryChannels.nativeHeap) {
                recordMetricLocked(AndroidMetricSampler.nativeHeapBytes(), AnsightChannels.NativeHeap)
            }
            if (options.defaultMemoryChannels.rss) {
                val rss = AndroidMetricSampler.rssBytes()
                if (rss > 0) {
                    recordMetricLocked(rss, AnsightChannels.Rss)
                }
            }
            if (options.enableFramesPerSecond) {
                val fps = frameRateSampler.consumeFramesPerSecond()
                if (fps > 0) {
                    recordMetricLocked(fps.toLong(), AnsightChannels.FramesPerSecond)
                }
            }
            if (options.enableBatteryLevel) {
                application?.let { app ->
                    DeviceAppProfileCollector.collect(app, profileSeq = profileSequence).device.battery?.levelPct?.let { level ->
                        recordMetricLocked(level.toLong(), AnsightChannels.BatteryLevel)
                    }
                }
            }
        }
        streamPendingTelemetry()
    }

    private fun recordMetricLocked(value: Long, channel: Int) {
        ensureChannelLocked(channel)
        metrics += RecordedMetric(
            value = value,
            channel = channel,
            sequence = ++nextMetricSequence,
        )
        trimMetricsLocked()
    }

    private fun recordEventLocked(event: RecordedEvent) {
        ensureChannelLocked(event.channel)
        events += event
        trimEventsLocked()
    }

    private fun trimMetricsLocked() {
        val max = options.maximumBufferSize.coerceAtLeast(1)
        if (metrics.size > max) {
            metrics.subList(0, metrics.size - max).clear()
        }
    }

    private fun trimEventsLocked() {
        val max = options.maximumBufferSize.coerceAtLeast(1)
        if (events.size > max) {
            events.subList(0, events.size - max).clear()
        }
    }

    private fun trimTouchesLocked() {
        val max = (options.maximumBufferSize * 2).coerceAtLeast(1)
        if (touches.size > max) {
            touches.subList(0, touches.size - max).clear()
        }
    }

    private fun startLifecycleCapture(app: Application) {
        synchronized(lock) {
            if (lifecycleCallbacks != null) {
                return
            }
            startedActivityCount = 0
            lifecycleCallbacks = object : Application.ActivityLifecycleCallbacks {
                override fun onActivityCreated(activity: Activity, savedInstanceState: Bundle?) = Unit

                override fun onActivityStarted(activity: Activity) {
                    val becameForeground = synchronized(lock) {
                        startedActivityCount += 1
                        startedActivityCount == 1
                    }
                    if (becameForeground) {
                        setAppLifecycleState(AppLifecycleState.Foreground)
                    }
                }

                override fun onActivityResumed(activity: Activity) {
                    AndroidUiEvidence.onActivityResumed(activity)
                    runCatching { screenViewed(activity.javaClass.simpleName) }
                }

                override fun onActivityPaused(activity: Activity) = Unit

                override fun onActivityStopped(activity: Activity) {
                    val becameBackground = synchronized(lock) {
                        startedActivityCount = (startedActivityCount - 1).coerceAtLeast(0)
                        startedActivityCount == 0
                    }
                    if (becameBackground) {
                        setAppLifecycleState(AppLifecycleState.Background)
                    }
                }

                override fun onActivitySaveInstanceState(activity: Activity, outState: Bundle) = Unit

                override fun onActivityDestroyed(activity: Activity) {
                    AndroidUiEvidence.onActivityDestroyed(activity)
                }
            }
            app.registerActivityLifecycleCallbacks(lifecycleCallbacks)
        }
    }

    private fun stopLifecycleCaptureLocked() {
        val app = application
        val callbacks = lifecycleCallbacks
        if (app != null && callbacks != null) {
            app.unregisterActivityLifecycleCallbacks(callbacks)
        }
        lifecycleCallbacks = null
        startedActivityCount = 0
    }

    private fun deactivateLocked(closeTransport: Boolean) {
        active = false
        sessionOpen = false
        sessionId = null
        connectionState = HostConnectionState.Disconnected
        stopLifecycleCaptureLocked()
        telemetryTask?.cancel(false)
        telemetryTask = null
        telemetryExecutor?.shutdownNow()
        telemetryExecutor = null
        stopSessionJpegCaptureLocked()
        stopAutoProbeLocked()
        frameRateSampler.stop()
        AndroidUiEvidence.setTouchCaptureEnabled(false, null)
        if (closeTransport) {
            liveTransport?.close(notify = false)
            liveTransport = null
        }
    }

    private fun hostConnectionStatusLocked(): HostConnectionStatus {
        val app = application
        val connected = sessionOpen && liveTransport?.isOpen == true && connectionState == HostConnectionState.Connected
        val hasSaved = app?.let { loadSavedPairingConfig(it) != null } ?: false
        val hasBundled = options.hostConnection.bundledConfigJson != null || options.hostConnection.bundledDeveloperConfigJson != null
        return HostConnectionStatus(
            isRuntimeActive = active,
            isConnected = connected,
            connectionState = connectionState,
            hasCachedSession = false,
            hasSavedConfig = hasSaved,
            hasBundledConfig = hasBundled,
            summaryKind = when {
                connected -> HostConnectionSummaryKind.Connected
                connectionState == HostConnectionState.Failed -> HostConnectionSummaryKind.Failed
                !initialized -> HostConnectionSummaryKind.Unavailable
                active -> HostConnectionSummaryKind.Ready
                else -> HostConnectionSummaryKind.Disconnected
            },
            summaryMessage = sessionMessage ?: if (connected) "Connected to Ansight host." else "Not connected.",
        )
    }

    private fun startAutoProbeIfNeeded(app: Application) {
        synchronized(lock) {
            if (!initialized || !active || !options.hostAutoProbe.enabled || autoProbeTask != null) {
                return
            }
            if (loadSavedPairingConfig(app) == null &&
                options.hostConnection.bundledDeveloperConfigJson == null &&
                options.hostConnection.bundledConfigJson == null
            ) {
                return
            }

            autoProbeExecutor = Executors.newSingleThreadScheduledExecutor { runnable ->
                Thread(runnable, "AnsightAndroidHostAutoProbe").apply { isDaemon = true }
            }
            val executor = autoProbeExecutor ?: return
            autoProbeTask = executor.schedule(
                { runAutoProbeLoop() },
                options.hostAutoProbe.initialDelayMilliseconds,
                TimeUnit.MILLISECONDS,
            )
        }
    }

    private fun runAutoProbeLoop() {
        while (!Thread.currentThread().isInterrupted) {
            val delay = synchronized(lock) {
                if (!initialized || !active || !options.hostAutoProbe.enabled) {
                    return
                }

                if (sessionOpen && liveTransport?.isOpen == true && connectionState == HostConnectionState.Connected) {
                    options.hostAutoProbe.reconnectDelayMilliseconds
                } else {
                    null
                }
            }

            if (delay != null) {
                sleepAutoProbe(delay)
                continue
            }

            val result = try {
                connectInternal(
                    HostConnectionRequest(
                        kind = HostConnectionRequestKind.Auto,
                        clientName = options.hostAutoProbe.clientName,
                    ),
                )
            } catch (ex: Exception) {
                synchronized(lock) {
                    sessionMessage = ex.message ?: "Host auto-probe failed."
                    connectionState = HostConnectionState.Disconnected
                }
                HostConnectionResult.failure(ex.message ?: "Host auto-probe failed.")
            }

            sleepAutoProbe(
                if (result.success) {
                    options.hostAutoProbe.reconnectDelayMilliseconds
                } else {
                    options.hostAutoProbe.probeIntervalMilliseconds
                },
            )
        }
    }

    private fun sleepAutoProbe(milliseconds: Long) {
        try {
            Thread.sleep(milliseconds.coerceAtLeast(1_000))
        } catch (_: InterruptedException) {
            Thread.currentThread().interrupt()
        }
    }

    private fun stopAutoProbeLocked() {
        autoProbeTask?.cancel(true)
        autoProbeTask = null
        autoProbeExecutor?.shutdownNow()
        autoProbeExecutor = null
    }

    private fun startSessionJpegCaptureIfNeeded() {
        synchronized(lock) {
            val captureOptions = options.sessionJpegCapture ?: return
            if (!initialized || !active || liveTransport?.isOpen != true || sessionJpegTask != null) {
                return
            }
            sessionJpegExecutor = Executors.newSingleThreadScheduledExecutor { runnable ->
                Thread(runnable, "AnsightAndroidSessionJpeg").apply { isDaemon = true }
            }
            val executor = sessionJpegExecutor ?: return
            sessionJpegTask = executor.scheduleAtFixedRate(
                { captureAndSendSessionJpeg() },
                0,
                captureOptions.intervalMilliseconds.toLong(),
                TimeUnit.MILLISECONDS,
            )
        }
    }

    private fun captureAndSendSessionJpeg() {
        val state = synchronized(lock) {
            if (!initialized || !active || currentLifecycleState == AppLifecycleState.Background) {
                return
            }
            val captureOptions = options.sessionJpegCapture ?: return
            val transport = liveTransport?.takeIf { it.isOpen } ?: return
            captureOptions to transport
        }

        val screenshot = try {
            AndroidUiEvidence.captureScreenshot("jpeg", state.first.quality, state.first.maxWidth)
        } catch (_: Exception) {
            return
        }

        val result = state.second.sendSessionJpegFrame(screenshot, state.first.quality)
        if (!result.success) {
            synchronized(lock) {
                sessionMessage = result.message
                stopSessionJpegCaptureLocked()
            }
        }
    }

    private fun stopSessionJpegCaptureLocked() {
        sessionJpegTask?.cancel(false)
        sessionJpegTask = null
        sessionJpegExecutor?.shutdownNow()
        sessionJpegExecutor = null
    }

    private fun nextDeviceProfileLocked(app: Application, increment: Boolean): DeviceAppProfile {
        if (increment) {
            profileSequence += 1
        }
        return DeviceAppProfileCollector.collect(app, profileSeq = profileSequence.coerceAtLeast(1))
    }

    private fun makeChannelDictionary(options: AnsightOptions): LinkedHashMap<Int, AnsightChannel> {
        val dictionary = linkedMapOf<Int, AnsightChannel>()
        if (options.defaultMemoryChannels.javaHeap) {
            dictionary[AnsightChannels.JavaHeap] = AnsightChannel(AnsightChannels.JavaHeap, "Java heap", "#5C2D90", "bytes", "memory")
        }
        if (options.defaultMemoryChannels.nativeHeap) {
            dictionary[AnsightChannels.NativeHeap] = AnsightChannel(AnsightChannels.NativeHeap, "Native heap", "#007AFF", "bytes", "memory")
        }
        if (options.defaultMemoryChannels.rss) {
            dictionary[AnsightChannels.Rss] = AnsightChannel(AnsightChannels.Rss, "RSS", "#C88C1E", "bytes", "memory")
        }
        if (options.enableFramesPerSecond) {
            dictionary[AnsightChannels.FramesPerSecond] = AnsightChannel(AnsightChannels.FramesPerSecond, "FPS", "#23B573", "fps", "frames")
        }
        dictionary[AnsightChannels.Lifecycle] = AnsightChannel(AnsightChannels.Lifecycle, "Lifecycle", "#FF9500", null, "lifecycle")
        if (options.enableBatteryLevel) {
            dictionary[AnsightChannels.BatteryLevel] = AnsightChannel(AnsightChannels.BatteryLevel, "Battery Level", "#FFCC00", "percent", "battery")
        }
        dictionary[AnsightChannels.Unspecified] = AnsightChannel(AnsightChannels.Unspecified, "Unspecified", null, null, "unspecified")
        options.additionalChannels.forEach { dictionary[it.id] = it }
        return dictionary
    }

    private fun validateChannel(channel: Int): Int {
        require(channel in 0..255) { "Channel ids must be between 0 and 255." }
        return channel
    }

    private fun ensureChannelLocked(channel: Int) {
        if (channels[channel] == null) {
            channels[channel] = AnsightChannel(channel, "Channel $channel")
        }
    }

    private fun sourceFor(kind: HostConnectionRequestKind): HostConnectionSource = when (kind) {
        HostConnectionRequestKind.Auto -> HostConnectionSource.AutoProbe
        HostConnectionRequestKind.SavedConfig -> HostConnectionSource.SavedConfig
        HostConnectionRequestKind.BundledConfig -> HostConnectionSource.BundledConfig
        HostConnectionRequestKind.File,
        HostConnectionRequestKind.QrCode -> HostConnectionSource.ConfigReader
        HostConnectionRequestKind.Payload -> HostConnectionSource.Payload
        HostConnectionRequestKind.Config -> HostConnectionSource.HostConnection
    }

    private fun reasonCodeFor(error: Throwable): String? {
        val message = error.message.orEmpty()
        return when {
            message.contains("expired", ignoreCase = true) -> PairingFailureCodes.PairingTokenExpired
            message.contains("signature", ignoreCase = true) -> PairingFailureCodes.PairingProofInvalid
            message.contains("appId", ignoreCase = true) -> PairingFailureCodes.PairingRequired
            else -> null
        }
    }

    private fun loadSavedPairingConfig(app: Application): String? {
        return app.getSharedPreferences(options.hostConnection.savedConfigKey, Application.MODE_PRIVATE)
            .getString("payload", null)
            ?.trim()
            ?.ifBlank { null }
    }

    private fun savePairingConfigLocked(app: Application, payload: String) {
        app.getSharedPreferences(options.hostConnection.savedConfigKey, Application.MODE_PRIVATE)
            .edit()
            .putString("payload", payload)
            .putLong("savedAtEpochMs", System.currentTimeMillis())
            .apply()
    }

    private fun runConnectionOffCallingThread(block: () -> HostConnectionResult): HostConnectionResult {
        val result = arrayOfNulls<HostConnectionResult>(1)
        val error = arrayOfNulls<Throwable>(1)
        val thread = Thread {
            try {
                result[0] = block()
            } catch (throwable: Throwable) {
                error[0] = throwable
            }
        }.apply {
            name = "AnsightAndroidConnect"
            isDaemon = true
            start()
        }
        thread.join(35_000)
        if (thread.isAlive) {
            thread.interrupt()
            return HostConnectionResult.failure(
                "Timed out connecting to Ansight host.",
                kind = HostConnectionActionKind.Connect,
                source = HostConnectionSource.HostConnection,
                reasonCode = PairingFailureCodes.UdpBootstrapTimeout,
            )
        }
        error[0]?.let { throw it }
        return result[0] ?: HostConnectionResult.failure("Connection did not return a result.")
    }

    private data class ResolvedConnectionCandidate(
        val payload: String,
        val source: HostConnectionSource,
        val shouldSaveOnSuccess: Boolean = false,
        val usedEmbeddedDeveloperPairing: Boolean = false,
    )

    private data class TelemetryBatch(
        val metrics: List<RecordedMetric>,
        val events: List<RecordedEvent>,
        val touches: List<RecordedTouch>,
    )
}

private class AndroidFrameRateSampler {
    private val mainHandler = Handler(Looper.getMainLooper())
    private var running = false
    private var frameCount = 0
    private var lastSampleNanos = 0L
    private var lastFramesPerSecond = 0

    private val callback = object : Choreographer.FrameCallback {
        override fun doFrame(frameTimeNanos: Long) {
            if (!running) {
                return
            }

            if (lastSampleNanos == 0L) {
                lastSampleNanos = frameTimeNanos
            }
            frameCount += 1
            val elapsed = frameTimeNanos - lastSampleNanos
            if (elapsed >= 1_000_000_000L) {
                lastFramesPerSecond = ((frameCount * 1_000_000_000.0) / elapsed).toInt().coerceIn(0, 1_000)
                frameCount = 0
                lastSampleNanos = frameTimeNanos
            }
            Choreographer.getInstance().postFrameCallback(this)
        }
    }

    fun start() {
        if (running) {
            return
        }
        running = true
        frameCount = 0
        lastSampleNanos = 0
        mainHandler.post {
            Choreographer.getInstance().postFrameCallback(callback)
        }
    }

    fun stop() {
        running = false
        mainHandler.post {
            Choreographer.getInstance().removeFrameCallback(callback)
        }
    }

    fun consumeFramesPerSecond(): Int {
        val value = lastFramesPerSecond
        lastFramesPerSecond = 0
        return value
    }
}
