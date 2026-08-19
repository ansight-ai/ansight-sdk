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
import java.util.concurrent.atomic.AtomicInteger

object AnsightRuntime {
    private val lock = Any()
    private var application: Application? = null
    private val connector = PairingSessionConnector(
        simulatorLocalHostAddressProvider = { PairingSimulatorLocalHostAddress.resolve() },
        networkStatusProvider = { PairingNetworkPreflight.getStatus(application) },
        applicationProvider = { application },
    )
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
    private var touchVisualTreeCaptureCoordinator: TouchVisualTreeCaptureCoordinator? = null
    private var hostSessionJpegCapturePolicy = HostSessionJpegCapturePolicy.App
    private var autoProbeExecutor: ScheduledExecutorService? = null
    private var autoProbeTask: ScheduledFuture<*>? = null
    private val sessionPropertiesExecutor = Executors.newSingleThreadExecutor { runnable ->
        Thread(runnable, "AnsightSessionProperties").apply { isDaemon = true }
    }
    private var lifecycleCallbacks: Application.ActivityLifecycleCallbacks? = null
    private var startedActivityCount = 0
    private var unattendedProvisioningInProgress = false
    private val frameRateSampler = AndroidFrameRateSampler()
    private var frameRateTrackingEnabled = false
    private var touchCaptureRuntimeEnabled = true
    private var touchCaptureGuard: (() -> Boolean)? = null

    private val metrics = mutableListOf<RecordedMetric>()
    private val events = mutableListOf<RecordedEvent>()
    private val touches = mutableListOf<RecordedTouch>()
    private val channels = linkedMapOf<Int, AnsightChannel>()
    private val metricStreams = linkedMapOf<Int, AnsightMetricStream>()
    private val externallyManagedMetricChannels = mutableSetOf<Int>()
    private val tools = linkedMapOf<String, AnsightToolDescriptor>()
    private val toolRegistry = AndroidToolRegistry()
    private var externalToolProtocolHandler: ExternalToolProtocolHandler? = null
    private var externalToolProtocolResponseSentHandler: ((String) -> Unit)? = null
    private var externalToolIds: List<String> = emptyList()
    private var externalToolCategories: List<String> = emptyList()
    private val hostConnectionStatusListeners = linkedMapOf<Int, HostConnectionStatusListener>()
    private val nextHostConnectionStatusListenerId = AtomicInteger(1)
    private var lastPublishedHostConnectionStatus: HostConnectionStatus? = null
    private var lastPublishedHostConnectionCapabilities: HostConnectionCapabilities? = null

    @JvmOverloads
    fun initialize(application: Application, options: AnsightOptions = AnsightOptions()) {
        AnsightLogger.info("Initializing Ansight runtime.")
        val validated = options.validated()
        AnsightCrashCapture.initialize(application, validated.crashCapture)
        synchronized(lock) {
            deactivateLocked(closeTransport = true)
            this.application = application
            this.options = validated
            channels.clear()
            channels.putAll(makeChannelDictionary(validated))
            metricStreams.clear()
            externallyManagedMetricChannels.clear()
            metrics.clear()
            events.clear()
            touches.clear()
            tools.clear()
            toolRegistry.clear()
            externalToolProtocolHandler = null
            externalToolProtocolResponseSentHandler = null
            externalToolIds = emptyList()
            externalToolCategories = emptyList()
            validated.initialTools.forEach { tool -> registerToolLocked(tool) }
            if (validated.artifactProviders.isNotEmpty()) {
                AndroidArtifactTools.create { this.options.artifactProviders }
                    .filterNot { toolRegistry.contains(it.definition.id) }
                    .forEach { tool -> registerToolLocked(tool) }
            }
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
            frameRateTrackingEnabled = validated.enableFramesPerSecond
            touchCaptureRuntimeEnabled = true
            touchCaptureGuard = null
            hostId = null
            hostName = null
            resolvedHostAddress = null
            sessionMessage = "Runtime initialized."
            unattendedProvisioningInProgress = false
        }
        publishHostConnectionStatusIfChanged(force = true)
        AnsightLogger.info("Ansight runtime initialized.")
    }

    @JvmOverloads
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
        AnsightLogger.info("Ansight runtime activated.")

        startLifecycleCapture(app)
        bindCurrentActivity(app)
        startTelemetrySampling()
        if (frameRateTrackingEnabled) {
            frameRateSampler.start()
        }
        AndroidUiEvidence.setTouchCaptureEnabled(options.touchCapture != null && touchCaptureRuntimeEnabled) { touch -> onTouchCaptured(touch) }
        startAutoProbeIfNeeded(app)
        publishHostConnectionStatusIfChanged()
    }

    fun bindActivity(activity: Activity) {
        AndroidUiEvidence.onActivityResumed(activity)
        recordBoundActivity()
        startUnattendedProvisioningIfNeeded(activity)
    }

    private fun bindCurrentActivity(app: Application) {
        val activity = AndroidUiEvidence.bindCurrentActivity(app)
        if (activity != null) {
            recordBoundActivity()
            startUnattendedProvisioningIfNeeded(activity)
        }
    }

    private fun recordBoundActivity() {
        val shouldRecordForeground = synchronized(lock) {
            if (!initialized) {
                return
            }
            if (active && startedActivityCount == 0) {
                startedActivityCount = 1
            }
            active
        }

        if (shouldRecordForeground) {
            setAppLifecycleState(AppLifecycleState.Foreground)
        }
    }

    fun deactivate() {
        synchronized(lock) {
            deactivateLocked(closeTransport = true)
            sessionMessage = "Runtime deactivated."
        }
        publishHostConnectionStatusIfChanged()
        AnsightLogger.info("Ansight runtime deactivated.")
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
        publishHostConnectionStatusIfChanged()
    }

    fun registerMetricChannel(channel: AnsightChannel) {
        val validated = channel.validated()
        require(validated.id !in AnsightChannels.reservedIds) { "Channel id ${validated.id} is reserved." }
        synchronized(lock) {
            channels[validated.id] = validated
            announcedMetricChannelIds.remove(validated.id)
            sessionMessage = "Registered metric channel ${validated.id}."
        }
    }

    fun registerMetricStream(stream: AnsightMetricStream) {
        val validated = stream.channel.validated()
        require(validated.id !in AnsightChannels.reservedIds) { "Channel id ${validated.id} is reserved." }
        val validatedStream = AnsightMetricStream(validated, AnsightMetricSampler { stream.sample() })
        synchronized(lock) {
            channels[validated.id] = validated
            metricStreams[validated.id] = validatedStream
            announcedMetricChannelIds.remove(validated.id)
            sessionMessage = "Registered metric stream ${validated.id}."
        }
    }

    fun setMetricChannelExternallyManaged(channel: Int, externallyManaged: Boolean) {
        synchronized(lock) {
            require(initialized) { "AnsightRuntime must be initialized before configuring metric channels." }
            require(channels.containsKey(channel)) { "Metric channel $channel is not configured." }
            if (externallyManaged) {
                externallyManagedMetricChannels += channel
            } else {
                externallyManagedMetricChannels -= channel
            }
        }
    }

    fun metric(value: Long, channel: Int = AnsightChannels.Unspecified) {
        synchronized(lock) {
            require(initialized) { "AnsightRuntime must be initialized before recording metrics." }
            recordMetricLocked(value, validateChannel(channel))
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
        AnsightCrashCapture.recordBreadcrumb("event", trimmedLabel, details)
        streamPendingTelemetry()
    }

    fun screenViewed(
        name: String,
        details: Map<String, String> = emptyMap(),
        channel: Int = AnsightChannels.Lifecycle,
    ) {
        val trimmedName = name.trim()
        require(trimmedName.isNotBlank()) { "Screen name must not be blank." }
        val sanitizedDetails = details
            .mapKeys { it.key.trim() }
            .filterKeys { it.isNotBlank() }
            .mapValues { it.value.trim() }
        val detailsText = if (sanitizedDetails.isEmpty()) null else JSONObject(sanitizedDetails).toString()

        synchronized(lock) {
            require(initialized) { "AnsightRuntime must be initialized before recording screen views." }
            currentScreen = RecordedScreenView(trimmedName, sanitizedDetails)
            recordEventLocked(
                RecordedEvent(
                    label = trimmedName,
                    type = AnsightEventType.ScreenViewed,
                    details = detailsText,
                    channel = validateChannel(channel),
                    sequence = ++nextEventSequence,
                ),
            )
            sessionMessage = "Recorded screen view $trimmedName."
        }
        AnsightCrashCapture.recordBreadcrumb("screen", trimmedName, detailsText)
        streamPendingTelemetry()
    }

    @JvmOverloads
    fun recordCrashCandidate(
        runtime: String,
        kind: String = "unhandled_exception",
        message: String? = null,
        stack: String? = null,
        fatal: Boolean = true,
        metadataJson: String? = null,
    ): String? = AnsightCrashCapture.recordCandidate(
        runtime = runtime,
        kind = kind,
        message = message,
        stack = stack,
        fatal = fatal,
        metadata = metadataJson?.trim()?.ifBlank { null }?.let { JSONObject(it) },
    )

    fun processSessionId(): String = AnsightCrashCapture.processSessionId()

    fun pendingCrashReportsJson(): String = AnsightCrashCapture.pendingReportsJson()

    fun associateOfflineCaptureSession(sessionId: String, directory: String? = null) {
        AnsightCrashCapture.associateOfflineSession(sessionId, directory)
    }

    fun completeOfflineCaptureSession(sessionId: String) {
        AnsightCrashCapture.markOfflineSessionCompleted(sessionId)
    }

    fun markCrashReportPersistedToOfflineCapture(reportId: String): Boolean =
        AnsightCrashCapture.markOfflineReportPersisted(reportId)

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

        AnsightCrashCapture.recordBreadcrumb("lifecycle", state.wireName)

        when (state) {
            AppLifecycleState.Foreground -> startTouchVisualTreeCaptureIfNeeded()
            AppLifecycleState.Background -> synchronized(lock) { stopTouchVisualTreeCaptureLocked() }
            AppLifecycleState.Unknown -> Unit
        }

        streamPendingTelemetry()
        if (shouldSend) {
            sendCurrentAppState()
        }
    }

    internal fun onTouchCaptured(touch: CapturedTouch) {
        val touchVisualTreeCaptureCoordinator = synchronized(lock) {
            if (!initialized || !active || options.touchCapture == null || !touchCaptureRuntimeEnabled) {
                return
            }
            val guard = touchCaptureGuard
            if (guard != null && !runCatching { guard() }.getOrDefault(false)) {
                touchVisualTreeCaptureCoordinator?.interruptGesture()
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
            touchVisualTreeCaptureCoordinator
        }
        touchVisualTreeCaptureCoordinator?.observe(touch)
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

        val result = runConnectionOffCallingThread {
            connectInternal(request)
        }
        publishHostConnectionStatusIfChanged()
        return result
    }

    fun disconnect(): HostConnectionResult {
        val transport = synchronized(lock) {
            connectionState = HostConnectionState.Disconnecting
            stopSessionJpegCaptureLocked()
            stopTouchVisualTreeCaptureLocked()
            liveTransport.also {
                liveTransport = null
                sessionOpen = false
                sessionId = null
                hostSessionJpegCapturePolicy = HostSessionJpegCapturePolicy.App
                connectionState = HostConnectionState.Disconnected
                sessionMessage = "Disconnected from Ansight host."
            }
        }
        transport?.close(notify = false)
        val result = HostConnectionResult.success(
            "Disconnected from Ansight host.",
            kind = HostConnectionActionKind.Disconnect,
            source = HostConnectionSource.HostConnection,
        )
        publishHostConnectionStatusIfChanged()
        return result
    }

    fun completeSession() {
        liveTransport?.sendControlRequest(
            PairingControlActions.SessionComplete,
            JSONObject().put("reason", "client log stream complete"),
            timeoutMilliseconds = 10_000,
        )
        AnsightCrashCapture.markStudioSessionCompleted()
        closeSession()
    }

    fun closeSession() {
        disconnect()
    }

    fun clearSavedPairingConfig(): HostConnectionResult {
        val app = synchronized(lock) { application }
            ?: return HostConnectionResult.failure("AnsightRuntime is not initialized.", HostConnectionActionKind.ClearSavedConfig)
        clearStoredPairingProfile(app)
        synchronized(lock) {
            stopAutoProbeLocked()
            sessionMessage = "Saved Studio registration cleared."
        }
        val result = HostConnectionResult.success(
            "Saved Studio registration cleared.",
            kind = HostConnectionActionKind.ClearSavedConfig,
            source = HostConnectionSource.SavedConfig,
        )
        publishHostConnectionStatusIfChanged()
        return result
    }

    fun clearCachedSession(): OperationResult {
        val app = synchronized(lock) { application }
            ?: return OperationResult.failure("AnsightRuntime is not initialized.")
        clearCachedPairingProfile(app)
        synchronized(lock) {
            stopAutoProbeLocked()
            sessionMessage = "Cached pairing session cleared."
        }
        publishHostConnectionStatusIfChanged()
        return OperationResult.success("Cached pairing session cleared.")
    }

    fun notifyHostConnectionConfigChanged(): HostConnectionResult {
        val result = synchronized(lock) {
            if (!initialized) {
                return HostConnectionResult.failure(
                    "AnsightRuntime must be initialized before refreshing host connection config state.",
                    kind = HostConnectionActionKind.NotifyConfigChanged,
                    source = HostConnectionSource.ConfigReader,
                )
            }

            val status = hostConnectionStatusLocked()
            sessionMessage = status.summaryMessage
            HostConnectionResult.success(
                status.summaryMessage,
                kind = HostConnectionActionKind.NotifyConfigChanged,
                source = when {
                    options.hostConnection.bundledConfigJson != null -> HostConnectionSource.BundledConfig
                    else -> HostConnectionSource.ConfigReader
                },
            )
        }
        publishHostConnectionStatusIfChanged(force = true)
        return result
    }

    @JvmOverloads
    fun savePairingConfig(pairingJson: String, expectedAppId: String? = null): HostConnectionResult {
        val app = synchronized(lock) { application }
            ?: return HostConnectionResult.failure("AnsightRuntime is not initialized.", HostConnectionActionKind.Connect)

        return try {
            val normalizedExpectedAppId = expectedAppId?.trim()?.ifBlank { null } ?: app.packageName
            PairingConfigDocumentService.parseAndValidateDocument(pairingJson, normalizedExpectedAppId)
            savePairingConfigLocked(app, pairingJson)
            startAutoProbeIfNeeded(app)
            val result = HostConnectionResult.success("Saved Studio registration.", source = HostConnectionSource.SavedConfig)
            publishHostConnectionStatusIfChanged()
            result
        } catch (ex: Exception) {
            HostConnectionResult.failure(
                ex.message ?: "Pairing config could not be saved.",
                source = HostConnectionSource.SavedConfig,
                reasonCode = reasonCodeFor(ex),
            )
        }
    }

    fun isFramesPerSecondEnabled(): Boolean = synchronized(lock) { frameRateTrackingEnabled }

    fun enableFramesPerSecond() {
        val shouldStart = synchronized(lock) {
            frameRateTrackingEnabled = true
            channels[AnsightChannels.FramesPerSecond] = framesPerSecondChannel()
            sessionMessage = "Frames-per-second sampling enabled."
            active
        }
        if (shouldStart) {
            frameRateSampler.start()
        }
    }

    fun disableFramesPerSecond() {
        synchronized(lock) {
            frameRateTrackingEnabled = false
            sessionMessage = "Frames-per-second sampling disabled."
        }
        frameRateSampler.stop()
    }

    fun isTouchCaptureEnabled(): Boolean = synchronized(lock) {
        initialized && options.touchCapture != null && touchCaptureRuntimeEnabled
    }

    fun setTouchCaptureGuard(guard: (() -> Boolean)?) {
        synchronized(lock) {
            touchCaptureGuard = guard
            sessionMessage = if (guard == null) "Touch capture guard cleared." else "Touch capture guard configured."
        }
    }

    fun enableTouchCapture(): OperationResult {
        val canEnable = synchronized(lock) {
            if (!initialized) {
                return OperationResult.failure("AnsightRuntime must be initialized before enabling touch capture.")
            }
            if (options.touchCapture == null) {
                sessionMessage = "Touch capture is not configured."
                return OperationResult.failure("Touch capture is not configured.")
            }
            touchCaptureRuntimeEnabled = true
            sessionMessage = "Touch capture enabled."
            active
        }
        if (canEnable) {
            AndroidUiEvidence.setTouchCaptureEnabled(true) { touch -> onTouchCaptured(touch) }
            startTouchVisualTreeCaptureIfNeeded()
        }
        return OperationResult.success("Touch capture enabled.")
    }

    fun disableTouchCapture(): OperationResult {
        synchronized(lock) {
            if (!initialized) {
                return OperationResult.failure("AnsightRuntime must be initialized before disabling touch capture.")
            }
            touchCaptureRuntimeEnabled = false
            stopTouchVisualTreeCaptureLocked()
            sessionMessage = "Touch capture disabled."
        }
        AndroidUiEvidence.setTouchCaptureEnabled(false, null)
        return OperationResult.success("Touch capture disabled.")
    }

    fun captureScreenFrame(options: AnsightSessionJpegCaptureOptions? = null): OperationResult {
        val state = synchronized(lock) {
            val transport = liveTransport
            if (!initialized || !active || !sessionOpen || connectionState != HostConnectionState.Connected || transport?.isOpen != true) {
                sessionMessage = "A connected live session is required before capturing a screen frame."
                return OperationResult.failure("A connected live session is required before capturing a screen frame.")
            }

            val captureOptions = (options ?: this.options.sessionJpegCapture ?: AnsightSessionJpegCaptureOptions()).validated()
            captureOptions to transport
        }
        application?.let { app -> bindCurrentActivity(app) }

        val screenshot = try {
            AndroidUiEvidence.captureSessionScreenshot("jpeg", state.first.quality, state.first.maxWidth)
        } catch (ex: Exception) {
            val message = ex.message ?: "Android screen frame capture failed."
            synchronized(lock) {
                sessionMessage = message
            }
            return OperationResult.failure(message)
        }

        val capturedAtEpochMs = System.currentTimeMillis()
        val capturedAtUtc = AnsightClock.isoAt(capturedAtEpochMs)
        val visualTrees = captureSessionVisualTrees(state.first, state.second, capturedAtUtc)
        var result = state.second.sendSessionJpegFrame(screenshot, state.first.quality, capturedAtEpochMs)
        if (result.success) {
            for (visualTree in visualTrees) {
                result = state.second.sendSessionVisualTree(visualTree, capturedAtUtc)
                if (!result.success) break
            }
        }
        val message = if (result.success) {
            "Captured and sent screen frame ${screenshot.width}x${screenshot.height} (${screenshot.bytes.size} bytes)."
        } else {
            result.message
        }
        synchronized(lock) {
            sessionMessage = message
            if (!result.success) {
                stopSessionJpegCaptureLocked()
            }
        }
        return OperationResult(result.success, message)
    }

    fun captureBuiltInTelemetrySample() {
        sampleBuiltInTelemetry()
    }

    fun isToolRegistered(toolId: String): Boolean =
        synchronized(lock) { toolRegistry.contains(toolId) }

    fun setExternalToolProtocolHandler(handler: ExternalToolProtocolHandler?) {
        synchronized(lock) {
            externalToolProtocolHandler = handler
            sessionMessage = if (handler == null) {
                "External tool protocol handler cleared."
            } else {
                "External tool protocol handler registered."
            }
        }
    }

    fun setExternalToolProtocolCallback(handler: ((String) -> String?)?) {
        setExternalToolProtocolHandler(
            handler?.let { callback ->
                ExternalToolProtocolHandler { requestJson -> callback(requestJson) }
            },
        )
    }

    fun setExternalToolProtocolResponseSentCallback(handler: ((String) -> Unit)?) {
        synchronized(lock) {
            externalToolProtocolResponseSentHandler = handler
        }
    }

    fun setExternalToolCatalog(toolIds: List<String>, toolCategories: List<String>) {
        synchronized(lock) {
            externalToolIds = toolIds.distinct()
            externalToolCategories = toolCategories.distinct()
        }
    }

    @JvmOverloads
    fun registerTool(tool: AnsightToolDescriptor, replaceExisting: Boolean = false) {
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
                replaceExisting = replaceExisting,
            )
            sessionMessage = "Registered tool ${tool.id}."
        }
    }

    @JvmOverloads
    fun registerTool(tool: AndroidTool, replaceExisting: Boolean = false) {
        synchronized(lock) {
            registerToolLocked(tool, replaceExisting)
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

    fun sendControlRequest(action: String, payload: JSONObject?): OperationResult {
        val normalizedAction = action.trim()
        if (normalizedAction.isEmpty()) {
            return OperationResult.failure("A control request action is required.")
        }

        val transport = synchronized(lock) { liveTransport }
            ?: return OperationResult.failure("WebSocket session is not open.")
        return transport.sendControlRequest(normalizedAction, payload)
    }

    fun sendBinaryData(bytes: ByteArray): OperationResult {
        val transport = synchronized(lock) { liveTransport }
            ?: return OperationResult.failure("WebSocket session is not open.")
        return transport.sendData(bytes)
    }

    fun updateCustomProperties(customProperties: Map<String, Map<String, String>>): OperationResult {
        val normalized = customProperties.normalizedCustomProperties()
        val shouldSend = synchronized(lock) {
            if (!initialized) {
                return OperationResult.failure("AnsightRuntime must be initialized before updating custom properties.")
            }

            options = options.copy(customProperties = normalized)
            sessionMessage = "Session properties updated locally."
            sessionOpen && liveTransport?.isOpen == true
        }
        return sendSessionPropertiesIfNeeded(shouldSend)
    }

    fun registerCustomProperty(group: String, key: String, value: String): OperationResult {
        val normalizedGroup = group.trim()
        if (normalizedGroup.isBlank()) {
            return OperationResult.failure("Custom property group must not be blank.")
        }
        val normalizedKey = key.trim()
        if (normalizedKey.isBlank()) {
            return OperationResult.failure("Custom property key must not be blank.")
        }

        val normalizedValue = value.trim()
        val shouldSend = synchronized(lock) {
            if (!initialized) {
                return OperationResult.failure("AnsightRuntime must be initialized before updating custom properties.")
            }

            val next = mutableCustomPropertiesLocked()
            val groupProperties = next.getOrPut(normalizedGroup) { linkedMapOf() }
            groupProperties[normalizedKey] = normalizedValue
            options = options.copy(customProperties = next.toImmutableCustomProperties())
            sessionMessage = "Session property $normalizedGroup.$normalizedKey updated locally."
            sessionOpen && liveTransport?.isOpen == true
        }
        return sendSessionPropertiesIfNeeded(shouldSend)
    }

    fun removeCustomProperty(group: String, key: String): OperationResult {
        val normalizedGroup = group.trim()
        if (normalizedGroup.isBlank()) {
            return OperationResult.failure("Custom property group must not be blank.")
        }
        val normalizedKey = key.trim()
        if (normalizedKey.isBlank()) {
            return OperationResult.failure("Custom property key must not be blank.")
        }

        val shouldSend = synchronized(lock) {
            if (!initialized) {
                return OperationResult.failure("AnsightRuntime must be initialized before updating custom properties.")
            }

            val next = mutableCustomPropertiesLocked()
            val removed = next[normalizedGroup]?.remove(normalizedKey) != null
            if (next[normalizedGroup]?.isEmpty() == true) {
                next.remove(normalizedGroup)
            }
            options = options.copy(customProperties = next.toImmutableCustomProperties())
            sessionMessage = if (removed) {
                "Session property $normalizedGroup.$normalizedKey removed locally."
            } else {
                "Session property $normalizedGroup.$normalizedKey was not registered."
            }
            sessionOpen && liveTransport?.isOpen == true
        }
        return sendSessionPropertiesIfNeeded(shouldSend)
    }

    fun clearCustomProperties(): OperationResult {
        val shouldSend = synchronized(lock) {
            if (!initialized) {
                return OperationResult.failure("AnsightRuntime must be initialized before updating custom properties.")
            }

            options = options.copy(customProperties = emptyMap())
            sessionMessage = "Session properties cleared locally."
            sessionOpen && liveTransport?.isOpen == true
        }
        return sendSessionPropertiesIfNeeded(shouldSend)
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

    fun recordedMetrics(): List<RecordedMetric> {
        synchronized(lock) {
            return metrics.toList()
        }
    }

    fun recordedEvents(): List<RecordedEvent> {
        synchronized(lock) {
            return events.toList()
        }
    }

    fun hostConnectionStatus(): HostConnectionStatus {
        synchronized(lock) {
            return hostConnectionStatusLocked()
        }
    }

    fun hostConnectionCapabilities(): HostConnectionCapabilities {
        synchronized(lock) {
            return hostConnectionCapabilitiesLocked()
        }
    }

    @JvmOverloads
    fun addHostConnectionStatusListener(
        listener: HostConnectionStatusListener,
        emitCurrent: Boolean = true,
    ): HostConnectionStatusSubscription {
        val listenerId = nextHostConnectionStatusListenerId.getAndIncrement()
        val current = synchronized(lock) {
            hostConnectionStatusListeners[listenerId] = listener
            if (emitCurrent) {
                hostConnectionSnapshotLocked().also { snapshot ->
                    lastPublishedHostConnectionStatus = snapshot.status
                    lastPublishedHostConnectionCapabilities = snapshot.capabilities
                }
            } else {
                null
            }
        }

        current?.let { snapshot ->
            runCatching { listener.onChanged(snapshot.status, snapshot.capabilities) }
        }

        return HostConnectionStatusSubscription {
            synchronized(lock) {
                hostConnectionStatusListeners.remove(listenerId)
            }
        }
    }

    private fun connectInternal(request: HostConnectionRequest): HostConnectionResult {
        val app = synchronized(lock) { application ?: error("AnsightRuntime has no application context.") }
        val candidates = resolveConnectionCandidates(app, request)
        if (candidates.isEmpty()) {
            AnsightLogger.warning("No Studio registration is available for ${request.kind}.")
            return HostConnectionResult.failure(
                "No Studio registration is available. Scan an enrollment QR in Ansight Studio.",
                kind = HostConnectionActionKind.Connect,
                source = sourceFor(request.kind),
                reasonCode = PairingFailureCodes.EnrollmentRequired,
            )
        }

        var lastResult: HostConnectionResult? = null
        var finalCandidateWasClearedSavedRegistration = false
        for ((index, candidate) in candidates.withIndex()) {
            val result = connectCandidate(app, request, candidate)
            if (result.success) {
                return result
            }
            if (request.kind == HostConnectionRequestKind.Auto) {
                when (candidate.source) {
                    HostConnectionSource.CachedSession -> {
                        if (shouldClearCachedPairingProfile(result.reasonCode)) {
                            clearCachedPairingProfile(app, candidate.networkKey)
                        }
                    }
                    HostConnectionSource.SavedConfig -> {
                        if (shouldClearStoredPairingProfile(result.reasonCode)) {
                            clearStoredPairingProfile(app)
                            finalCandidateWasClearedSavedRegistration = index == candidates.lastIndex
                            AnsightLogger.warning(
                                "Saved Studio registration is invalid and was cleared. Scan a fresh enrollment QR code.",
                            )
                        }
                    }
                    else -> Unit
                }
            }
            lastResult = result
            if (request.kind != HostConnectionRequestKind.Auto) {
                return result
            }
        }

        if (finalCandidateWasClearedSavedRegistration) {
            val message = "No Studio registration is available. Scan an enrollment QR in Ansight Studio."
            synchronized(lock) {
                connectionState = HostConnectionState.Disconnected
                sessionMessage = message
            }
            publishHostConnectionStatusIfChanged()
            return HostConnectionResult.failure(
                message,
                kind = HostConnectionActionKind.Connect,
                source = HostConnectionSource.AutoProbe,
                reasonCode = PairingFailureCodes.EnrollmentRequired,
            )
        }

        return lastResult ?: HostConnectionResult.failure(
            "No Studio registration is available. Scan an enrollment QR in Ansight Studio.",
            kind = HostConnectionActionKind.Connect,
            source = sourceFor(request.kind),
            reasonCode = PairingFailureCodes.EnrollmentRequired,
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
            publishHostConnectionStatusIfChanged()
            AnsightLogger.warning(ex.message ?: "Pairing config is invalid.", ex)
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
        publishHostConnectionStatusIfChanged()

        val attempt = connector.connect(
            document = document,
            clientName = clientName,
            options = PairingConnectionOptions(
                hostAddressOverride = originalRequest.hostAddressOverride?.trim()?.ifBlank { null }
                    ?: candidate.hostAddressOverride,
                discoveryPort = options.hostConnection.discoveryPort,
                allowCellularConnections = options.hostConnection.allowCellularConnections,
            ),
        )

        if (!attempt.success || attempt.transport == null || attempt.connectResponse == null) {
            synchronized(lock) {
                connectionState = HostConnectionState.Disconnected
                sessionOpen = false
                sessionId = null
                sessionMessage = attempt.message
            }
            publishHostConnectionStatusIfChanged()
            if (originalRequest.kind == HostConnectionRequestKind.Auto &&
                document.config.configId.startsWith(PairingEnrollmentModes.LocalConfigPrefix)
            ) {
                AnsightLogger.debug(attempt.message)
            } else {
                AnsightLogger.warning(attempt.message)
            }
            val open = OpenSessionResult(
                success = false,
                accepted = attempt.accepted,
                message = attempt.message,
                configId = document.config.configId,
                appId = document.config.appId,
                resolvedHostAddress = attempt.hostAddress,
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
        val customProperties = synchronized(lock) { options.customProperties }
        if (customProperties.isNotEmpty()) {
            openPayload.put("customProperties", customProperties.toJSONObject())
        }

        val sessionOpenResult = transport.sendControlRequest(PairingControlActions.SessionOpen, openPayload)
        if (!sessionOpenResult.success) {
            transport.close(notify = false)
            synchronized(lock) {
                connectionState = HostConnectionState.Disconnected
                sessionMessage = sessionOpenResult.message
            }
            publishHostConnectionStatusIfChanged()
            return HostConnectionResult.failure(
                sessionOpenResult.message,
                kind = HostConnectionActionKind.Connect,
                source = HostConnectionSource.Transport,
                reasonCode = PairingFailureCodes.WebSocketHandshakeFailed,
            )
        }

        val profile = synchronized(lock) { nextDeviceProfileLocked(app, increment = true) }
        val profileRequestResult = transport.sendControlRequestWithResponse(
            PairingControlActions.DeviceProfile,
            profile.toJson().put(
                HostSessionJpegCapturePolicy.ControlVersionPropertyName,
                HostSessionJpegCapturePolicy.ControlVersion,
            ),
        )
        val profileResult = profileRequestResult.operationResult
        synchronized(lock) {
            hostSessionJpegCapturePolicy = HostSessionJpegCapturePolicy.fromPayload(
                profileRequestResult.response?.optJSONObject("payload"),
            )
        }
        if (!profileResult.success) {
            transport.close(notify = false)
            synchronized(lock) {
                connectionState = HostConnectionState.Disconnected
                sessionMessage = profileResult.message
            }
            publishHostConnectionStatusIfChanged()
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
        AnsightCrashCapture.associateStudioSession(
            hostId = attempt.connectResponse.hostId,
            configId = document.config.configId,
            appId = document.config.appId,
        )
        publishHostConnectionStatusIfChanged()
        AnsightLogger.info("Connected to Ansight host.")

        sendCurrentAppState()
        sendSessionProperties()
        sendMetricChannelDefinitions()
        startSessionJpegCaptureIfNeeded()
        startTouchVisualTreeCaptureIfNeeded()
        streamPendingTelemetry()
        AnsightCrashCapture.deliverPendingReports(transport)

        if (!document.config.configId.startsWith(PairingEnrollmentModes.LocalConfigPrefix)) {
            if (candidate.shouldSaveOnSuccess) {
                savePairingConfigLocked(app, candidate.payload)
            }
            saveCachedPairingProfile(app, candidate.payload, attempt.hostAddress, document)
        }

        val open = OpenSessionResult(
            success = true,
            accepted = true,
            message = "Connected to Ansight host.",
            sessionId = ProcessSessionIdentity.current,
            configId = document.config.configId,
            appId = document.config.appId,
            resolvedHostAddress = attempt.hostAddress,
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
        val cached = { loadCachedPairingProfiles(app) }
        val local: () -> List<ResolvedConnectionCandidate> = local@{
            val hostAddress = connector.localHostAddress() ?: return@local emptyList()
            val clientName = request.clientName?.trim()?.ifBlank { null }
                ?: options.hostAutoProbe.clientName?.trim()?.ifBlank { null }
                ?: DeviceAppProfileCollector.collect(app, profileSeq = 0).app.appName
                ?: app.packageName
            val discoveryPorts = options.hostConnection.discoveryPort?.let(::listOf)
                ?: PairingProtocolDefaults.LocalDiscoveryPorts
            discoveryPorts.map { discoveryPort ->
                ResolvedConnectionCandidate(
                    payload = LocalPairingDocumentFactory.createPayload(
                        application = app,
                        appName = clientName,
                        hostAddress = hostAddress,
                        discoveryPort = discoveryPort,
                    ),
                    source = HostConnectionSource.AutoProbe,
                    hostAddressOverride = hostAddress,
                )
            }
        }
        return when (request.kind) {
            HostConnectionRequestKind.Payload,
            HostConnectionRequestKind.Config -> listOfNotNull(
                request.payload?.trim()?.ifBlank { null }?.let {
                    ResolvedConnectionCandidate(it, sourceFor(request.kind), shouldSaveOnSuccess = true)
                },
            )
            HostConnectionRequestKind.QrCode -> listOfNotNull(
                readConfigPayloadFromReader(request)?.let {
                    ResolvedConnectionCandidate(it, HostConnectionSource.ConfigReader, shouldSaveOnSuccess = true)
                },
                request.payload?.trim()?.ifBlank { null }?.let {
                    ResolvedConnectionCandidate(it, HostConnectionSource.ConfigReader, shouldSaveOnSuccess = true)
                },
            )
            HostConnectionRequestKind.File -> listOfNotNull(
                readConfigPayloadFromReader(request),
                request.payload?.trim()?.ifBlank { null }?.let { path ->
                    runCatching { File(path).readText() }.getOrNull()
                },
            ).map { payload ->
                ResolvedConnectionCandidate(payload, HostConnectionSource.ConfigReader, shouldSaveOnSuccess = true)
            }
            HostConnectionRequestKind.SavedConfig -> listOfNotNull(
                saved()?.let { ResolvedConnectionCandidate(it, HostConnectionSource.SavedConfig) },
            )
            HostConnectionRequestKind.BundledConfig -> listOfNotNull(
                options.hostConnection.bundledConfigJson?.let { ResolvedConnectionCandidate(it, HostConnectionSource.BundledConfig) },
            )
            HostConnectionRequestKind.Auto ->
                local() +
                    cached() +
                    listOfNotNull(
                        saved()?.let { ResolvedConnectionCandidate(it, HostConnectionSource.SavedConfig) },
                        options.hostConnection.bundledConfigJson?.let { ResolvedConnectionCandidate(it, HostConnectionSource.BundledConfig) },
                    )
        }
    }

    private fun readConfigPayloadFromReader(request: HostConnectionRequest): String? {
        val reader = options.hostConnection.configReader ?: return null
        if (!runCatching { reader.canRead(request.kind) }.getOrDefault(false)) {
            return null
        }
        return runCatching { reader.readConfigPayload(request)?.trim()?.ifBlank { null } }.getOrNull()
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
            val visibleTools = toolRegistry.visible(options.toolGuard)
            val toolIds = if (externalToolProtocolHandler == null) {
                visibleTools.map { it.definition.id }
            } else {
                externalToolIds
            }
            val toolCategories = if (externalToolProtocolHandler == null) {
                visibleTools.map { it.definition.category }.distinct()
            } else {
                externalToolCategories
            }
            val capabilities = (
                listOf(
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
                ) + toolCategories + toolIds
                ).distinct()
            JSONObject()
                .put("schema", "ansight.session-properties.v1")
                .put("platform", "android")
                .put("sdk", "ansight-core-android")
                .put("sessionId", ProcessSessionIdentity.current)
                .put("updatedAtUtc", AnsightClock.isoNow())
                .put("customProperties", options.customProperties.toJSONObject())
                .put("toolGuard", options.toolGuard.toProtocolJson())
                .put("capabilities", JSONArray(capabilities))
                .put("tools", JSONObject()
                    .put("count", visibleTools.size)
                    .put("ids", JSONArray(toolIds)))
        }
        val transport = synchronized(lock) { liveTransport } ?: return OperationResult.failure("WebSocket session is not open.")
        return transport.sendControlRequest(PairingControlActions.SessionProperties, payload)
    }

    private fun sendSessionPropertiesIfNeeded(shouldSend: Boolean): OperationResult {
        if (!shouldSend) {
            return OperationResult.success("Session properties updated locally.")
        }

        sessionPropertiesExecutor.execute {
            val result = sendSessionProperties()
            synchronized(lock) {
                sessionMessage = if (result.success) "Session properties updated." else result.message
            }
            if (!result.success) {
                AnsightLogger.warning(result.message)
            }
        }
        return OperationResult.success("Session properties updated.")
    }

    private fun mutableCustomPropertiesLocked(): MutableMap<String, MutableMap<String, String>> {
        val result = linkedMapOf<String, MutableMap<String, String>>()
        options.customProperties.forEach { (group, properties) ->
            result[group] = LinkedHashMap(properties)
        }
        return result
    }

    private fun Map<String, Map<String, String>>.toImmutableCustomProperties(): Map<String, Map<String, String>> {
        return normalizedCustomProperties().mapValues { LinkedHashMap(it.value) }
    }

    private fun registerToolLocked(tool: AndroidTool, replaceExisting: Boolean = false) {
        val definition = tool.definition.validated()
        toolRegistry.register(tool, replaceExisting = replaceExisting)
        tools[definition.id] = AnsightToolDescriptor(
            id = definition.id,
            name = definition.name,
            scope = definition.scope.name,
        )
    }

    private fun handleTransportText(text: String) {
        val json = try {
            JSONObject(text)
        } catch (_: Exception) {
            return
        }
        when (json.optionalString("type")) {
            ToolProtocol.QueryType, ToolProtocol.CallType -> {
                val externalHandler = synchronized(lock) { externalToolProtocolHandler }
                if (externalHandler == null) {
                    if (json.optionalString("type") == ToolProtocol.QueryType) {
                        sendToolCatalog(json)
                    } else {
                        executeToolCall(json)
                    }
                } else {
                    executeExternalToolProtocol(text, json, externalHandler)
                }
            }
        }
    }

    private fun executeExternalToolProtocol(
        messageJson: String,
        request: JSONObject,
        handler: ExternalToolProtocolHandler,
    ) {
        Thread {
            val response = try {
                handler.handle(messageJson)
            } catch (ex: Exception) {
                toolErrorEnvelope(
                    request,
                    "tool_execution_exception",
                    ex.message ?: "External tool execution failed.",
                ).toString()
            }
            if (!response.isNullOrBlank()) {
                val sendResult = synchronized(lock) { liveTransport }?.sendText(response)
                if (sendResult?.success == true) {
                    synchronized(lock) { externalToolProtocolResponseSentHandler }
                        ?.invoke(messageJson)
                }
            }
        }.apply {
            name = "AnsightAndroidExternalToolCall"
            isDaemon = true
            start()
        }
    }

    private fun sendToolCatalog(request: JSONObject) {
        val catalogState = synchronized(lock) {
            Triple(application, liveTransport, options)
        }
        val app = catalogState.first
        val transport = catalogState.second
        val currentOptions = catalogState.third
        val response = if (app == null) {
            toolErrorEnvelope(request, "runtime_unavailable", "AnsightRuntime is not initialized.")
        } else if (!currentOptions.toolGuard.canDiscover(ToolScope.Read)) {
            toolErrorEnvelope(request, "tool_discovery_disabled", "Tool discovery is disabled by the current guard policy.")
        } else {
            val context = AndroidToolExecutionContext(
                application = app,
                transport = transport,
                sessionId = synchronized(lock) { sessionId },
                requestId = request.optionalString("id"),
                options = currentOptions,
            )
            val visibleTools = synchronized(lock) {
                toolRegistry.visible(currentOptions.toolGuard)
            }.map { tool ->
                val availability = tool.availability(context)
                tool.definition.toJson()
                    .put("runtime", availability.toJson())
                    .put("executable", availability.available)
            }
            toolEnvelope(
                type = ToolProtocol.CatalogType,
                request = request,
                payload = JSONObject()
                    .put("guard", currentOptions.toolGuard.toProtocolJson())
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
        val availability = tool.availability(context)
        if (!availability.available) {
            return toolErrorEnvelope(
                request = request,
                code = availability.reasonCode ?: "tool_unavailable",
                message = availability.reason ?: "Tool '$toolId' is not available in the current runtime state.",
                details = availability.toJson(),
                retryable = availability.retryable,
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

    private fun toolErrorEnvelope(
        request: JSONObject,
        code: String,
        message: String,
        details: JSONObject? = null,
        retryable: Boolean = false,
    ): JSONObject = toolEnvelope(
        type = ToolProtocol.ErrorType,
        request = request,
        payload = JSONObject()
            .put("success", false)
            .put("errorCode", code)
            .put("message", message)
            .put("retryable", retryable)
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
                            .putNullable("source", channel.source)
                            .putNullable("group", channel.group)
                            .putNullable("kind", channel.kind)
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
                    lastStreamedMetricSequence = maxSequence(lastStreamedMetricSequence, batch.metrics.last().sequence)
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
                    lastStreamedEventSequence = maxSequence(lastStreamedEventSequence, batch.events.last().sequence)
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
                    lastStreamedTouchSequence = maxSequence(lastStreamedTouchSequence, batch.touches.last().sequence)
                }
            }
        }
    }

    private fun maxSequence(current: Long, candidate: Long): Long = if (candidate > current) candidate else current

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
        val streams = synchronized(lock) {
            if (!initialized || !active) {
                emptyList()
            } else {
                metricStreams.values.toList()
            }
        }

        synchronized(lock) {
            if (!initialized || !active) {
                return
            }

            if (
                options.defaultMemoryChannels.javaHeap &&
                AnsightChannels.JavaHeap !in externallyManagedMetricChannels
            ) {
                recordMetricLocked(AndroidMetricSampler.javaHeapBytes(), AnsightChannels.JavaHeap)
            }
            if (
                options.defaultMemoryChannels.nativeHeap &&
                AnsightChannels.NativeHeap !in externallyManagedMetricChannels
            ) {
                recordMetricLocked(AndroidMetricSampler.nativeHeapBytes(), AnsightChannels.NativeHeap)
            }
            if (
                options.defaultMemoryChannels.rss &&
                AnsightChannels.Rss !in externallyManagedMetricChannels
            ) {
                val rss = AndroidMetricSampler.rssBytes()
                if (rss > 0) {
                    recordMetricLocked(rss, AnsightChannels.Rss)
                }
            }
            if (frameRateTrackingEnabled) {
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
            if (options.enableOpenFileHandleTracking) {
                AndroidMetricSampler.openFileHandleCount()?.let { count ->
                    recordMetricLocked(count, AnsightChannels.OpenFileHandles)
                }
            }
        }

        val streamMetrics = streams.mapNotNull { stream ->
            val value = stream.sample() ?: return@mapNotNull null
            RecordedMetric(value = value, channel = stream.channel.id)
        }

        if (streamMetrics.isNotEmpty()) {
            synchronized(lock) {
                if (initialized && active) {
                    streamMetrics.forEach { metric ->
                        if (channels.containsKey(metric.channel)) {
                            recordMetricLocked(metric.value, metric.channel)
                        }
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
                override fun onActivityCreated(activity: Activity, savedInstanceState: Bundle?) {
                    startUnattendedProvisioningIfNeeded(activity)
                }

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
                    startUnattendedProvisioningIfNeeded(activity)
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
        unattendedProvisioningInProgress = false
    }

    private fun startUnattendedProvisioningIfNeeded(activity: Activity) {
        val payload = synchronized(lock) {
            if (!initialized || !active || sessionOpen || unattendedProvisioningInProgress) {
                null
            } else {
                AnsightUnattendedProvisioning.consumePayload(
                    activity,
                    options.hostConnection.allowUnattendedProvisioning,
                )?.also {
                    unattendedProvisioningInProgress = true
                }
            }
        } ?: return
        val appId = activity.packageName

        Thread {
            try {
                val result = connect(
                    HostConnectionRequest.payloadText(
                        payload = payload,
                        expectedAppId = appId,
                    ),
                )
                if (!result.success) {
                    AnsightLogger.warning(result.message)
                }
            } catch (error: Throwable) {
                AnsightLogger.warning(error.message ?: "Unattended Ansight provisioning failed.", error)
            } finally {
                synchronized(lock) {
                    unattendedProvisioningInProgress = false
                }
            }
        }.apply {
            name = "AnsightAndroidUnattendedProvisioning"
            isDaemon = true
            start()
        }
    }

    private fun deactivateLocked(closeTransport: Boolean) {
        val hadUiEvidenceCapture = active || lifecycleCallbacks != null || sessionJpegTask != null || sessionJpegExecutor != null
        active = false
        sessionOpen = false
        sessionId = null
        hostSessionJpegCapturePolicy = HostSessionJpegCapturePolicy.App
        connectionState = HostConnectionState.Disconnected
        stopLifecycleCaptureLocked()
        telemetryTask?.cancel(false)
        telemetryTask = null
        telemetryExecutor?.shutdownNow()
        telemetryExecutor = null
        stopSessionJpegCaptureLocked(releaseUiResources = hadUiEvidenceCapture)
        stopTouchVisualTreeCaptureLocked()
        stopAutoProbeLocked()
        frameRateSampler.stop()
        if (hadUiEvidenceCapture) {
            AndroidUiEvidence.setTouchCaptureEnabled(false, null)
        }
        if (closeTransport) {
            liveTransport?.close(notify = false)
            liveTransport = null
        }
    }

    private fun publishHostConnectionStatusIfChanged(force: Boolean = false) {
        val notification = synchronized(lock) {
            val snapshot = hostConnectionSnapshotLocked()
            if (!force &&
                snapshot.status == lastPublishedHostConnectionStatus &&
                snapshot.capabilities == lastPublishedHostConnectionCapabilities
            ) {
                return
            }

            lastPublishedHostConnectionStatus = snapshot.status
            lastPublishedHostConnectionCapabilities = snapshot.capabilities

            if (hostConnectionStatusListeners.isEmpty()) {
                return
            }

            HostConnectionStatusNotification(
                listeners = hostConnectionStatusListeners.values.toList(),
                status = snapshot.status,
                capabilities = snapshot.capabilities,
            )
        }

        notification.listeners.forEach { listener ->
            runCatching { listener.onChanged(notification.status, notification.capabilities) }
        }
    }

    private fun hostConnectionSnapshotLocked(): HostConnectionSnapshot {
        val status = hostConnectionStatusLocked()
        return HostConnectionSnapshot(status, hostConnectionCapabilitiesLocked(status))
    }

    private fun hostConnectionStatusLocked(): HostConnectionStatus {
        val app = application
        val connected = sessionOpen && liveTransport?.isOpen == true && connectionState == HostConnectionState.Connected
        val hasSaved = app?.let { loadSavedPairingConfig(it) != null } ?: false
        val hasCached = app?.let { loadCachedPairingProfiles(it).isNotEmpty() } ?: false
        val hasBundled = options.hostConnection.bundledConfigJson != null
        return HostConnectionStatus(
            isRuntimeActive = active,
            isConnected = connected,
            connectionState = connectionState,
            hasCachedSession = hasCached,
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

    private fun hostConnectionCapabilitiesLocked(status: HostConnectionStatus = hostConnectionStatusLocked()): HostConnectionCapabilities {
        return HostConnectionCapabilities(
            canConnectUsingSavedConfig = initialized && status.hasSavedConfig,
            canConnectUsingBundledConfig = initialized && status.hasBundledConfig,
            canChooseConfigFile = initialized && hostConnectionReaderCanRead(HostConnectionRequestKind.File),
            canScanConfigQrCode = initialized && hostConnectionReaderCanRead(HostConnectionRequestKind.QrCode),
            canClearSavedConfigs = initialized && (status.hasSavedConfig || status.hasCachedSession),
        )
    }

    private fun hostConnectionReaderCanRead(kind: HostConnectionRequestKind): Boolean {
        val reader = options.hostConnection.configReader ?: return false
        return runCatching { reader.canRead(kind) }.getOrDefault(false)
    }

    private data class HostConnectionSnapshot(
        val status: HostConnectionStatus,
        val capabilities: HostConnectionCapabilities,
    )

    private data class HostConnectionStatusNotification(
        val listeners: List<HostConnectionStatusListener>,
        val status: HostConnectionStatus,
        val capabilities: HostConnectionCapabilities,
    )

    private fun startAutoProbeIfNeeded(app: Application) {
        synchronized(lock) {
            if (!initialized || !active || !options.hostAutoProbe.enabled || autoProbeTask != null) {
                return
            }
            if (loadCachedPairingProfiles(app).isEmpty() && connector.localHostAddress() == null) {
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

            synchronized(lock) { application } ?: return
            val request = HostConnectionRequest(
                kind = HostConnectionRequestKind.Auto,
                clientName = options.hostAutoProbe.clientName,
            )
            val result = try {
                connectInternal(request)
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

    private fun connectUsingCachedProfilesForAutoProbe(
        app: Application,
        request: HostConnectionRequest,
    ): HostConnectionResult {
        val candidates = loadCachedPairingProfiles(app)
        if (candidates.isEmpty()) {
            return HostConnectionResult.failure(
                "No remembered Ansight host profile is available.",
                kind = HostConnectionActionKind.Connect,
                source = HostConnectionSource.CachedSession,
                reasonCode = PairingFailureCodes.EnrollmentRequired,
            )
        }

        var lastResult: HostConnectionResult? = null
        for (candidate in candidates) {
            val result = connectCandidate(app, request, candidate)
            if (result.success) {
                return result
            }
            if (shouldClearCachedPairingProfile(result.reasonCode)) {
                clearCachedPairingProfile(app, candidate.networkKey)
            }
            lastResult = result
        }

        return lastResult ?: HostConnectionResult.failure(
            "No remembered Ansight host profile is available.",
            kind = HostConnectionActionKind.Connect,
            source = HostConnectionSource.CachedSession,
            reasonCode = PairingFailureCodes.EnrollmentRequired,
        )
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
            if (!initialized ||
                !active ||
                hostSessionJpegCapturePolicy.useHostCapture ||
                liveTransport?.isOpen != true ||
                sessionJpegTask != null
            ) {
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
        application?.let { app -> bindCurrentActivity(app) }

        val screenshot = try {
            AndroidUiEvidence.captureSessionScreenshot("jpeg", state.first.quality, state.first.maxWidth)
        } catch (_: Exception) {
            return
        }

        val capturedAtEpochMs = System.currentTimeMillis()
        val capturedAtUtc = AnsightClock.isoAt(capturedAtEpochMs)
        val visualTrees = captureSessionVisualTrees(state.first, state.second, capturedAtUtc)
        val result = state.second.sendSessionJpegFrame(screenshot, state.first.quality, capturedAtEpochMs)
        if (!result.success) {
            synchronized(lock) {
                sessionMessage = result.message
                stopSessionJpegCaptureLocked()
            }
        } else {
            for (visualTree in visualTrees) {
                val treeResult = state.second.sendSessionVisualTree(visualTree, capturedAtUtc)
                if (!treeResult.success) {
                    synchronized(lock) { sessionMessage = treeResult.message }
                    break
                }
            }
        }
    }

    private fun captureSessionVisualTrees(
        captureOptions: AnsightSessionJpegCaptureOptions,
        transport: PairingLiveSessionTransport,
        capturedAtUtc: String,
    ): List<JSONObject> {
        if (captureOptions.mode != AnsightSessionJpegCaptureMode.ScreenshotAndVisualTree) {
            return emptyList()
        }

        return captureRegisteredVisualTrees(transport, capturedAtUtc)
    }

    private fun startTouchVisualTreeCaptureIfNeeded() {
        synchronized(lock) {
            if (!initialized ||
                !active ||
                !sessionOpen ||
                currentLifecycleState == AppLifecycleState.Background ||
                options.touchCapture == null ||
                !touchCaptureRuntimeEnabled ||
                options.sessionJpegCapture?.mode != AnsightSessionJpegCaptureMode.ScreenshotWithVisualTreeOnTouch ||
                liveTransport?.isOpen != true ||
                touchVisualTreeCaptureCoordinator != null
            ) {
                return
            }

            touchVisualTreeCaptureCoordinator = TouchVisualTreeCaptureCoordinator(
                capture = { trigger -> captureAndSendTouchVisualTrees(trigger) },
            )
        }
    }

    private fun captureAndSendTouchVisualTrees(trigger: TouchVisualTreeCaptureTrigger) {
        val state = synchronized(lock) {
            if (!initialized ||
                !active ||
                !sessionOpen ||
                currentLifecycleState == AppLifecycleState.Background ||
                options.touchCapture == null ||
                !touchCaptureRuntimeEnabled ||
                options.sessionJpegCapture?.mode != AnsightSessionJpegCaptureMode.ScreenshotWithVisualTreeOnTouch
            ) {
                return
            }
            val transport = liveTransport?.takeIf { it.isOpen } ?: return
            transport
        }

        val capturedAtUtc = AnsightClock.isoNow()
        val visualTrees = captureRegisteredVisualTrees(state, capturedAtUtc)
        for (visualTree in visualTrees) {
            val result = state.sendSessionVisualTree(
                visualTree,
                capturedAtUtc,
                screenshotCapturedAtUtc = null,
                trigger = trigger,
            )
            if (!result.success) {
                synchronized(lock) { sessionMessage = result.message }
                return
            }
        }
    }

    private fun captureRegisteredVisualTrees(
        transport: PairingLiveSessionTransport,
        capturedAtUtc: String,
    ): List<JSONObject> = runCatching {
        val context = AndroidToolExecutionContext(
            application = application ?: return emptyList(),
            transport = transport,
            sessionId = synchronized(lock) { sessionId },
            requestId = null,
            options = synchronized(lock) { options },
        )
        SessionVisualTreeCaptureRegistry.capture(context).map {
            it.put("capturedAtUtc", capturedAtUtc)
        }
    }.getOrDefault(emptyList())

    private fun stopTouchVisualTreeCaptureLocked() {
        touchVisualTreeCaptureCoordinator?.close()
        touchVisualTreeCaptureCoordinator = null
    }

    private fun stopSessionJpegCaptureLocked(releaseUiResources: Boolean = true) {
        sessionJpegTask?.cancel(false)
        sessionJpegTask = null
        sessionJpegExecutor?.shutdownNow()
        sessionJpegExecutor = null
        if (releaseUiResources) {
            AndroidUiEvidence.releaseSessionScreenshotResources()
        }
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
            dictionary[AnsightChannels.FramesPerSecond] = framesPerSecondChannel()
        }
        dictionary[AnsightChannels.Lifecycle] = AnsightChannel(AnsightChannels.Lifecycle, "Lifecycle", "#FF9500", null, "lifecycle")
        if (options.enableBatteryLevel) {
            dictionary[AnsightChannels.BatteryLevel] = AnsightChannel(AnsightChannels.BatteryLevel, "Battery Level", "#FFCC00", "percent", "battery")
        }
        if (options.enableJniReferenceCountTracking) {
            dictionary[AnsightChannels.JniReferenceCount] = AnsightChannel(AnsightChannels.JniReferenceCount, "JNI reference count", "#AF52DE", "references", "runtime")
        }
        if (options.enableOpenFileHandleTracking) {
            dictionary[AnsightChannels.OpenFileHandles] = AnsightChannel(AnsightChannels.OpenFileHandles, "Open File Handles", "#FF3B30", "handles", "runtime")
        }
        dictionary[AnsightChannels.Unspecified] = AnsightChannel(AnsightChannels.Unspecified, "Unspecified", null, null, "unspecified")
        options.additionalChannels.forEach { dictionary[it.id] = it }
        return dictionary
    }

    private fun framesPerSecondChannel(): AnsightChannel =
        AnsightChannel(AnsightChannels.FramesPerSecond, "FPS", "#23B573", "fps", "frames")

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
            message.contains("expired", ignoreCase = true) -> PairingFailureCodes.RegistrationExpired
            message.contains("appId", ignoreCase = true) -> PairingFailureCodes.EnrollmentRequired
            error is IllegalArgumentException -> PairingFailureCodes.EnrollmentRequired
            else -> null
        }
    }

    private fun shouldClearStoredPairingProfile(reasonCode: String?): Boolean =
        reasonCode == PairingFailureCodes.EnrollmentRequired ||
            reasonCode == PairingFailureCodes.EnrollmentExpired ||
            reasonCode == PairingFailureCodes.EnrollmentConsumed ||
            reasonCode == PairingFailureCodes.AccessTokenInvalid ||
            reasonCode == PairingFailureCodes.RegistrationExpired

    private fun shouldClearCachedPairingProfile(reasonCode: String?): Boolean =
        reasonCode == PairingFailureCodes.EnrollmentRequired ||
            reasonCode == PairingFailureCodes.EnrollmentExpired ||
            reasonCode == PairingFailureCodes.EnrollmentConsumed ||
            reasonCode == PairingFailureCodes.AccessTokenInvalid ||
            reasonCode == PairingFailureCodes.RegistrationExpired ||
            reasonCode == PairingFailureCodes.UdpBootstrapFailed ||
            reasonCode == PairingFailureCodes.UdpBootstrapTimeout ||
            reasonCode == PairingFailureCodes.HostAddressRequired

    private fun loadSavedPairingConfig(app: Application): String? {
        return app.getSharedPreferences(options.hostConnection.savedConfigKey, Application.MODE_PRIVATE)
            .getString("payload", null)
            ?.trim()
            ?.ifBlank { null }
    }

    private fun clearStoredPairingProfile(app: Application) {
        app.getSharedPreferences(options.hostConnection.savedConfigKey, Application.MODE_PRIVATE)
            .edit()
            .clear()
            .apply()
    }

    private fun savePairingConfigLocked(app: Application, payload: String) {
        app.getSharedPreferences(options.hostConnection.savedConfigKey, Application.MODE_PRIVATE)
            .edit()
            .putString("payload", payload)
            .putLong("savedAtEpochMs", System.currentTimeMillis())
            .apply()
    }

    private fun loadCachedPairingProfileEntries(app: Application): List<CachedPairingProfile> {
        val preferences = app.getSharedPreferences(cachedPairingProfileKey(), Application.MODE_PRIVATE)
        val loadResult = CachedPairingProfilesCodec.load(
            profilesJson = preferences.getString("profiles", null),
            nowEpochMs = System.currentTimeMillis(),
        )
        if (loadResult.shouldRewrite) {
            writeCachedPairingProfiles(app, loadResult.profiles)
        }

        return loadResult.profiles
    }

    private fun loadCachedPairingProfiles(app: Application): List<ResolvedConnectionCandidate> {
        return loadCachedPairingProfileEntries(app).map { profile ->
            ResolvedConnectionCandidate(
                payload = profile.payload,
                source = HostConnectionSource.CachedSession,
                hostAddressOverride = profile.hostAddress,
                networkKey = profile.networkKey,
            )
        }
    }

    private fun saveCachedPairingProfile(
        app: Application,
        payload: String,
        hostAddress: String?,
        document: ParsedPairingDocument,
    ) {
        val normalizedPayload = payload.trim().ifBlank { return }
        val now = System.currentTimeMillis()
        val retentionMillis = synchronized(lock) {
            options.hostConnection.connectionProfileRetentionSeconds.coerceAtLeast(1) * 1_000L
        }
        val existing = loadCachedPairingProfileEntries(app)
        val updated = CachedPairingProfilesCodec.upsert(
            existingProfiles = existing,
            payload = normalizedPayload,
            hostAddress = hostAddress,
            document = document,
            nowEpochMs = now,
            retentionMillis = retentionMillis,
        )
        writeCachedPairingProfiles(app, updated)
        startAutoProbeIfNeeded(app)
    }

    private fun clearCachedPairingProfile(app: Application) {
        app.getSharedPreferences(cachedPairingProfileKey(), Application.MODE_PRIVATE)
            .edit()
            .clear()
            .apply()
    }

    private fun clearCachedPairingProfile(app: Application, networkKey: String?) {
        val key = networkKey?.trim()?.ifBlank { null }
        if (key == null) {
            clearCachedPairingProfile(app)
            return
        }

        writeCachedPairingProfiles(app, CachedPairingProfilesCodec.remove(loadCachedPairingProfileEntries(app), key))
    }

    private fun writeCachedPairingProfiles(app: Application, profiles: List<CachedPairingProfile>) {
        val preferences = app.getSharedPreferences(cachedPairingProfileKey(), Application.MODE_PRIVATE)
        val json = CachedPairingProfilesCodec.serialize(profiles)
        val editor = preferences.edit().clear()
        if (json != null) {
            editor.putString("profiles", json)
        }
        editor.apply()
    }

    private fun cachedPairingProfileKey(): String = "${options.hostConnection.savedConfigKey}.cached-profile"

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
        val hostAddressOverride: String? = null,
        val networkKey: String? = null,
    )

    private data class TelemetryBatch(
        val metrics: List<RecordedMetric>,
        val events: List<RecordedEvent>,
        val touches: List<RecordedTouch>,
    )
}

private class AndroidFrameRateSampler {
    private val mainHandler by lazy { Handler(Looper.getMainLooper()) }
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
        if (!running) {
            return
        }
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
