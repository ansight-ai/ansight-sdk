import Foundation

#if canImport(Darwin)
import Darwin
#endif

#if canImport(UIKit)
import UIKit
#endif

public final class AnsightRuntime: @unchecked Sendable {
    public static let shared = AnsightRuntime()

    private static let cachedProfileResetReasonCodes: Set<String> = [
        PairingFailureCodes.enrollmentRequired,
        PairingFailureCodes.enrollmentExpired,
        PairingFailureCodes.enrollmentConsumed,
        PairingFailureCodes.accessTokenInvalid,
        PairingFailureCodes.registrationExpired,
        PairingFailureCodes.udpBootstrapFailed,
        PairingFailureCodes.udpBootstrapTimeout,
        PairingFailureCodes.hostAddressRequired,
    ]

    private static let storedProfileResetReasonCodes: Set<String> = [
        PairingFailureCodes.enrollmentRequired,
        PairingFailureCodes.enrollmentExpired,
        PairingFailureCodes.enrollmentConsumed,
        PairingFailureCodes.accessTokenInvalid,
        PairingFailureCodes.registrationExpired,
    ]

    private static let screenCaptureRenderBudgetMilliseconds = 14
    private static let screenCaptureMinimumAdaptiveMaxWidth = 720

    private let lock = NSLock()
    private let pairingDocumentService = PairingConfigDocumentService()
    private var connector: any PairingSessionConnecting = PairingSessionConnector()
    private let liveTransport = PairingLiveSessionTransport()

    private var savedPairingStore: any PairingConfigStore = KeychainPairingConfigStore(account: "ai.ansight.ios.saved-pairing")
    private var cachedPairingProfileStore: any PairingConfigStore = KeychainPairingConfigStore(account: "ai.ansight.ios.saved-pairing.cached-profile")
    private var hostConnectionConfigReader: (any HostConnectionConfigReading)?
    private var options = AnsightOptions()
    private var initialized = false
    private var active = false
    private var sessionOpen = false
    private var sessionId: String?
    private var sessionMessage: String?
    private var metrics: [RecordedMetric] = []
    private var events: [RecordedEvent] = []
    private var channels: [Int: AnsightChannel] = [:]
    private var metricStreams: [Int: AnsightMetricStream] = [:]
    private var tools: [String: RegisteredTool] = [:]
    private var externalToolProtocolHandler: (@Sendable (String) throws -> String?)?
    private var externalToolProtocolResponseSentHandler: (@Sendable (String) -> Void)?
    private var artifactProviders: [String: any AnsightArtifactProvider] = [:]
    private var lastPairingDocument: ParsedPairingDocument?
    private var resolvedHostAddress: String?
    private var currentLifecycleState: AppLifecycleState = .unknown
    private var currentLifecycleChangedAtUtc: String?
    private var currentScreen: RecordedScreenView?
    private var connectionState: HostConnectionState = .disconnected
    private var hostId: String?
    private var hostName: String?
    private var lastDisconnectedAtUtc: Date?
    private var profileSequence = 0
    private var nextMetricSequence: Int64 = 0
    private var nextEventSequence: Int64 = 0
    private var lastStreamedMetricSequence: Int64 = 0
    private var lastStreamedEventSequence: Int64 = 0
    private var announcedMetricChannelIds: Set<Int> = []
    private var telemetryStreamLoopActive = false
    private var telemetryGeneration = 0
    private var telemetrySamplingTask: Task<Void, Never>?
    private var autoProbeTask: Task<Void, Never>?
    private var connectionTask: Task<HostConnectionResult, Never>?
    private var connectionTaskId: UUID?
    private var screenCaptureTask: Task<Void, Never>?
    private var hostSessionJpegCapturePolicy = HostSessionJpegCapturePolicy.app
    private let lifecycleObserver = AnsightLifecycleObserver()
    private var frameRateSampler: AnsightFrameRateSampler?
    private var frameRateTrackingEnabled = false
    private var touchCaptureSession: AnsightTouchCaptureSession?
    private var touchCaptureStreamer: AnsightTouchCaptureStreamer?
    private var touchVisualTreeCaptureCoordinator: AnsightTouchVisualTreeCaptureCoordinator?
    private var screenCaptureGeneration = 0
    private var screenFramesCaptured = 0
    private var screenFramesSent = 0
    private var lastScreenCaptureMessage: String?
    private var lastScreenCaptureRenderMilliseconds: Int?
    private var lastScreenCaptureEncodeMilliseconds: Int?
    private var lastScreenCaptureSendMilliseconds: Int?
    private var lastScreenCaptureTotalMilliseconds: Int?
    private var lastFrameRate: Int?
    private var touchCaptureRuntimeEnabled = true
    private var touchCaptureGuard: (@Sendable () -> Bool)?
    private var touchesCaptured = 0
    private var touchesSent = 0
    private var lastTouchCaptureMessage: String?
    private var pendingBinaryTransfers: [String: AnsightPendingBinaryTransfer] = [:]
    private var hostConnectionStatusListeners: [UUID: HostConnectionStatusListener] = [:]
    private var lastPublishedHostConnectionStatus: HostConnectionStatus?
    private var lastPublishedHostConnectionCapabilities: HostConnectionCapabilities?

    private init() {}

    public func initialize(options: AnsightOptions = .init()) throws {
        AnsightLogger.info("Initializing Ansight runtime.")
        lock.withLock {
            telemetryGeneration += 1
            active = false
            sessionOpen = false
            connectionState = .disconnected
            lastDisconnectedAtUtc = nil
        }
        telemetrySamplingTask?.cancel()
        telemetrySamplingTask = nil
        autoProbeTask?.cancel()
        autoProbeTask = nil
        connectionTask?.cancel()
        connectionTask = nil
        connectionTaskId = nil
        stopLifecycleCapture()
        stopScreenCapture()
        stopTouchVisualTreeCapture()
        stopFrameRateSampling()
        stopTouchCapture(message: "Touch capture stopped.")
        let validatedOptions = try options.validated()
        AnsightCrashCapture.shared.initialize(options: validatedOptions.crashCapture)

        lock.withLock {
            self.options = validatedOptions
            self.savedPairingStore = KeychainPairingConfigStore(account: validatedOptions.hostConnection.savedConfigKey)
            self.cachedPairingProfileStore = KeychainPairingConfigStore(
                account: Self.cachedPairingProfileKey(for: validatedOptions.hostConnection.savedConfigKey)
            )
            self.channels = Self.makeChannelDictionary(options: validatedOptions)
            metricStreams.removeAll()
            metrics.removeAll()
            events.removeAll()
            nextMetricSequence = 0
            nextEventSequence = 0
            initialized = true
            active = false
            sessionOpen = false
            sessionId = nil
            lastStreamedMetricSequence = 0
            lastStreamedEventSequence = 0
            announcedMetricChannelIds = []
            telemetryStreamLoopActive = false
            hostSessionJpegCapturePolicy = .app
            screenFramesCaptured = 0
            screenFramesSent = 0
            lastScreenCaptureMessage = nil
            lastScreenCaptureRenderMilliseconds = nil
            lastScreenCaptureEncodeMilliseconds = nil
            lastScreenCaptureSendMilliseconds = nil
            lastScreenCaptureTotalMilliseconds = nil
            lastFrameRate = nil
            frameRateTrackingEnabled = validatedOptions.enableFramesPerSecond
            touchCaptureRuntimeEnabled = validatedOptions.touchCapture != nil
            touchCaptureGuard = nil
            touchesCaptured = 0
            touchesSent = 0
            lastTouchCaptureMessage = nil
            pendingBinaryTransfers.removeAll()
            externalToolProtocolHandler = nil
            externalToolProtocolResponseSentHandler = nil
            lastPairingDocument = nil
            currentScreen = nil
            currentLifecycleState = .unknown
            currentLifecycleChangedAtUtc = nil
            resolvedHostAddress = nil
            hostId = nil
            hostName = nil
            connectionState = .disconnected
            lastDisconnectedAtUtc = nil
            sessionMessage = "Runtime initialized."
        }
        publishHostConnectionStatusIfChanged(force: true)
        AnsightLogger.info("Ansight runtime initialized.")
    }

    public func initializeAndActivate(options: AnsightOptions = .init()) throws {
        try initialize(options: options)
        try activate()
    }

    public func activate() throws {
        let activation = try lock.withLock {
            guard initialized else {
                throw RuntimeError.notInitialized("AnsightRuntime must be initialized before activation.")
            }

            guard !active else {
                return RuntimeActivationWork(
                    shouldStartAutoProbe: false
                )
            }

            active = true
            sessionMessage = "Runtime activated."
            return RuntimeActivationWork(
                shouldStartAutoProbe: options.hostAutoProbe.enabled
            )
        }

        startTelemetrySamplingIfNeeded()
        startLifecycleCaptureIfNeeded()
        startFrameRateSamplingIfNeeded()
        startTouchCaptureIfNeeded()
        if activation.shouldStartAutoProbe {
            startAutoProbeIfNeeded()
        }
        publishHostConnectionStatusIfChanged()
        AnsightLogger.info("Ansight runtime activated.")
    }

    public func deactivate() {
        stopLifecycleCapture()
        stopScreenCapture(message: "Screen capture stopped.")
        stopTouchVisualTreeCapture()
        stopFrameRateSampling()
        stopTouchCapture(message: "Touch capture stopped.")
        lock.withLock {
            telemetryGeneration += 1
        }
        telemetrySamplingTask?.cancel()
        telemetrySamplingTask = nil
        autoProbeTask?.cancel()
        autoProbeTask = nil
        connectionTask?.cancel()
        connectionTask = nil
        connectionTaskId = nil

        lock.withLock {
            active = false
            sessionOpen = false
            sessionId = nil
            lastStreamedMetricSequence = 0
            lastStreamedEventSequence = 0
            announcedMetricChannelIds = []
            telemetryStreamLoopActive = false
            connectionState = .disconnected
            resolvedHostAddress = nil
            hostId = nil
            hostName = nil
            lastDisconnectedAtUtc = Date()
            pendingBinaryTransfers.removeAll()
            sessionMessage = "Runtime deactivated."
        }
        publishHostConnectionStatusIfChanged()

        Task {
            await liveTransport.close(notify: false)
        }
        AnsightLogger.info("Ansight runtime deactivated.")
    }

    public func clear() {
        lock.withLock {
            metrics.removeAll()
            events.removeAll()
            screenFramesCaptured = 0
            screenFramesSent = 0
            lastScreenCaptureMessage = nil
            lastScreenCaptureRenderMilliseconds = nil
            lastScreenCaptureEncodeMilliseconds = nil
            lastScreenCaptureSendMilliseconds = nil
            lastScreenCaptureTotalMilliseconds = nil
            lastFrameRate = nil
            touchesCaptured = 0
            touchesSent = 0
            lastTouchCaptureMessage = nil
            lastPairingDocument = nil
            currentScreen = nil
            currentLifecycleState = .unknown
            currentLifecycleChangedAtUtc = nil
            nextMetricSequence = 0
            nextEventSequence = 0
            lastStreamedMetricSequence = 0
            lastStreamedEventSequence = 0
            announcedMetricChannelIds = []
            telemetryStreamLoopActive = false
            hostSessionJpegCapturePolicy = .app
            resolvedHostAddress = nil
            pendingBinaryTransfers.removeAll()
            sessionMessage = "Runtime buffers cleared."
        }
        publishHostConnectionStatusIfChanged()
    }

    public func clearSavedPairing() {
        savedPairingStore.clear()
        lock.withLock {
            sessionMessage = "Saved Studio registration cleared."
        }
        publishHostConnectionStatusIfChanged()
    }

    public func clearCachedSession() {
        cachedPairingProfileStore.clear()
        lock.withLock {
            sessionMessage = "Cached pairing session cleared."
        }
        publishHostConnectionStatusIfChanged()
    }

    public func savePairingConfig(_ pairingJson: String, expectedAppId: String? = nil) -> HostConnectionResult {
        let isInitialized = lock.withLock { initialized }
        guard isInitialized else {
            return HostConnectionResult(
                success: false,
                message: "AnsightRuntime must be initialized before saving a Studio registration.",
                kind: .savedConfig,
                source: .savedConfig
            )
        }

        let trimmedJson = pairingJson.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmedJson.isEmpty else {
            return HostConnectionResult(
                success: false,
                message: "An enrollment invite is required.",
                kind: .savedConfig,
                source: .savedConfig
            )
        }

        let normalizedExpectedAppId = expectedAppId?.trimmingCharacters(in: .whitespacesAndNewlines)
        let bundleId = Bundle.main.bundleIdentifier?.trimmingCharacters(in: .whitespacesAndNewlines)
        let effectiveExpectedAppId: String?
        if let normalizedExpectedAppId, !normalizedExpectedAppId.isEmpty {
            effectiveExpectedAppId = normalizedExpectedAppId
        } else if let bundleId, !bundleId.isEmpty {
            effectiveExpectedAppId = bundleId
        } else {
            effectiveExpectedAppId = nil
        }

        do {
            _ = try pairingDocumentService.parseAndValidateDocument(trimmedJson, expectedAppId: effectiveExpectedAppId)
            try savedPairingStore.save(trimmedJson)
            lock.withLock {
                sessionMessage = "Saved Studio registration."
            }
            publishHostConnectionStatusIfChanged()
            return HostConnectionResult(
                success: true,
                message: "Saved Studio registration.",
                kind: .savedConfig,
                source: .savedConfig
            )
        } catch {
            return HostConnectionResult(
                success: false,
                message: error.localizedDescription,
                kind: .savedConfig,
                source: .savedConfig,
                reasonCode: reasonCode(for: error)
            )
        }
    }

    public func setHostConnectionConfigReader(_ reader: (any HostConnectionConfigReading)?) {
        lock.withLock {
            hostConnectionConfigReader = reader
        }
        publishHostConnectionStatusIfChanged(force: true)
    }

    func replacePairingStoresForTesting(saved: any PairingConfigStore, cached: any PairingConfigStore) {
        lock.withLock {
            savedPairingStore = saved
            cachedPairingProfileStore = cached
        }
    }

    func replaceConnectorForTesting(_ connector: any PairingSessionConnecting) {
        lock.withLock {
            self.connector = connector
        }
    }

    func resolveConnectionRequestForTesting(_ request: HostConnectionRequest) throws -> ResolvedConnectionRequest {
        try resolveConnectionRequest(request)
    }

    func resolveConnectionRequestsForTesting(_ request: HostConnectionRequest) throws -> [ResolvedConnectionRequest] {
        try resolveConnectionRequests(request)
    }

    func saveCachedPairingProfileForTesting(_ document: PairingConfigDocument) {
        saveCachedPairingProfile(document)
    }

    public var isFramesPerSecondEnabled: Bool {
        lock.withLock { frameRateTrackingEnabled }
    }

    public func enableFramesPerSecond() {
        let shouldStart = lock.withLock { () -> Bool in
            frameRateTrackingEnabled = true
            channels[AnsightChannels.framesPerSecond] = AnsightChannels.framesPerSecondChannel
            sessionMessage = "Frames-per-second sampling enabled."
            return active
        }

        if shouldStart {
            startFrameRateSamplingIfNeeded()
        }
    }

    public func disableFramesPerSecond() {
        lock.withLock {
            frameRateTrackingEnabled = false
            sessionMessage = "Frames-per-second sampling disabled."
        }
        stopFrameRateSampling()
    }

    public var isTouchCaptureEnabled: Bool {
        lock.withLock {
            initialized && options.touchCapture != nil && touchCaptureRuntimeEnabled
        }
    }

    public func setTouchCaptureGuard(_ guardCallback: (@Sendable () -> Bool)?) {
        lock.withLock {
            touchCaptureGuard = guardCallback
            sessionMessage = guardCallback == nil ? "Touch capture guard cleared." : "Touch capture guard configured."
        }
    }

    public func enableTouchCapture() {
        let canEnable = lock.withLock { () -> Bool in
            guard options.touchCapture != nil else {
                lastTouchCaptureMessage = "Touch capture is not configured."
                sessionMessage = "Touch capture is not configured."
                return false
            }

            touchCaptureRuntimeEnabled = true
            lastTouchCaptureMessage = "Touch capture enabled."
            sessionMessage = "Touch capture enabled."
            return true
        }

        guard canEnable else {
            return
        }

        startTouchCaptureIfNeeded()
        if liveTransport.isOpen {
            _ = startTouchCaptureStreamingIfNeeded()
            startTouchVisualTreeCaptureIfNeeded()
        }
    }

    public func disableTouchCapture() {
        lock.withLock {
            touchCaptureRuntimeEnabled = false
        }
        stopTouchVisualTreeCapture()
        stopTouchCapture(message: "Touch capture disabled.")
    }

    public func setScreenRouteResolver(_ resolver: AnsightScreenRouteResolver?) {
        lifecycleObserver.setScreenRouteResolver(resolver)
        lock.withLock {
            sessionMessage = resolver == nil ? "Screen route resolver cleared." : "Screen route resolver configured."
        }
    }

    public func captureScreenFrame(options overrideOptions: AnsightSessionJpegCaptureOptions? = nil) async -> OperationResult {
        var captureOptions = overrideOptions ?? lock.withLock { options.sessionJpegCapture } ?? AnsightSessionJpegCaptureOptions()
        captureOptions.validate()

        return await captureAndSendScreenFrame(options: captureOptions).operationResult
    }

    private func captureAndSendScreenFrame(options captureOptions: AnsightSessionJpegCaptureOptions) async -> ScreenCaptureSendResult {
        let preparation = await prepareScreenFrame(options: captureOptions)
        guard let preparedFrame = preparation.preparedFrame else {
            return ScreenCaptureSendResult(
                operationResult: preparation.operationResult,
                renderMilliseconds: preparation.renderMilliseconds,
                frameWidth: preparation.frameWidth
            )
        }

        return await sendPreparedScreenFrame(preparedFrame)
    }

    private func prepareScreenFrame(options captureOptions: AnsightSessionJpegCaptureOptions) async -> ScreenCapturePreparationResult {
        guard lock.withLock({ initialized && active && sessionOpen && connectionState == .connected }),
              liveTransport.isOpen
        else {
            let message = "A connected live session is required before capturing a screen frame."
            lock.withLock {
                lastScreenCaptureMessage = message
            }
            return ScreenCapturePreparationResult(operationResult: .failure(message))
        }

        do {
            let captureStarted = AnsightTiming.now()
            let capture = try await AnsightScreenCapture.capture(options: captureOptions)
            let frame = capture.frame
            let payload = SessionJpegWireProtocol.encode(frame)
            let visualTrees = captureOptions.mode == .screenshotAndVisualTree
                ? AnsightSessionVisualTreeCaptureRegistry.capture()
                : []
            let readyAt = AnsightTiming.now()
            let totalMilliseconds = AnsightTiming.elapsedMilliseconds(since: captureStarted)
            let message = "Captured screen frame \(frame.width)x\(frame.height) (\(frame.jpegData.count) bytes, render \(capture.renderMilliseconds) ms, encode \(capture.encodeMilliseconds) ms); queued for delivery."

            lock.withLock {
                screenFramesCaptured += 1
                lastScreenCaptureMessage = message
                lastScreenCaptureRenderMilliseconds = capture.renderMilliseconds
                lastScreenCaptureEncodeMilliseconds = capture.encodeMilliseconds
                lastScreenCaptureSendMilliseconds = nil
                lastScreenCaptureTotalMilliseconds = totalMilliseconds
                sessionMessage = message
            }

            return ScreenCapturePreparationResult(
                operationResult: .success(message),
                preparedFrame: PreparedScreenFrame(
                    frame: frame,
                    payload: payload,
                    captureStarted: captureStarted,
                    readyAt: readyAt,
                    renderMilliseconds: capture.renderMilliseconds,
                    encodeMilliseconds: capture.encodeMilliseconds,
                    visualTrees: visualTrees
                ),
                renderMilliseconds: capture.renderMilliseconds,
                frameWidth: frame.width
            )
        } catch {
            let message = "Failed to capture screen frame: \(error.localizedDescription)"
            lock.withLock {
                lastScreenCaptureMessage = message
                lastScreenCaptureRenderMilliseconds = nil
                lastScreenCaptureEncodeMilliseconds = nil
                lastScreenCaptureSendMilliseconds = nil
                lastScreenCaptureTotalMilliseconds = nil
                sessionMessage = message
            }
            return ScreenCapturePreparationResult(operationResult: .failure(message))
        }
    }

    private func sendPreparedScreenFrame(_ preparedFrame: PreparedScreenFrame) async -> ScreenCaptureSendResult {
        let queueMilliseconds = AnsightTiming.elapsedMilliseconds(since: preparedFrame.readyAt)
        let sendStarted = AnsightTiming.now()
        var result = await liveTransport.sendData(preparedFrame.payload)
        if result.success {
            for visualTree in preparedFrame.visualTrees {
                do {
                    let event = Self.sessionVisualTreeEvent(
                        payload: visualTree,
                        capturedAtUtc: preparedFrame.frame.capturedAtUtc,
                        screenshotCapturedAtUtc: preparedFrame.frame.capturedAtUtc,
                        trigger: nil
                    )
                    result = await liveTransport.sendText(try event.jsonString())
                    if !result.success {
                        break
                    }
                } catch {
                    result = .failure("Failed to encode the captured visual tree: \(error.localizedDescription)")
                    break
                }
            }
        }
        let sendMilliseconds = AnsightTiming.elapsedMilliseconds(since: sendStarted)
        let totalMilliseconds = AnsightTiming.elapsedMilliseconds(since: preparedFrame.captureStarted)
        let frame = preparedFrame.frame
        let message = result.success
            ? "Captured and sent screen frame \(frame.width)x\(frame.height) (\(frame.jpegData.count) bytes, render \(preparedFrame.renderMilliseconds) ms, encode \(preparedFrame.encodeMilliseconds) ms, queued \(queueMilliseconds) ms, send \(sendMilliseconds) ms)."
            : result.message

        lock.withLock {
            if result.success {
                screenFramesSent += 1
            }
            lastScreenCaptureMessage = message
            lastScreenCaptureRenderMilliseconds = preparedFrame.renderMilliseconds
            lastScreenCaptureEncodeMilliseconds = preparedFrame.encodeMilliseconds
            lastScreenCaptureSendMilliseconds = sendMilliseconds
            lastScreenCaptureTotalMilliseconds = totalMilliseconds
            sessionMessage = message
        }

        return ScreenCaptureSendResult(
            operationResult: OperationResult(success: result.success, message: message),
            renderMilliseconds: preparedFrame.renderMilliseconds,
            frameWidth: frame.width
        )
    }

    public func registerMetricChannel(_ channel: AnsightChannel) throws {
        let validated = try Self.validatedMetricChannel(channel, allowReservedIds: false, allowUnspecified: false)

        lock.withLock {
            channels[validated.id] = validated
            announcedMetricChannelIds.remove(validated.id)
            sessionMessage = "Registered metric channel \(validated.id)."
        }
    }

    public func registerMetricStream(_ stream: AnsightMetricStream) throws {
        let validated = try Self.validatedMetricChannel(stream.channel, allowReservedIds: false, allowUnspecified: false)
        let validatedStream = AnsightMetricStream(channel: validated) {
            stream.sample()
        }

        lock.withLock {
            channels[validated.id] = validated
            metricStreams[validated.id] = validatedStream
            announcedMetricChannelIds.remove(validated.id)
            sessionMessage = "Registered metric stream \(validated.id)."
        }
    }

    public func metric(_ value: Int64, channel: Int = AnsightChannels.unspecified) throws {
        try lock.withLock {
            guard initialized else {
                throw RuntimeError.notInitialized("AnsightRuntime must be initialized before recording metrics.")
            }

            guard (0...255).contains(channel) else {
                throw RuntimeError.invalidInput("Channel ids must be between 0 and 255.")
            }

            guard channels[channel] != nil else {
                sessionMessage = "Ignored metric for unknown channel \(channel)."
                return
            }

            recordMetricLocked(value: value, channel: try validateChannel(channel))
            sessionMessage = "Recorded metric \(value)."
        }

        streamPendingTelemetry()
    }

    public func event(
        _ label: String,
        type: AnsightEventType = .info,
        details: String? = nil,
        channel: Int = AnsightChannels.unspecified,
        id: String = UUID().uuidString,
        externalId: String? = nil
    ) throws {
        let trimmedLabel = label.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmedLabel.isEmpty else {
            throw RuntimeError.invalidInput("Event label must not be blank.")
        }

        try lock.withLock {
            guard initialized else {
                throw RuntimeError.notInitialized("AnsightRuntime must be initialized before recording events.")
            }

            nextEventSequence += 1
            events.append(
                RecordedEvent(
                    id: id,
                    label: trimmedLabel,
                    type: type,
                    details: details?.trimmingCharacters(in: .whitespacesAndNewlines),
                    channel: try validateChannel(channel),
                    externalId: externalId,
                    sequence: nextEventSequence
                )
            )
            trimEventsLocked()
            sessionMessage = "Recorded event \(trimmedLabel)."
        }

        AnsightCrashCapture.shared.recordBreadcrumb(kind: "event", label: trimmedLabel, details: details)
        streamPendingTelemetry()
    }

    @discardableResult
    public func recordCrashCandidate(
        runtime: String,
        kind: String = "unhandled_exception",
        message: String? = nil,
        stack: String? = nil,
        fatal: Bool = true,
        metadata: [String: String] = [:]
    ) -> String? {
        AnsightCrashCapture.shared.recordCandidate(
            runtime: runtime,
            kind: kind,
            message: message,
            stack: stack,
            fatal: fatal,
            metadata: metadata
        )
    }

    public var processSessionId: String { AnsightCrashCapture.shared.processSessionId }

    public func pendingCrashReportsJSON() -> String { AnsightCrashCapture.shared.pendingReportsJSON() }

    public func associateOfflineCaptureSession(_ sessionId: String, directory: String? = nil) {
        AnsightCrashCapture.shared.associateOfflineSession(sessionId: sessionId, directory: directory)
    }

    public func completeOfflineCaptureSession(_ sessionId: String) {
        AnsightCrashCapture.shared.markOfflineSessionCompleted(sessionId: sessionId)
    }

    @discardableResult
    public func markCrashReportPersistedToOfflineCapture(_ reportId: String) -> Bool {
        AnsightCrashCapture.shared.markOfflineReportPersisted(reportId: reportId)
    }

    public func screenViewed(
        _ name: String,
        details: [String: String] = [:],
        channel: Int = AnsightChannels.lifecycle
    ) throws {
        let trimmedName = name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmedName.isEmpty else {
            throw RuntimeError.invalidInput("Screen name must not be blank.")
        }

        let capturedAtUtc = AnsightClock.isoNow()
        let detailsJson = try? JSONValue.object(from: details).jsonString()
        try lock.withLock {
            guard initialized else {
                throw RuntimeError.notInitialized("AnsightRuntime must be initialized before recording screen views.")
            }

            currentScreen = RecordedScreenView(name: trimmedName, details: details, capturedAtUtc: capturedAtUtc)
            nextEventSequence += 1
            events.append(
                RecordedEvent(
                    label: trimmedName,
                    type: .screenViewed,
                    details: detailsJson,
                    channel: try validateChannel(channel),
                    capturedAtUtc: capturedAtUtc,
                    sequence: nextEventSequence
                )
            )
            trimEventsLocked()
            sessionMessage = "Recorded screen view \(trimmedName)."
        }

        AnsightCrashCapture.shared.recordBreadcrumb(kind: "screen", label: trimmedName, details: detailsJson)
        streamPendingTelemetry()
    }

    public func setAppLifecycleState(_ state: AppLifecycleState, changedAtUtc: String = AnsightClock.isoNow()) {
        let lifecycleChange = lock.withLock { () -> RuntimeLifecycleChange in
            guard initialized, currentLifecycleState != state else {
                return .unchanged
            }

            currentLifecycleState = state
            currentLifecycleChangedAtUtc = changedAtUtc
            nextEventSequence += 1
            events.append(
                RecordedEvent(
                    label: "lifecycle.\(state.rawValue)",
                    type: .lifecycle,
                    details: nil,
                    channel: AnsightChannels.lifecycle,
                    capturedAtUtc: changedAtUtc,
                    sequence: nextEventSequence
                )
            )
            trimEventsLocked()
            sessionMessage = "Lifecycle state changed to \(state.rawValue)."
            return RuntimeLifecycleChange(didChange: true, shouldSendAppState: sessionOpen)
        }

        guard lifecycleChange.didChange else {
            return
        }

        AnsightCrashCapture.shared.recordBreadcrumb(kind: "lifecycle", label: state.rawValue)
        streamPendingTelemetry()

        if lifecycleChange.shouldSendAppState {
            Task { [weak self] in
                await self?.sendCurrentAppState()
            }
        }

        switch state {
        case .foreground:
            recoverLiveSessionAfterForeground()
        case .background:
            pauseLiveSessionPipelinesForBackground()
        case .unknown:
            break
        }
    }

    func recordFrameRateSample(_ framesPerSecond: Int) {
        let normalized = max(0, min(framesPerSecond, 1_000))
        lock.withLock {
            guard initialized, active, frameRateTrackingEnabled else {
                return
            }

            lastFrameRate = normalized
        }

        try? metric(Int64(normalized), channel: AnsightChannels.framesPerSecond)
    }

    func recordCapturedTouch(_ touch: AnsightCapturedTouch) {
        let guardCallback = lock.withLock { touchCaptureGuard }
        if let guardCallback, !guardCallback() {
            lock.withLock { touchVisualTreeCaptureCoordinator }?.interruptGesture()
            return
        }

        let captureTargets = lock.withLock {
            () -> (AnsightTouchCaptureStreamer?, AnsightTouchVisualTreeCaptureCoordinator?) in
            guard initialized,
                  active,
                  touchCaptureRuntimeEnabled,
                  options.touchCapture != nil
            else {
                return (nil, nil)
            }

            touchesCaptured += 1
            lastTouchCaptureMessage = "Captured touch input."
            return (touchCaptureStreamer, touchVisualTreeCaptureCoordinator)
        }

        captureTargets.0?.record(touch)
        captureTargets.1?.observe(touch)
    }

    public func connect(_ request: HostConnectionRequest = .auto()) async -> HostConnectionResult {
        let connectionTask = lock.withLock { () -> RuntimeConnectionTask in
            if let existingTask = self.connectionTask,
               let existingTaskId = self.connectionTaskId {
                return RuntimeConnectionTask(id: existingTaskId, task: existingTask, created: false)
            }

            let taskId = UUID()
            let task = Task { [weak self] in
                guard let self else {
                    return HostConnectionResult(
                        success: false,
                        message: "Ansight runtime is no longer available.",
                        kind: request.kind,
                        source: .hostConnection,
                        reasonCode: nil
                    )
                }

                return await self.connectCore(request)
            }
            self.connectionTaskId = taskId
            self.connectionTask = task
            return RuntimeConnectionTask(id: taskId, task: task, created: true)
        }

        let result = await connectionTask.task.value
        if connectionTask.created {
            lock.withLock {
                if connectionTaskId == connectionTask.id {
                    connectionTaskId = nil
                    self.connectionTask = nil
                }
            }
        }

        return result
    }

    private func connectCore(_ request: HostConnectionRequest) async -> HostConnectionResult {
        let resolvedRequests: [ResolvedConnectionRequest]
        do {
            resolvedRequests = try await resolveConnectionRequestsForConnect(request)
        } catch {
            AnsightLogger.warning(error.localizedDescription, error: error)
            return HostConnectionResult(
                success: false,
                message: error.localizedDescription,
                kind: request.kind,
                source: source(for: request.kind),
                reasonCode: reasonCode(for: error)
            )
        }

        let clientName = resolveClientName(request.clientName)
        var lastResult: HostConnectionResult?
        for resolvedRequest in resolvedRequests {
            let result = await connectResolvedRequest(
                resolvedRequest,
                originalRequest: request,
                clientName: clientName
            )
            if result.success {
                AnsightLogger.info("Connected to Ansight host.")
                return result
            }

            if request.kind == .auto,
               resolvedRequest.document.config.configId.hasPrefix(
                   PairingEnrollmentModes.localConfigPrefix
               ) {
                AnsightLogger.debug(result.message)
            } else {
                AnsightLogger.warning(result.message)
            }
            lastResult = result
            cleanUpFailedAutoConnectionCandidate(resolvedRequest, result: result)
            guard shouldTryNextAutoConnectionCandidate(
                after: result,
                resolvedRequest: resolvedRequest,
                originalRequest: request
            ) else {
                return result
            }
        }

        return lastResult ?? HostConnectionResult(
            success: false,
            message: "No Studio registration is available. Scan an enrollment QR in Ansight Studio.",
            kind: request.kind,
            source: source(for: request.kind),
            reasonCode: nil
        )
    }

    private func connectResolvedRequest(
        _ resolvedRequest: ResolvedConnectionRequest,
        originalRequest request: HostConnectionRequest,
        clientName: String
    ) async -> HostConnectionResult {
        let expectedAppId = requestExpectedAppId(for: resolvedRequest.document, request: request)
        do {
            try pairingDocumentService.validateDocument(resolvedRequest.document, expectedAppId: expectedAppId)
            try validatePinnedHostIdentity(for: resolvedRequest.document, source: resolvedRequest.source)
        } catch {
            return HostConnectionResult(
                success: false,
                message: error.localizedDescription,
                kind: request.kind,
                source: resolvedRequest.source,
                reasonCode: reasonCode(for: error)
            )
        }

        lock.withLock {
            connectionState = .connecting
            sessionMessage = "Connecting to Ansight host."
        }
        publishHostConnectionStatusIfChanged()

        let connectionOptions = PairingConnectionOptions(
            hostAddressOverride: request.hostAddressOverride,
            discoveryPort: options.hostConnection.discoveryPort,
            allowCellularConnections: options.hostConnection.allowCellularConnections,
            deviceAppProfile: nextDeviceProfile(),
            customProperties: options.customProperties
        )
        let connector = lock.withLock { self.connector }
        let attempt = await connector.connect(
            document: resolvedRequest.document,
            clientName: clientName,
            options: connectionOptions
        )

        guard attempt.success,
              let connectResponse = attempt.connectResponse,
              attempt.webSocketURL != nil || attempt.authenticatedWebSocket != nil
        else {
            lock.withLock {
                connectionState = .disconnected
                sessionOpen = false
                sessionId = nil
                lastStreamedMetricSequence = 0
                lastStreamedEventSequence = 0
                sessionMessage = attempt.message
            }
            publishHostConnectionStatusIfChanged()

            let open = OpenSessionResult(
                success: false,
                accepted: attempt.accepted,
                message: attempt.message,
                sessionId: nil,
                configId: resolvedRequest.document.config.configId,
                appId: resolvedRequest.document.config.appId,
                resolvedHostAddress: attempt.hostAddress,
                discoverySource: resolvedRequest.document.discoveryHint?.source,
                reasonCode: attempt.failureCode ?? attempt.connectResponse?.reason,
                hostId: attempt.connectResponse?.hostId,
                hostName: attempt.connectResponse?.hostName
            )
            return HostConnectionResult(
                success: false,
                message: attempt.message,
                kind: request.kind,
                source: resolvedRequest.source,
                reasonCode: open.reasonCode,
                openSession: open
            )
        }

        let sessionOpenAttempt: LiveSessionOpenAttempt
        if let authenticatedWebSocket = attempt.authenticatedWebSocket {
            sessionOpenAttempt = await openLiveTransportSession(
                authenticatedSocket: authenticatedWebSocket,
                config: resolvedRequest.document.config,
                clientName: clientName
            )
        } else {
            sessionOpenAttempt = await openLiveTransportSession(
                url: attempt.webSocketURL!,
                config: resolvedRequest.document.config,
                clientName: clientName
            )
        }
        guard sessionOpenAttempt.result.success else {
            lock.withLock {
                connectionState = .disconnected
                sessionOpen = false
                sessionId = nil
                lastStreamedMetricSequence = 0
                lastStreamedEventSequence = 0
                sessionMessage = sessionOpenAttempt.result.message
            }
            publishHostConnectionStatusIfChanged()
            return HostConnectionResult(
                success: false,
                message: sessionOpenAttempt.result.message,
                kind: request.kind,
                source: .transport,
                reasonCode: sessionOpenAttempt.reasonCode
            )
        }

        let profile = connectionOptions.deviceAppProfile ?? nextDeviceProfile()
        let profileResult = await sendDeviceProfile(profile)
        if !profileResult.success {
            await liveTransport.close(notify: false)
            lock.withLock {
                connectionState = .disconnected
                sessionOpen = false
                sessionId = nil
                lastStreamedMetricSequence = 0
                lastStreamedEventSequence = 0
                sessionMessage = profileResult.message
            }
            publishHostConnectionStatusIfChanged()
            return HostConnectionResult(
                success: false,
                message: profileResult.message,
                kind: request.kind,
                source: .hostConnection,
                reasonCode: PairingFailureCodes.webSocketHandshakeFailed
            )
        }

        lock.withLock {
            lastStreamedMetricSequence = 0
            lastStreamedEventSequence = 0
            announcedMetricChannelIds = []
            telemetryStreamLoopActive = false
        }
        await sendCurrentAppState()
        _ = await sendMetricChannelDefinitions()
        let touchCaptureResult = startTouchCaptureStreamingIfNeeded()
        guard touchCaptureResult.success else {
            await liveTransport.close(notify: false)
            lock.withLock {
                connectionState = .disconnected
                sessionOpen = false
                sessionId = nil
                lastStreamedMetricSequence = 0
                lastStreamedEventSequence = 0
                sessionMessage = touchCaptureResult.message
            }
            publishHostConnectionStatusIfChanged()
            return HostConnectionResult(
                success: false,
                message: touchCaptureResult.message,
                kind: request.kind,
                source: .touchCapture,
                reasonCode: PairingFailureCodes.webSocketHandshakeFailed
            )
        }
        let newSessionId = ProcessSessionIdentity.current
        lock.withLock {
            connectionState = .connected
            sessionOpen = true
            sessionId = newSessionId
            lastPairingDocument = resolvedRequest.document
            resolvedHostAddress = attempt.hostAddress
            hostId = connectResponse.hostId
            hostName = connectResponse.hostName
            lastDisconnectedAtUtc = nil
            sessionMessage = attempt.message
        }
        AnsightCrashCapture.shared.associateStudioSession(
            hostId: connectResponse.hostId,
            configId: resolvedRequest.document.config.configId,
            appId: resolvedRequest.document.config.appId
        )
        publishHostConnectionStatusIfChanged()
        streamPendingTelemetry()
        await AnsightCrashCapture.shared.deliverPendingReports(using: liveTransport)

        startScreenCaptureIfNeeded()
        startTouchVisualTreeCaptureIfNeeded()
        if !resolvedRequest.document.config.configId.hasPrefix(
            PairingEnrollmentModes.localConfigPrefix
        ) {
            savePairingDocument(
                resolvedRequest.document,
                connectedHostAddress: attempt.hostAddress,
                connectResponse: connectResponse
            )
        }
        if request.kind == .auto, resolvedRequest.source == .payload {
            AnsightUnattendedProvisioning.clearPayloadFromEnvironment()
        }

        let open = OpenSessionResult(
            success: true,
            accepted: true,
            message: attempt.message,
            sessionId: newSessionId,
            configId: resolvedRequest.document.config.configId,
            appId: resolvedRequest.document.config.appId,
            resolvedHostAddress: attempt.hostAddress,
            discoverySource: resolvedRequest.document.discoveryHint?.source,
            reasonCode: connectResponse.reason,
            hostId: connectResponse.hostId,
            hostName: connectResponse.hostName
        )

        return HostConnectionResult(
            success: true,
            message: attempt.message,
            kind: request.kind,
            source: resolvedRequest.source,
            reasonCode: connectResponse.reason,
            openSession: open
        )
    }

    private func connectCachedPairingProfileForAutoProbe(clientName requestedClientName: String?) async -> HostConnectionResult {
        let request = HostConnectionRequest.auto(
            clientName: requestedClientName,
            sourceDescription: "auto-probe"
        )
        guard let resolvedRequest = cachedPairingProfile() else {
            return HostConnectionResult(
                success: false,
                message: "No cached Ansight host session is available.",
                kind: request.kind,
                source: .cachedSession,
                reasonCode: nil
            )
        }

        let result = await connectResolvedRequest(
            resolvedRequest,
            originalRequest: request,
            clientName: resolveClientName(request.clientName)
        )
        if !result.success {
            cleanUpFailedAutoConnectionCandidate(resolvedRequest, result: result)
        }
        return result
    }

    private func cleanUpFailedAutoConnectionCandidate(
        _ resolvedRequest: ResolvedConnectionRequest,
        result: HostConnectionResult
    ) {
        guard result.kind == .auto, !result.success else {
            return
        }

        switch resolvedRequest.source {
        case .cachedSession:
            if shouldClearCachedPairingProfile(reasonCode: result.reasonCode) {
                clearCachedPairingProfile(for: resolvedRequest)
            }
        case .savedConfig:
            if shouldClearStoredPairingProfile(reasonCode: result.reasonCode) {
                savedPairingStore.clear()
            }
        default:
            return
        }
    }

    private func shouldTryNextAutoConnectionCandidate(
        after result: HostConnectionResult,
        resolvedRequest: ResolvedConnectionRequest,
        originalRequest: HostConnectionRequest
    ) -> Bool {
        guard originalRequest.kind == .auto, !result.success else {
            return false
        }

        switch resolvedRequest.source {
        case .cachedSession, .savedConfig, .bundledConfig, .autoProbe, .payload:
            return true
        default:
            return false
        }
    }

    private func shouldClearCachedPairingProfile(reasonCode: String?) -> Bool {
        guard let reasonCode, !reasonCode.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            return false
        }

        return Self.cachedProfileResetReasonCodes.contains(reasonCode)
    }

    private func shouldClearStoredPairingProfile(reasonCode: String?) -> Bool {
        guard let reasonCode, !reasonCode.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            return false
        }

        return Self.storedProfileResetReasonCodes.contains(reasonCode)
    }

    private func clearCachedPairingProfile(for resolvedRequest: ResolvedConnectionRequest) {
        let targetDocument = PairingConfigDocument(
            config: resolvedRequest.document.config,
            discovery: resolvedRequest.document.discoveryHint
        )
        let targetNetworkKey = Self.cachedPairingNetworkKey(for: targetDocument)
        let targetConfigId = resolvedRequest.document.config.configId
        var profiles = cachedPairingProfileDocuments()
        let originalCount = profiles.count
        profiles.removeAll { profile in
            Self.cachedPairingNetworkKey(for: profile) == targetNetworkKey &&
                profile.document.config.configId == targetConfigId
        }

        if profiles.count != originalCount {
            saveCachedPairingProfileDocuments(profiles)
        }
    }

    public func disconnect() async -> HostConnectionResult {
        stopScreenCapture(message: "Screen capture stopped.")
        stopTouchVisualTreeCapture()
        stopTouchCaptureStreaming(message: "Touch capture streaming stopped.")
        await liveTransport.close(notify: false)
        lock.withLock {
            connectionState = .disconnected
            sessionOpen = false
            sessionId = nil
            lastStreamedMetricSequence = 0
            lastStreamedEventSequence = 0
            announcedMetricChannelIds = []
            telemetryStreamLoopActive = false
            hostSessionJpegCapturePolicy = .app
            resolvedHostAddress = nil
            hostId = nil
            hostName = nil
            lastDisconnectedAtUtc = Date()
            pendingBinaryTransfers.removeAll()
            sessionMessage = "Session disconnected."
        }
        publishHostConnectionStatusIfChanged()
        return HostConnectionResult(
            success: true,
            message: "Session disconnected.",
            kind: .auto,
            source: .hostConnection,
            reasonCode: nil
        )
    }

    private func handleLiveTransportClosed(reason: String) {
        stopScreenCapture(message: "Screen capture stopped because the live session closed.")
        stopTouchVisualTreeCapture()
        stopTouchCaptureStreaming(message: "Touch capture streaming stopped because the live session closed.")

        let shouldReconnect = lock.withLock { () -> Bool in
            guard connectionState == .connected || sessionOpen else {
                return false
            }

            connectionState = .disconnected
            sessionOpen = false
            sessionId = nil
            lastStreamedMetricSequence = 0
            lastStreamedEventSequence = 0
            announcedMetricChannelIds = []
            telemetryStreamLoopActive = false
            resolvedHostAddress = nil
            hostId = nil
            hostName = nil
            lastDisconnectedAtUtc = Date()
            pendingBinaryTransfers.removeAll()
            sessionMessage = reason

            return initialized &&
                active &&
                options.hostAutoProbe.enabled
        }
        publishHostConnectionStatusIfChanged()

        if shouldReconnect {
            startAutoProbeIfNeeded()
        }
    }

    public func openLiveSession(pairingJson: String, options: PairingOpenOptions) async throws -> OpenSessionResult {
        let request = HostConnectionRequest.payloadText(
            pairingJson,
            clientName: options.clientName,
            expectedAppId: options.expectedAppId,
            hostAddressOverride: options.hostAddressOverride,
            sourceDescription: "PairingOpenOptions"
        )
        let result = await connect(request)
        return result.openSession ?? OpenSessionResult(
            success: result.success,
            message: result.message,
            sessionId: nil,
            reasonCode: result.reasonCode
        )
    }

    public func openSession(pairingJson: String, options: PairingOpenOptions) throws -> OpenSessionResult {
        let result = try lock.withLock {
            guard initialized else {
                throw RuntimeError.notInitialized("AnsightRuntime must be initialized before opening a session.")
            }

            guard !pairingJson.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
                return OpenSessionResult(
                    success: false,
                    message: "An enrollment invite is required.",
                    sessionId: nil
                )
            }

            let document = try pairingDocumentService.parseAndValidateDocument(
                pairingJson,
                expectedAppId: options.expectedAppId
            )

            let hintedHostAddress = PairingHostAddressCandidates.resolve(
                discoveryHint: document.discoveryHint,
                hostAddressOverride: options.hostAddressOverride,
                simulatorLocalHostAddress: PairingSimulatorLocalHostAddress.resolve()
            ).first
            guard let hintedHostAddress, !hintedHostAddress.isEmpty else {
                return OpenSessionResult(
                    success: false,
                    message: "Pairing config must include a discovery host hint.",
                    sessionId: nil,
                    configId: document.config.configId,
                    appId: document.config.appId,
                    discoverySource: document.discoveryHint?.source,
                    reasonCode: PairingFailureCodes.hostAddressRequired
                )
            }

            sessionOpen = true
            sessionId = "ios-\(UUID().uuidString)"
            lastPairingDocument = document
            resolvedHostAddress = hintedHostAddress
            sessionMessage =
                "Harness session opened locally for \(options.clientName) using config \(document.config.configId) at \(hintedHostAddress). Use connect(...) or openLiveSession(...) for a live Studio session."
            return OpenSessionResult(
                success: true,
                message: sessionMessage ?? "Session opened.",
                sessionId: sessionId,
                configId: document.config.configId,
                appId: document.config.appId,
                resolvedHostAddress: hintedHostAddress,
                discoverySource: document.discoveryHint?.source
            )
        }
        if result.success {
            publishHostConnectionStatusIfChanged()
        }
        return result
    }

    public func sendClientLog(_ logLine: String) async -> OperationResult {
        let trimmedLogLine = logLine.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmedLogLine.isEmpty else {
            return .failure("Enter log text before sending.")
        }

        guard liveTransport.isOpen else {
            return .failure("WebSocket session is not open.")
        }

        let result = await liveTransport.sendControlRequest(
            action: PairingControlActions.clientLog,
            payload: Self.makeClientLogPayload(trimmedLogLine)
        )

        lock.withLock {
            sessionMessage = result.success ? "Log sent." : result.message
        }
        return result.success ? .success("Log sent.") : result
    }

    /// Sanitizes and sends one typed V1 network request record to the active host session.
    public func recordNetworkRequest(_ request: AnsightNetworkRequest) async -> OperationResult {
        guard liveTransport.isOpen else {
            return .failure("WebSocket session is not open.")
        }
        let sanitized = AnsightNetworkRequestSanitizer.sanitize(request)

        do {
            let envelope = JSONValue.object([
                "type": .string("CLIENT_NETWORK_REQUEST"),
                "sentAtUtc": .string(AnsightClock.isoNow()),
                "request": try JSONValue.fromEncodable(sanitized),
            ])
            return await liveTransport.sendText(try envelope.jsonString())
        } catch {
            return .failure("Failed to encode network request: \(error.localizedDescription)")
        }
    }

    /// Backwards-compatible JSON bridge used by managed native bindings.
    public func recordNetworkRequest(json: String) async -> OperationResult {
        guard let data = json.data(using: .utf8),
              let request = try? JSONDecoder().decode(AnsightNetworkRequest.self, from: data),
              request.schema == AnsightNetworkRequest.schemaName
        else {
            return .failure("Network request JSON must use the ansight.network-request.v1 schema.")
        }
        return await recordNetworkRequest(request)
    }

    public func sendControlRequest(action: String, payload: JSONValue?) async -> OperationResult {
        let normalizedAction = action.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !normalizedAction.isEmpty else {
            return .failure("A control request action is required.")
        }
        guard liveTransport.isOpen else {
            return .failure("WebSocket session is not open.")
        }

        return await liveTransport.sendControlRequest(
            action: normalizedAction,
            payload: payload
        )
    }

    public func sendBinaryData(_ data: Data) async -> OperationResult {
        guard liveTransport.isOpen else {
            return .failure("WebSocket session is not open.")
        }

        return await liveTransport.sendData(data)
    }

    public func updateSessionProperties(_ customProperties: [String: [String: String]]) async -> OperationResult {
        let normalized = Self.normalizedCustomProperties(customProperties)
        lock.withLock {
            options.customProperties = normalized
            sessionMessage = "Session properties updated locally."
        }

        guard liveTransport.isOpen else {
            return .success("Session properties updated locally.")
        }

        let payload = Self.makeSessionPropertiesPayload(normalized)
        let transport = liveTransport
        Task { [weak self] in
            let result = await transport.sendControlRequest(
                action: PairingControlActions.sessionProperties,
                payload: payload,
                acknowledgementTimeoutSeconds: 10
            )

            guard let self else {
                return
            }
            self.lock.withLock {
                self.sessionMessage = result.success ? "Session properties updated." : result.message
            }
            if !result.success {
                AnsightLogger.warning(result.message)
            }
        }
        return .success("Session properties updated.")
    }

    public func clearSessionProperties() async -> OperationResult {
        await updateSessionProperties([:])
    }

    public func completeSession() {
        Task {
            _ = await completeLiveSession()
        }
    }

    @discardableResult
    public func completeLiveSession() async -> OperationResult {
        let shouldSendComplete = liveTransport.isOpen
        let result: OperationResult
        if shouldSendComplete {
            result = await liveTransport.sendControlRequest(
                action: PairingControlActions.sessionComplete,
                payload: Self.makeSessionCompletePayload(),
                acknowledgementTimeoutSeconds: 10
            )
        } else {
            result = .success("Session already closed.")
        }

        AnsightCrashCapture.shared.markStudioSessionCompleted()
        closeSession()
        return result
    }

    public func closeSession() {
        stopScreenCapture(message: "Screen capture stopped.")
        stopTouchVisualTreeCapture()
        stopTouchCaptureStreaming(message: "Touch capture streaming stopped.")
        lock.withLock {
            sessionOpen = false
            sessionId = nil
            connectionState = .disconnected
            lastStreamedMetricSequence = 0
            lastStreamedEventSequence = 0
            announcedMetricChannelIds = []
            telemetryStreamLoopActive = false
            resolvedHostAddress = nil
            hostId = nil
            hostName = nil
            lastDisconnectedAtUtc = Date()
            pendingBinaryTransfers.removeAll()
            sessionMessage = "Session closed."
        }
        publishHostConnectionStatusIfChanged()

        Task {
            await liveTransport.close(notify: false)
        }
    }

    public func isToolRegistered(_ toolId: String) -> Bool {
        let normalizedId = AnsightToolProtocolBridge.normalizedToolId(toolId)
        guard !normalizedId.isEmpty else {
            return false
        }

        return lock.withLock {
            tools[normalizedId] != nil
        }
    }

    public func setExternalToolProtocolHandler(
        _ handler: (@Sendable (String) throws -> String?)?
    ) {
        lock.withLock {
            externalToolProtocolHandler = handler
            sessionMessage = handler == nil
                ? "External tool protocol handler cleared."
                : "External tool protocol handler registered."
        }
    }

    public func setExternalToolProtocolResponseSentHandler(
        _ handler: (@Sendable (String) -> Void)?
    ) {
        lock.withLock {
            externalToolProtocolResponseSentHandler = handler
        }
    }

    public func registerTool(_ tool: AnsightToolDescriptor, replaceExisting: Bool = false) throws {
        let normalizedId = AnsightToolProtocolBridge.normalizedToolId(tool.id)
        guard !normalizedId.isEmpty else {
            throw RuntimeError.invalidInput("Tool id must not be blank.")
        }

        try lock.withLock {
            guard replaceExisting || tools[normalizedId] == nil else {
                throw RuntimeError.invalidInput("A tool with id '\(tool.id)' has already been registered.")
            }

            tools[normalizedId] = RegisteredTool(
                descriptor: tool,
                availability: { _ in .availableNow },
                execute: nil
            )
            sessionMessage = "Registered tool \(tool.id)."
        }
    }

    public func registerTool(_ tool: any AnsightTool, replaceExisting: Bool = false) throws {
        let descriptor = tool.descriptor
        let normalizedId = AnsightToolProtocolBridge.normalizedToolId(descriptor.id)
        guard !normalizedId.isEmpty else {
            throw RuntimeError.invalidInput("Tool id must not be blank.")
        }

        try lock.withLock {
            guard replaceExisting || tools[normalizedId] == nil else {
                throw RuntimeError.invalidInput("A tool with id '\(descriptor.id)' has already been registered.")
            }

            tools[normalizedId] = RegisteredTool(
                descriptor: descriptor,
                availability: tool.availability(context:),
                execute: tool.execute(arguments:)
            )
            sessionMessage = "Registered executable tool \(descriptor.id)."
        }
    }

    private func registerExecutableTool(
        _ descriptor: AnsightToolDescriptor,
        replaceExisting: Bool = false,
        execute: @escaping ([String: String]) throws -> AnsightToolExecutionResult
    ) throws {
        let normalizedId = AnsightToolProtocolBridge.normalizedToolId(descriptor.id)
        guard !normalizedId.isEmpty else {
            throw RuntimeError.invalidInput("Tool id must not be blank.")
        }

        try lock.withLock {
            guard replaceExisting || tools[normalizedId] == nil else {
                throw RuntimeError.invalidInput("A tool with id '\(descriptor.id)' has already been registered.")
            }

            tools[normalizedId] = RegisteredTool(
                descriptor: descriptor,
                availability: { _ in .availableNow },
                execute: execute
            )
            sessionMessage = "Registered executable tool \(descriptor.id)."
        }
    }

    public func registerArtifactProvider(
        _ provider: any AnsightArtifactProvider,
        replaceExisting: Bool = false
    ) throws {
        let descriptor = try provider.descriptor.validated()
        let normalizedId = descriptor.id.lowercased()

        try lock.withLock {
            guard replaceExisting || artifactProviders[normalizedId] == nil else {
                throw RuntimeError.invalidInput("An artifact provider with id '\(descriptor.id)' has already been registered.")
            }

            artifactProviders[normalizedId] = provider
            sessionMessage = "Registered artifact provider \(descriptor.id)."
        }

        try registerArtifactToolsIfNeeded()
    }

    public func registerArtifactProviders(
        _ providers: [any AnsightArtifactProvider],
        replaceExisting: Bool = false
    ) throws {
        for provider in providers {
            try registerArtifactProvider(provider, replaceExisting: replaceExisting)
        }
    }

    @discardableResult
    public func unregisterArtifactProvider(_ providerId: String) -> Bool {
        let normalizedId = providerId.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        guard !normalizedId.isEmpty else {
            return false
        }

        return lock.withLock {
            let removed = artifactProviders.removeValue(forKey: normalizedId) != nil
            if removed {
                sessionMessage = "Unregistered artifact provider \(providerId)."
            }

            return removed
        }
    }

    public func clearArtifactProviders() {
        lock.withLock {
            artifactProviders.removeAll()
            sessionMessage = "Cleared artifact providers."
        }
    }

    public func registeredArtifactProviderIds() -> [String] {
        lock.withLock {
            artifactProviders.values
                .compactMap { try? $0.descriptor.validated().id }
                .sorted { $0.localizedCaseInsensitiveCompare($1) == .orderedAscending }
        }
    }

    public func registeredArtifactProviders() -> [any AnsightArtifactProvider] {
        lock.withLock {
            artifactProviders.values.sorted { lhs, rhs in
                let lhsId = (try? lhs.descriptor.validated().id) ?? lhs.descriptor.id
                let rhsId = (try? rhs.descriptor.validated().id) ?? rhs.descriptor.id
                return lhsId.localizedCaseInsensitiveCompare(rhsId) == .orderedAscending
            }
        }
    }

    public func registerArtifactTools(replaceExisting: Bool = false) throws {
        let providers: @Sendable () -> [any AnsightArtifactProvider] = { [weak self] in
            self?.registeredArtifactProviders() ?? []
        }

        let queryDescriptor = AnsightArtifactToolSupport.queryDescriptor
        if replaceExisting || !isToolRegistered(queryDescriptor.id) {
            try registerExecutableTool(queryDescriptor, replaceExisting: replaceExisting) { arguments in
                try AnsightArtifactToolSupport.executeQuery(arguments: arguments, providers: providers)
            }
        }

        let requestDescriptor = AnsightArtifactToolSupport.requestDescriptor
        if replaceExisting || !isToolRegistered(requestDescriptor.id) {
            try registerExecutableTool(requestDescriptor, replaceExisting: replaceExisting) { [weak self] arguments in
                guard let self else {
                    return .failure("AnsightRuntime is no longer available.", errorCode: "artifact_request_failed")
                }

                return try AnsightArtifactToolSupport.executeRequest(
                    arguments: arguments,
                    providers: providers,
                    runtime: self
                )
            }
        }
    }

    public func handleToolProtocolMessage(_ json: String) throws -> String? {
        if let externalHandler = lock.withLock({ externalToolProtocolHandler }) {
            return try externalHandler(json)
        }

        let bridge = lock.withLock {
            AnsightToolProtocolBridge(registry: tools, guardPolicy: options.toolGuard)
        }

        guard initialized else {
            return bridge.runtimeNotInitializedResponse(for: json)
        }

        return try bridge.handleIfSupported(json)
    }

    private func registerArtifactToolsIfNeeded() throws {
        try registerArtifactTools(replaceExisting: false)
    }

    public func queueBinaryTransfer(
        requestId rawRequestId: String,
        transferId: UUID,
        data: Data,
        chunkBytes: Int = 64 * 1024,
        description: String = "binary-transfer"
    ) -> OperationResult {
        let requestId = rawRequestId.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !requestId.isEmpty else {
            return .failure("Binary transfer requires a live tool request id.")
        }

        guard liveTransport.isOpen else {
            return .failure("Binary transfers require an active pairing WebSocket session.")
        }

        let transfer = AnsightPendingBinaryTransfer(
            transferId: transferId,
            data: data,
            chunkBytes: chunkBytes,
            description: description
        )
        lock.withLock {
            pendingBinaryTransfers[requestId] = transfer
        }
        return .success("Binary transfer queued.")
    }

    public func captureBuiltInTelemetrySample() {
        let streams = lock.withLock { () -> [AnsightMetricStream] in
            guard initialized && active else {
                return []
            }

            return Array(metricStreams.values)
        }

        guard lock.withLock({ initialized && active }) else {
            return
        }

        let configuredChannels = lock.withLock { Set(channels.keys) }
        if configuredChannels.contains(AnsightChannels.physicalFootprint),
           let bytes = Self.currentPhysicalFootprintBytes() {
            try? metric(bytes, channel: AnsightChannels.physicalFootprint)
        }

        if configuredChannels.contains(AnsightChannels.batteryLevel),
           lock.withLock({ options.enableBatteryLevel }),
           let level = Self.currentBatteryLevelPercentage() {
            try? metric(Int64(level), channel: AnsightChannels.batteryLevel)
        }

        if configuredChannels.contains(AnsightChannels.openFileHandles),
           let count = Self.currentOpenFileHandleCount() {
            try? metric(count, channel: AnsightChannels.openFileHandles)
        }

        let streamMetrics = streams.compactMap { stream -> RecordedMetric? in
            guard let value = stream.sample() else {
                return nil
            }

            return RecordedMetric(value: value, channel: stream.channel.id)
        }

        guard !streamMetrics.isEmpty else {
            return
        }

        lock.withLock {
            guard initialized && active else {
                return
            }

            for metric in streamMetrics {
                guard channels[metric.channel] != nil else {
                    continue
                }

                recordMetricLocked(
                    value: metric.value,
                    channel: metric.channel,
                    capturedAtUtc: metric.capturedAtUtc
                )
            }
        }

        streamPendingTelemetry()
    }

    public func hostConnectionStatus() -> HostConnectionStatus {
        lock.withLock { hostConnectionStatusLocked }
    }

    public func hostConnectionCapabilities() -> HostConnectionCapabilities {
        lock.withLock { hostConnectionCapabilitiesLocked }
    }

    @discardableResult
    public func addHostConnectionStatusListener(
        emitCurrent: Bool = true,
        _ listener: @escaping HostConnectionStatusListener
    ) -> HostConnectionStatusSubscription {
        let listenerId = UUID()
        let current = lock.withLock { () -> HostConnectionSnapshot? in
            hostConnectionStatusListeners[listenerId] = listener
            guard emitCurrent else {
                return nil
            }

            let snapshot = hostConnectionSnapshotLocked()
            lastPublishedHostConnectionStatus = snapshot.status
            lastPublishedHostConnectionCapabilities = snapshot.capabilities
            return snapshot
        }

        if let current {
            listener(current.status, current.capabilities)
        }

        return HostConnectionStatusSubscription { [weak self] in
            guard let self else {
                return
            }

            self.lock.withLock {
                self.hostConnectionStatusListeners.removeValue(forKey: listenerId)
            }
        }
    }

    public func snapshot() -> AnsightDebugSnapshot {
        lock.withLock {
            let executableTools = tools.values.filter { $0.execute != nil }.count
            return AnsightDebugSnapshot(
                initialized: initialized,
                active: active,
                sessionOpen: sessionOpen,
                metricsRecorded: metrics.count,
                eventsRecorded: events.count,
                registeredTools: tools.count,
                executableTools: executableTools,
                toolDiscoveryEnabled: options.toolGuard.discoveryEnabled,
                toolExecutionEnabled: options.toolGuard.executionEnabled,
                detectedBundledTools: AnsightDeveloperMode.bundledToolScanReport.detectedToolTypes,
                lastMetric: metrics.last,
                lastEvent: events.last,
                lastPairingConfigId: lastPairingDocument?.config.configId,
                resolvedHostAddress: resolvedHostAddress,
                sessionMessage: sessionMessage,
                lifecycleState: currentLifecycleState,
                currentScreen: currentScreen,
                channels: channels.values.sorted { $0.id < $1.id },
                hostConnectionStatus: hostConnectionStatusLocked,
                screenCaptureActive: screenCaptureTask != nil,
                screenFramesCaptured: screenFramesCaptured,
                screenFramesSent: screenFramesSent,
                lastScreenCaptureMessage: lastScreenCaptureMessage,
                lastScreenCaptureRenderMilliseconds: lastScreenCaptureRenderMilliseconds,
                lastScreenCaptureEncodeMilliseconds: lastScreenCaptureEncodeMilliseconds,
                lastScreenCaptureSendMilliseconds: lastScreenCaptureSendMilliseconds,
                lastScreenCaptureTotalMilliseconds: lastScreenCaptureTotalMilliseconds,
                frameRateCaptureActive: frameRateSampler != nil,
                lastFrameRate: lastFrameRate,
                touchCaptureEnabled: touchCaptureRuntimeEnabled && options.touchCapture != nil,
                touchCaptureActive: touchCaptureSession != nil,
                touchCaptureStreamingActive: touchCaptureStreamer?.isStreaming == true,
                touchesCaptured: touchesCaptured,
                touchesSent: touchesSent,
                lastTouchCaptureMessage: lastTouchCaptureMessage
            )
        }
    }

    public func currentOptions() -> AnsightOptions {
        lock.withLock { options }
    }

    public func notifyHostConnectionConfigChanged() -> HostConnectionResult {
        let result = lock.withLock {
            guard initialized else {
                return HostConnectionResult(
                    success: false,
                    message: "AnsightRuntime must be initialized before refreshing host connection config state.",
                    kind: .config,
                    source: .configReader
                )
            }

            let status = hostConnectionStatusLocked
            sessionMessage = status.summaryMessage
            let source: HostConnectionSource
            if hasBundledConfigLocked {
                source = .bundledConfig
            } else {
                source = .configReader
            }
            return HostConnectionResult(
                success: true,
                message: status.summaryMessage,
                kind: hasBundledConfigLocked ? .bundledConfig : .config,
                source: source
            )
        }
        if result.success {
            publishHostConnectionStatusIfChanged(force: true)
        }
        return result
    }

    public func recordedMetrics() -> [RecordedMetric] {
        lock.withLock { metrics }
    }

    public func recordedEvents() -> [RecordedEvent] {
        lock.withLock { events }
    }

    private func startQueuedBinaryTransferIfNeeded(forToolProtocolMessage json: String) {
        guard let data = json.data(using: .utf8),
              let envelope = try? JSONDecoder.ansightDecoder.decode(AnsightToolProtocolEnvelope.self, from: data),
              envelope.type == AnsightToolProtocolBridge.callType
        else {
            return
        }

        let transfer = lock.withLock {
            pendingBinaryTransfers.removeValue(forKey: envelope.id)
        }
        guard let transfer else {
            return
        }

        Task { [weak self] in
            await self?.streamBinaryTransfer(transfer)
        }
    }

    private func streamBinaryTransfer(_ transfer: AnsightPendingBinaryTransfer) async {
        var sequence: Int32 = 0
        var offsetBytes: Int64 = 0

        do {
            while offsetBytes < transfer.data.count {
                let remainingBytes = transfer.data.count - Int(offsetBytes)
                let byteCount = min(transfer.chunkBytes, remainingBytes)
                let payload = transfer.data.subdata(in: Int(offsetBytes)..<Int(offsetBytes) + byteCount)
                let frame = PairingFileTransferWireProtocol.createFrame(
                    transferId: transfer.transferId,
                    frameType: .chunk,
                    sequence: sequence,
                    offsetBytes: offsetBytes,
                    payload: payload
                )
                let result = await liveTransport.sendData(frame)
                guard result.success else {
                    throw RuntimeError.invalidInput(result.message)
                }

                sequence += 1
                offsetBytes += Int64(byteCount)
            }

            let completeFrame = PairingFileTransferWireProtocol.createFrame(
                transferId: transfer.transferId,
                frameType: .complete,
                sequence: sequence,
                offsetBytes: offsetBytes,
                payload: Data()
            )
            let completeResult = await liveTransport.sendData(completeFrame)
            guard completeResult.success else {
                throw RuntimeError.invalidInput(completeResult.message)
            }
        } catch {
            await sendBinaryTransferErrorFrame(
                transferId: transfer.transferId,
                sequence: sequence,
                offsetBytes: offsetBytes,
                message: error.localizedDescription
            )
        }
    }

    private func sendBinaryTransferErrorFrame(
        transferId: UUID,
        sequence: Int32,
        offsetBytes: Int64,
        message: String
    ) async {
        let payload = Data(message.utf8)
        let frame = PairingFileTransferWireProtocol.createFrame(
            transferId: transferId,
            frameType: .error,
            sequence: sequence,
            offsetBytes: offsetBytes,
            payload: payload
        )
        _ = await liveTransport.sendData(frame)
    }

    private func validateChannel(_ channel: Int) throws -> Int {
        guard (0...255).contains(channel) else {
            throw RuntimeError.invalidInput("Channel ids must be between 0 and 255.")
        }

        if channels[channel] == nil {
            channels[channel] = AnsightChannel(id: channel, name: "Channel \(channel)", color: nil)
        }

        return channel
    }

    private func recordMetricLocked(value: Int64, channel: Int, capturedAtUtc: String = AnsightClock.isoNow()) {
        nextMetricSequence += 1
        metrics.append(
            RecordedMetric(
                value: value,
                channel: channel,
                capturedAtUtc: capturedAtUtc,
                sequence: nextMetricSequence
            )
        )
        trimMetricsLocked()
    }

    private func startTelemetrySamplingIfNeeded() {
        telemetrySamplingTask?.cancel()
        let state = lock.withLock { () -> (Int, Int) in
            telemetryGeneration += 1
            return (options.sampleFrequencyMilliseconds, telemetryGeneration)
        }
        let intervalMs = state.0
        let generation = state.1
        telemetrySamplingTask = Task { [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(nanoseconds: UInt64(intervalMs) * 1_000_000)
                guard self?.isTelemetrySamplingCurrent(generation) == true else {
                    break
                }
                self?.captureBuiltInTelemetrySample()
            }
        }
    }

    private func isTelemetrySamplingCurrent(_ generation: Int) -> Bool {
        lock.withLock {
            initialized && active && telemetryGeneration == generation
        }
    }

    private func startFrameRateSamplingIfNeeded() {
        guard AnsightFrameRateSampler.isAvailable else {
            return
        }

        let sampler = lock.withLock { () -> AnsightFrameRateSampler? in
            guard initialized,
                  active,
                  frameRateTrackingEnabled,
                  frameRateSampler == nil
            else {
                return nil
            }

            let sampler = AnsightFrameRateSampler(
                sampleFrequencyMilliseconds: options.sampleFrequencyMilliseconds
            ) { [weak self] framesPerSecond in
                self?.recordFrameRateSample(framesPerSecond)
            }
            frameRateSampler = sampler
            return sampler
        }

        sampler?.start()
    }

    private func startLifecycleCaptureIfNeeded() {
        guard AnsightLifecycleObserver.isAvailable else {
            return
        }

        let lifecycleOptions = lock.withLock { () -> AnsightLifecycleCaptureOptions? in
            guard initialized,
                  active,
                  options.lifecycleCapture.enabled
            else {
                return nil
            }

            return options.lifecycleCapture
        }

        if let lifecycleOptions {
            lifecycleObserver.start(runtime: self, options: lifecycleOptions)
        }
    }

    private func stopLifecycleCapture() {
        lifecycleObserver.stop()
    }

    private func stopFrameRateSampling() {
        let sampler = lock.withLock { () -> AnsightFrameRateSampler? in
            let sampler = frameRateSampler
            frameRateSampler = nil
            return sampler
        }
        sampler?.stop()
    }

    private func startTouchCaptureIfNeeded() {
        guard AnsightTouchCaptureSession.isAvailable else {
            return
        }

        let session = lock.withLock { () -> AnsightTouchCaptureSession? in
            guard initialized,
                  active,
                  touchCaptureRuntimeEnabled,
                  let options = options.touchCapture,
                  touchCaptureSession == nil
            else {
                return nil
            }

            let session = AnsightTouchCaptureSession(options: options) { [weak self] touch in
                self?.recordCapturedTouch(touch)
            }
            touchCaptureSession = session
            lastTouchCaptureMessage = "Touch capture started."
            return session
        }

        session?.start()
    }

    private func stopTouchCapture(message: String) {
        let state = lock.withLock { () -> (AnsightTouchCaptureSession?, AnsightTouchCaptureStreamer?) in
            let session = touchCaptureSession
            let streamer = touchCaptureStreamer
            touchCaptureSession = nil
            touchCaptureStreamer = nil
            lastTouchCaptureMessage = message
            return (session, streamer)
        }
        state.0?.stop()
        _ = state.1?.stop()
    }

    private func startTouchCaptureStreamingIfNeeded() -> OperationResult {
        let streamer = lock.withLock { () -> AnsightTouchCaptureStreamer? in
            guard initialized,
                  active,
                  touchCaptureRuntimeEnabled,
                  options.touchCapture != nil
            else {
                if lastTouchCaptureMessage == nil {
                    lastTouchCaptureMessage = "Touch capture is not enabled."
                }
                return nil
            }

            if let touchCaptureStreamer {
                return touchCaptureStreamer
            }

            let streamer = AnsightTouchCaptureStreamer(transport: liveTransport) { [weak self] count, result in
                self?.recordTouchCaptureStreamingResult(sentCount: count, result: result)
            }
            touchCaptureStreamer = streamer
            return streamer
        }

        guard let streamer else {
            return .success("Touch capture is not enabled.")
        }

        let result = streamer.start()
        lock.withLock {
            lastTouchCaptureMessage = result.message
            if result.success {
                sessionMessage = result.message
            }
        }
        return result
    }

    private func stopTouchCaptureStreaming(message: String) {
        let result = lock.withLock { () -> OperationResult? in
            guard let streamer = touchCaptureStreamer else {
                lastTouchCaptureMessage = message
                return nil
            }

            lastTouchCaptureMessage = message
            return streamer.stop()
        }
        if let result {
            lock.withLock {
                lastTouchCaptureMessage = result.success ? message : result.message
            }
        }
    }

    private func recordTouchCaptureStreamingResult(sentCount: Int, result: OperationResult) {
        lock.withLock {
            if result.success {
                touchesSent += sentCount
            }
            lastTouchCaptureMessage = result.message
            sessionMessage = result.message
        }
    }

    private func pauseLiveSessionPipelinesForBackground() {
        stopScreenCapture(message: "Screen capture paused while app is in the background.")
        stopTouchVisualTreeCapture()
        stopTouchCaptureStreaming(message: "Touch capture streaming paused while app is in the background.")
    }

    private func recoverLiveSessionAfterForeground() {
        let action = foregroundRecoveryAction(transportOpen: liveTransport.isOpen)
        guard action != .none else {
            return
        }

        Task { [weak self] in
            guard let self else {
                return
            }

            await self.performForegroundRecovery(action)
        }
    }

    func foregroundRecoveryActionForTesting(transportOpen: Bool) -> RuntimeForegroundRecoveryAction {
        foregroundRecoveryAction(transportOpen: transportOpen)
    }

    private func foregroundRecoveryAction(transportOpen: Bool) -> RuntimeForegroundRecoveryAction {
        lock.withLock {
            guard initialized, active else {
                return .none
            }

            guard connectionState != .connecting,
                  connectionTask == nil
            else {
                return .none
            }

            if transportOpen, sessionOpen, connectionState == .connected {
                sessionMessage = "Foregrounded; refreshing live session streams."
                return .refreshOpenSession
            }

            guard options.hostAutoProbe.enabled else {
                if transportOpen {
                    sessionMessage = "Foregrounded with stale live transport; closing."
                    return .closeStaleTransport
                }

                return .none
            }

            lastDisconnectedAtUtc = nil
            if transportOpen {
                connectionState = .disconnected
                sessionOpen = false
                sessionId = nil
                lastStreamedMetricSequence = 0
                lastStreamedEventSequence = 0
                announcedMetricChannelIds = []
                telemetryStreamLoopActive = false
                sessionMessage = "Foregrounded with stale live transport; reconnecting."
                return .closeStaleTransportAndReconnect
            }

            sessionMessage = "Foregrounded; reconnecting to Ansight host."
            return .reconnect
        }
    }

    private func performForegroundRecovery(_ action: RuntimeForegroundRecoveryAction) async {
        switch action {
        case .none:
            return
        case .refreshOpenSession:
            await refreshLiveSessionPipelinesAfterForeground()
        case .closeStaleTransport:
            await liveTransport.close(notify: false)
        case .reconnect:
            await reconnectLiveSessionAfterForeground()
        case .closeStaleTransportAndReconnect:
            await liveTransport.close(notify: false)
            await reconnectLiveSessionAfterForeground()
        }
    }

    private func refreshLiveSessionPipelinesAfterForeground() async {
        await sendCurrentAppState()
        _ = await sendMetricChannelDefinitions()
        _ = startTouchCaptureStreamingIfNeeded()
        streamPendingTelemetry()
        startScreenCaptureIfNeeded()
        startTouchVisualTreeCaptureIfNeeded()
    }

    private func reconnectLiveSessionAfterForeground() async {
        let clientName = lock.withLock { options.hostAutoProbe.clientName }
        let result = await connect(.auto(clientName: clientName, sourceDescription: "foreground-recovery"))
        if result.success {
            return
        }

        let shouldContinueAutoProbe = lock.withLock { () -> Bool in
            if active && !sessionOpen {
                sessionMessage = result.message
            }

            return initialized && active && options.hostAutoProbe.enabled
        }

        if shouldContinueAutoProbe {
            startAutoProbeIfNeeded()
        }
    }

    private func startAutoProbeIfNeeded() {
        autoProbeTask?.cancel()
        let autoOptions = lock.withLock { options.hostAutoProbe }
        guard autoOptions.enabled else {
            autoProbeTask = nil
            return
        }

        autoProbeTask = Task { [weak self] in
            guard let self else {
                return
            }
            guard await self.sleepAutoProbe(milliseconds: autoOptions.initialDelayMilliseconds) else {
                return
            }

            while !Task.isCancelled {
                let nextDelayMilliseconds = self.lock.withLock { () -> Int? in
                    guard self.initialized,
                          self.active,
                          self.options.hostAutoProbe.enabled
                    else {
                        return nil
                    }

                    if self.sessionOpen || self.connectionState == .connecting {
                        return autoOptions.probeIntervalMilliseconds
                    }

                    if let lastDisconnectedAtUtc = self.lastDisconnectedAtUtc {
                        let elapsedMilliseconds = Int(Date().timeIntervalSince(lastDisconnectedAtUtc) * 1_000)
                        let remainingMilliseconds = autoOptions.reconnectDelayMilliseconds - elapsedMilliseconds
                        if remainingMilliseconds > 0 {
                            return remainingMilliseconds
                        }
                    }

                    guard self.hasCachedPairingProfileLocked ||
                            self.connector.localHostAddress != nil ||
                            self.hasUnattendedProvisioningPayloadLocked else {
                        return autoOptions.probeIntervalMilliseconds
                    }

                    return 0
                }

                guard let nextDelayMilliseconds else {
                    return
                }

                if nextDelayMilliseconds > 0 {
                    guard await self.sleepAutoProbe(milliseconds: nextDelayMilliseconds) else {
                        return
                    }
                    continue
                }

                let result = await self.connect(
                    .auto(
                        clientName: autoOptions.clientName,
                        sourceDescription: "runtime-enrollment"
                    )
                )
                if result.success {
                    continue
                }

                self.lock.withLock {
                    if self.active && !self.sessionOpen {
                        self.sessionMessage = result.message
                    }
                }

                guard await self.sleepAutoProbe(milliseconds: autoOptions.probeIntervalMilliseconds) else {
                    return
                }
            }
        }
    }

    private func sleepAutoProbe(milliseconds: Int) async -> Bool {
        guard milliseconds > 0 else {
            return !Task.isCancelled
        }

        do {
            try await Task.sleep(nanoseconds: UInt64(milliseconds) * 1_000_000)
            return !Task.isCancelled
        } catch {
            return false
        }
    }

    private func startScreenCaptureIfNeeded() {
        let state = lock.withLock { () -> (Int, AnsightSessionJpegCaptureOptions)? in
            guard initialized,
                  active,
                  sessionOpen,
                  !hostSessionJpegCapturePolicy.useHostCapture,
                  screenCaptureTask == nil,
                  var captureOptions = options.sessionJpegCapture
            else {
                return nil
            }

            captureOptions.validate()
            screenCaptureGeneration += 1
            lastScreenCaptureMessage = "Screen capture started."
            return (screenCaptureGeneration, captureOptions)
        }

        guard let (generation, captureOptions) = state else {
            return
        }

        let task = Task { [weak self] in
            guard let self else {
                return
            }

            await self.runScreenCaptureLoop(options: captureOptions, generation: generation)
        }

        let shouldCancel = lock.withLock { () -> Bool in
            guard screenCaptureGeneration == generation,
                  screenCaptureTask == nil,
                  initialized,
                  active,
                  sessionOpen
            else {
                return true
            }

            screenCaptureTask = task
            return false
        }

        if shouldCancel {
            task.cancel()
        }
    }

    private func startTouchVisualTreeCaptureIfNeeded() {
        let coordinator = lock.withLock { () -> AnsightTouchVisualTreeCaptureCoordinator? in
            guard initialized,
                  active,
                  sessionOpen,
                  options.touchCapture != nil,
                  touchCaptureRuntimeEnabled,
                  options.sessionJpegCapture?.mode == .screenshotWithVisualTreeOnTouch,
                  touchVisualTreeCaptureCoordinator == nil,
                  liveTransport.isOpen
            else {
                return nil
            }

            let configuredIntervalMilliseconds = options.sessionJpegCapture?.intervalMilliseconds ?? 0
            let minimumIntervalMilliseconds = max(
                configuredIntervalMilliseconds,
                Int(AnsightTouchVisualTreeCaptureCoordinator.defaultMinimumCaptureIntervalNanoseconds / 1_000_000)
            )
            let coordinator = AnsightTouchVisualTreeCaptureCoordinator(
                minimumCaptureIntervalNanoseconds: UInt64(minimumIntervalMilliseconds) * 1_000_000
            ) { [weak self] trigger in
                await self?.captureAndSendTouchVisualTrees(trigger)
            }
            touchVisualTreeCaptureCoordinator = coordinator
            return coordinator
        }

        if coordinator != nil {
            lock.withLock {
                lastTouchCaptureMessage = "Touch visual-tree capture started."
            }
        }
    }

    private func stopTouchVisualTreeCapture() {
        let coordinator = lock.withLock { () -> AnsightTouchVisualTreeCaptureCoordinator? in
            let coordinator = touchVisualTreeCaptureCoordinator
            touchVisualTreeCaptureCoordinator = nil
            return coordinator
        }
        coordinator?.close()
    }

    private func captureAndSendTouchVisualTrees(
        _ trigger: AnsightTouchVisualTreeCaptureTrigger
    ) async {
        guard lock.withLock({
            initialized &&
                active &&
                sessionOpen &&
                currentLifecycleState != .background &&
                options.touchCapture != nil &&
                touchCaptureRuntimeEnabled &&
                options.sessionJpegCapture?.mode == .screenshotWithVisualTreeOnTouch
        }), liveTransport.isOpen else {
            return
        }

        let capturedAtUtc = AnsightClock.isoNow()
        let visualTrees = AnsightSessionVisualTreeCaptureRegistry.capture()
        for visualTree in visualTrees {
            do {
                let event = Self.sessionVisualTreeEvent(
                    payload: visualTree,
                    capturedAtUtc: capturedAtUtc,
                    screenshotCapturedAtUtc: nil,
                    trigger: trigger
                )
                let result = await liveTransport.sendText(try event.jsonString())
                if !result.success {
                    lock.withLock { sessionMessage = result.message }
                    return
                }
            } catch {
                lock.withLock {
                    sessionMessage = "Failed to encode the captured touch visual tree: \(error.localizedDescription)"
                }
                return
            }
        }
    }

    private func stopScreenCapture(message: String? = nil) {
        let task = lock.withLock { () -> Task<Void, Never>? in
            screenCaptureGeneration += 1
            let task = screenCaptureTask
            screenCaptureTask = nil
            if let message {
                lastScreenCaptureMessage = message
            }
            return task
        }
        task?.cancel()
    }

    private func runScreenCaptureLoop(options: AnsightSessionJpegCaptureOptions, generation: Int) async {
        var captureOptions = options
        var nextCaptureDeadlineNanoseconds = DispatchTime.now().uptimeNanoseconds
        let sendBuffer = AnsightLatestValueBuffer<PreparedScreenFrame>()
        let sendTask = Task { [weak self] in
            while !Task.isCancelled,
                  let preparedFrame = await sendBuffer.next() {
                guard let self else {
                    break
                }

                _ = await self.sendPreparedScreenFrame(preparedFrame)
            }
        }

        while !Task.isCancelled {
            guard liveTransport.isOpen,
                  lock.withLock({ active && sessionOpen && screenCaptureGeneration == generation })
            else {
                break
            }

            let preparation = await prepareScreenFrame(options: captureOptions)
            if !preparation.operationResult.success && !liveTransport.isOpen {
                break
            }

            if let preparedFrame = preparation.preparedFrame {
                let replacedPendingFrame = await sendBuffer.submit(preparedFrame)
                if replacedPendingFrame {
                    lock.withLock {
                        lastScreenCaptureMessage = "Replaced a stale pending screen frame because delivery is slower than capture."
                        sessionMessage = lastScreenCaptureMessage
                    }
                }
            }

            if let adjustedMaxWidth = Self.adaptiveScreenCaptureMaxWidth(
                configuredMaxWidth: options.maxWidth,
                currentMaxWidth: captureOptions.maxWidth,
                frameWidth: preparation.frameWidth,
                renderMilliseconds: preparation.renderMilliseconds
            ) {
                captureOptions.maxWidth = adjustedMaxWidth
            }

            // The configured cadence is part of the capture contract. Slow renders may
            // reduce resolution above, while the latest-value buffer bounds delivery work.
            let captureIntervalNanoseconds = UInt64(options.intervalMilliseconds) * 1_000_000
            nextCaptureDeadlineNanoseconds += captureIntervalNanoseconds
            let currentTimeNanoseconds = DispatchTime.now().uptimeNanoseconds
            while nextCaptureDeadlineNanoseconds <= currentTimeNanoseconds {
                nextCaptureDeadlineNanoseconds += captureIntervalNanoseconds
            }

            do {
                try await Task.sleep(
                    nanoseconds: nextCaptureDeadlineNanoseconds - currentTimeNanoseconds
                )
            } catch {
                break
            }
        }

        await sendBuffer.finish()
        await sendTask.value

        lock.withLock {
            if screenCaptureGeneration == generation {
                screenCaptureTask = nil
                lastScreenCaptureMessage = "Screen capture stopped."
            }
        }
    }

    static func adaptiveScreenCaptureMaxWidth(
        configuredMaxWidth: Int?,
        currentMaxWidth: Int?,
        frameWidth: Int?,
        renderMilliseconds: Int?
    ) -> Int? {
        guard let renderMilliseconds,
              renderMilliseconds > screenCaptureRenderBudgetMilliseconds
        else {
            return currentMaxWidth
        }

        let currentWidth = currentMaxWidth ?? frameWidth ?? configuredMaxWidth
        guard let currentWidth,
              currentWidth > screenCaptureMinimumAdaptiveMaxWidth
        else {
            return currentMaxWidth
        }

        let downshiftedWidth = Int((Double(currentWidth) * 0.85).rounded(.down))
        let nextWidth = max(screenCaptureMinimumAdaptiveMaxWidth, downshiftedWidth)
        guard nextWidth < currentWidth else {
            return currentMaxWidth
        }
        return nextWidth
    }

    private func openLiveTransportSession(url: URL, config: PairingConfig, clientName: String) async -> LiveSessionOpenAttempt {
        let maxAttempts = 12
        let retryDelayNanoseconds: UInt64 = 250_000_000
        var lastResult = OperationResult.failure("WebSocket endpoint did not become reachable in time.")

        for attempt in 1...maxAttempts {
            do {
                try await liveTransport.attach(
                    url: url,
                    toolMessageHandler: { [weak self] message in
                        try? self?.handleToolProtocolMessage(message)
                    },
                    toolResponseSentHandler: { [weak self] request, _ in
                        self?.startQueuedBinaryTransferIfNeeded(forToolProtocolMessage: request)
                        self?.notifyExternalToolProtocolResponseSent(request)
                    },
                    closeHandler: { [weak self] reason in
                        self?.handleLiveTransportClosed(reason: reason)
                    }
                )
            } catch {
                lastResult = .failure("WebSocket endpoint did not become reachable: \(error.localizedDescription)")
                await liveTransport.close(notify: false)
                if attempt < maxAttempts {
                    try? await Task.sleep(nanoseconds: retryDelayNanoseconds)
                    continue
                }

                return LiveSessionOpenAttempt(
                    result: lastResult,
                    reasonCode: PairingFailureCodes.webSocketEndpointUnreachable
                )
            }

            let sessionOpenResult = await sendSessionOpen(config: config, clientName: clientName)
            if sessionOpenResult.success {
                return LiveSessionOpenAttempt(result: sessionOpenResult, reasonCode: nil)
            }

            lastResult = sessionOpenResult
            await liveTransport.close(notify: false)
            guard Self.isRetryableSessionOpenFailure(sessionOpenResult) else {
                return LiveSessionOpenAttempt(
                    result: sessionOpenResult,
                    reasonCode: PairingFailureCodes.webSocketHandshakeFailed
                )
            }

            if attempt < maxAttempts {
                try? await Task.sleep(nanoseconds: retryDelayNanoseconds)
            }
        }

        return LiveSessionOpenAttempt(
            result: .failure("WebSocket endpoint did not become reachable in time. Last error: \(lastResult.message)"),
            reasonCode: PairingFailureCodes.webSocketEndpointUnreachable
        )
    }

    private func openLiveTransportSession(
        authenticatedSocket: any PairingWebSocket,
        config: PairingConfig,
        clientName: String
    ) async -> LiveSessionOpenAttempt {
        await liveTransport.attach(
            authenticatedSocket: authenticatedSocket,
            toolMessageHandler: { [weak self] message in
                try? self?.handleToolProtocolMessage(message)
            },
            toolResponseSentHandler: { [weak self] request, _ in
                self?.startQueuedBinaryTransferIfNeeded(forToolProtocolMessage: request)
                self?.notifyExternalToolProtocolResponseSent(request)
            },
            closeHandler: { [weak self] reason in
                self?.handleLiveTransportClosed(reason: reason)
            }
        )
        let result = await sendSessionOpen(config: config, clientName: clientName)
        if !result.success {
            await liveTransport.close(notify: false)
        }
        return LiveSessionOpenAttempt(
            result: result,
            reasonCode: result.success ? nil : PairingFailureCodes.webSocketHandshakeFailed
        )
    }

    private static func isRetryableSessionOpenFailure(_ result: OperationResult) -> Bool {
        result.message.hasPrefix("Failed to send \(PairingControlActions.sessionOpen):")
    }

    private func notifyExternalToolProtocolResponseSent(_ request: String) {
        lock.withLock { externalToolProtocolResponseSentHandler }?(request)
    }

    private func sendSessionOpen(config: PairingConfig, clientName: String) async -> OperationResult {
        var payload: [String: JSONValue] = [
            "clientName": .string(clientName),
            "configId": .string(config.configId),
            "appId": .string(config.appId),
            "openedAtUtc": .string(AnsightClock.isoNow()),
        ]

        let customProperties = lock.withLock { options.customProperties }
        if !customProperties.isEmpty {
            payload["customProperties"] = .object(fromGrouped: customProperties)
        }

        return await liveTransport.sendControlRequest(
            action: PairingControlActions.sessionOpen,
            payload: .object(payload)
        )
    }

    private func sendDeviceProfile(_ profile: DeviceAppProfile) async -> OperationResult {
        do {
            guard case .object(var payload) = try JSONValue.fromEncodable(profile) else {
                return .failure("Failed to encode device profile as a JSON object.")
            }
            payload[HostSessionJpegCapturePolicy.controlVersionPropertyName] =
                .integer(HostSessionJpegCapturePolicy.controlVersion)
            let result = await liveTransport.sendControlRequestWithResponse(
                action: PairingControlActions.deviceProfile,
                payload: .object(payload)
            )
            lock.withLock {
                hostSessionJpegCapturePolicy = HostSessionJpegCapturePolicy(
                    payload: result.response?.payload
                )
            }
            return result.operationResult
        } catch {
            lock.withLock {
                hostSessionJpegCapturePolicy = .app
            }
            return .failure("Failed to encode device profile: \(error.localizedDescription)")
        }
    }

    private func sendCurrentAppState() async {
        let payload = lock.withLock {
            JSONValue.object([
                "state": .string(currentLifecycleState.rawValue),
                "changedAtUtc": .string(currentLifecycleChangedAtUtc ?? AnsightClock.isoNow()),
            ])
        }
        _ = await liveTransport.sendControlRequest(action: PairingControlActions.appState, payload: payload)
    }

    private func sendMetricChannelDefinitions(channelIds: Set<Int>? = nil) async -> OperationResult {
        let state = lock.withLock { () -> (ids: Set<Int>, payload: JSONValue?) in
            let selectedChannels = channels.values
                .filter { channelIds?.contains($0.id) ?? true }
                .filter { !announcedMetricChannelIds.contains($0.id) }
                .sorted { $0.id < $1.id }

            guard !selectedChannels.isEmpty else {
                return ([], nil)
            }

            let payload = JSONValue.object([
                "source": .string("client"),
                "type": .string("CLIENT_METRIC_CHANNELS"),
                "sentAtUtc": .string(AnsightClock.isoNow()),
                "channels": .array(selectedChannels.map { channel in
                    .object([
                        "id": .integer(Int64(channel.id)),
                        "name": .string(channel.name),
                        "unit": channel.unit.map(JSONValue.string) ?? .null,
                        "type": .string(channel.type),
                        "color": channel.color.map(JSONValue.string) ?? .null,
                        "source": channel.source.map(JSONValue.string) ?? .null,
                        "group": channel.group.map(JSONValue.string) ?? .null,
                        "kind": channel.kind.map(JSONValue.string) ?? .null,
                    ])
                }),
            ])
            return (Set(selectedChannels.map(\.id)), payload)
        }

        guard let payload = state.payload else {
            return .success("Metric channels already announced.")
        }

        let result: OperationResult
        do {
            result = await liveTransport.sendText(try payload.jsonString())
        } catch {
            result = .failure("Failed to encode metric channel definitions: \(error.localizedDescription)")
        }

        if result.success {
            lock.withLock {
                announcedMetricChannelIds.formUnion(state.ids)
            }
        }

        return result
    }

    private func streamPendingTelemetry() {
        guard liveTransport.isOpen,
              lock.withLock({ initialized && active && sessionOpen })
        else {
            return
        }

        let shouldStart = lock.withLock { () -> Bool in
            guard !telemetryStreamLoopActive else {
                return false
            }

            telemetryStreamLoopActive = true
            return true
        }

        guard shouldStart else {
            return
        }

        Task { [weak self] in
            await self?.runTelemetryStreamLoop()
        }
    }

    private func runTelemetryStreamLoop() async {
        defer {
            let shouldRestart = lock.withLock { () -> Bool in
                telemetryStreamLoopActive = false
                return liveTransport.isOpen &&
                    initialized &&
                    active &&
                    sessionOpen &&
                    ((metrics.last?.sequence ?? 0) > lastStreamedMetricSequence ||
                        (events.last?.sequence ?? 0) > lastStreamedEventSequence)
            }

            if shouldRestart {
                streamPendingTelemetry()
            }
        }

        while liveTransport.isOpen && lock.withLock({ initialized && active && sessionOpen }) {
            let batch = lock.withLock { () -> (metrics: [RecordedMetric], events: [RecordedEvent]) in
                let metricSequence = lastStreamedMetricSequence
                let eventSequence = lastStreamedEventSequence
                let newMetrics = Array(metrics.lazy.filter { $0.sequence > metricSequence }.prefix(160))
                let newEvents = Array(events.lazy.filter { $0.sequence > eventSequence }.prefix(160))
                return (newMetrics, newEvents)
            }

            guard !batch.metrics.isEmpty || !batch.events.isEmpty else {
                return
            }

            let channelResult = await sendMetricChannelDefinitions(channelIds: Set(batch.metrics.map(\.channel)))
            guard channelResult.success else {
                lock.withLock {
                    sessionMessage = channelResult.message
                }
                return
            }

            if !batch.metrics.isEmpty {
                let result = await sendTelemetryPayload(Self.makeMetricsPayload(batch.metrics))
                guard result.success else {
                    lock.withLock {
                        sessionMessage = result.message
                    }
                    return
                }

                lock.withLock {
                    if let lastSequence = batch.metrics.last?.sequence {
                        lastStreamedMetricSequence = max(lastStreamedMetricSequence, lastSequence)
                    }
                }
            }

            if !batch.events.isEmpty {
                let result = await sendTelemetryPayload(Self.makeEventsPayload(batch.events))
                guard result.success else {
                    lock.withLock {
                        sessionMessage = result.message
                    }
                    return
                }

                lock.withLock {
                    if let lastSequence = batch.events.last?.sequence {
                        lastStreamedEventSequence = max(lastStreamedEventSequence, lastSequence)
                    }
                }
            }
        }
    }

    private func sendTelemetryPayload(_ payload: JSONValue) async -> OperationResult {
        do {
            return await liveTransport.sendText(try payload.jsonString())
        } catch {
            return .failure("Failed to encode telemetry payload: \(error.localizedDescription)")
        }
    }

    private func resolveConnectionRequestsForConnect(_ request: HostConnectionRequest) async throws -> [ResolvedConnectionRequest] {
        switch request.kind {
        case .file, .qrCode:
            try lock.withLock {
                guard initialized else {
                    throw RuntimeError.notInitialized("AnsightRuntime must be initialized before connecting to a host.")
                }
                guard active else {
                    throw RuntimeError.invalidInput("AnsightRuntime must be active before connecting to a host.")
                }
            }

            let reader = lock.withLock { hostConnectionConfigReader }
            guard let reader, reader.canRead(request.kind) else {
                throw RuntimeError.invalidInput("No host config reader is registered for \(request.kind.rawValue).")
            }

            let payload = try await reader.readConfigPayload(for: request)
            guard let payload, !payload.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
                throw PairingDocumentError.invalidDocument("No enrollment invite was provided.")
            }

            return [try resolvedRequest(
                fromPayload: payload,
                source: .configReader
            )]
        default:
            return try resolveConnectionRequests(request)
        }
    }

    private func resolveConnectionRequest(_ request: HostConnectionRequest) throws -> ResolvedConnectionRequest {
        guard let resolvedRequest = try resolveConnectionRequests(request).first else {
            throw RuntimeError.invalidInput("No Studio registration is available.")
        }
        return resolvedRequest
    }

    private func resolveConnectionRequests(_ request: HostConnectionRequest) throws -> [ResolvedConnectionRequest] {
        try lock.withLock {
            guard initialized else {
                throw RuntimeError.notInitialized("AnsightRuntime must be initialized before connecting to a host.")
            }
            guard active else {
                throw RuntimeError.invalidInput("AnsightRuntime must be active before connecting to a host.")
            }
        }

        switch request.kind {
        case .auto:
            var resolvedRequests: [ResolvedConnectionRequest] = []
            let unattendedProvisioning = lock.withLock {
                options.hostConnection.allowUnattendedProvisioning
            }
            if let payload = AnsightUnattendedProvisioning.payload(enabled: unattendedProvisioning) {
                resolvedRequests.append(try resolvedRequest(
                    fromPayload: payload,
                    source: .payload
                ))
            }
            if let localHostAddress = lock.withLock({ connector.localHostAddress }) {
                let bundleAppId = Bundle.main.bundleIdentifier?
                    .trimmingCharacters(in: .whitespacesAndNewlines)
                let appId = bundleAppId?.isEmpty == false
                    ? bundleAppId!
                    : ProcessInfo.processInfo.processName
                let configuredDiscoveryPort = lock.withLock {
                    options.hostConnection.discoveryPort
                }
                let discoveryPorts = configuredDiscoveryPort.map { [$0] }
                    ?? PairingProtocolDefaults.localDiscoveryPorts
                resolvedRequests.append(contentsOf: discoveryPorts.map { discoveryPort in
                    ResolvedConnectionRequest(
                        document: LocalPairingDocumentFactory.create(
                            appId: appId,
                            appName: resolveClientName(request.clientName),
                            hostAddress: localHostAddress,
                            discoveryPort: discoveryPort
                        ),
                        source: .autoProbe
                    )
                })
            }
            resolvedRequests.append(contentsOf: cachedPairingProfiles())
            if let savedJson = savedPairingStore.load(), !savedJson.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                do {
                    resolvedRequests.append(try resolvedRequest(
                        fromPayload: savedJson,
                        source: .savedConfig
                    ))
                } catch {
                    savedPairingStore.clear()
                    AnsightLogger.warning(
                        "Saved Studio registration is invalid and was cleared. Scan a fresh enrollment QR code.",
                        error: error
                    )
                }
            }
            if let bundled = lock.withLock({ options.hostConnection.bundledConfigJson }) {
                resolvedRequests.append(try resolvedRequest(
                    fromPayload: bundled,
                    source: .bundledConfig
                ))
            }
            guard !resolvedRequests.isEmpty else {
                throw RuntimeError.invalidInput("No Studio registration is available. Scan an enrollment QR in Ansight Studio.")
            }
            return resolvedRequests
        case .savedConfig:
            guard let savedJson = savedPairingStore.load(), !savedJson.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
                throw PairingDocumentError.invalidDocument("No saved Studio registration is available.")
            }
            return [try resolvedRequest(fromPayload: savedJson, source: .savedConfig)]
        case .bundledConfig:
            if let bundled = lock.withLock({ options.hostConnection.bundledConfigJson }) {
                return [try resolvedRequest(fromPayload: bundled, source: .bundledConfig)]
            }
            throw PairingDocumentError.invalidDocument("No bundled enrollment invite is available.")
        case .payload:
            guard let payload = request.payload else {
                throw PairingDocumentError.invalidDocument("Pairing payload is required.")
            }
            return [try resolvedRequest(fromPayload: payload, source: .payload)]
        case .config:
            guard let config = request.config else {
                throw PairingDocumentError.invalidDocument("Pairing config value is required.")
            }
            return [ResolvedConnectionRequest(
                document: ParsedPairingDocument(config: config.config, discoveryHint: config.discovery),
                source: .configReader
            )]
        case .file, .qrCode:
            throw RuntimeError.invalidInput("File and QR config readers are resolved asynchronously by connect(...).")
        }
    }

    private func resolvedRequest(
        fromPayload payload: String,
        source: HostConnectionSource
    ) throws -> ResolvedConnectionRequest {
        ResolvedConnectionRequest(
            document: try pairingDocumentService.parseDocument(payload),
            source: source
        )
    }

    private func validatePinnedHostIdentity(for document: ParsedPairingDocument, source: HostConnectionSource) throws {
        // Enrollment access is bound by Studio to the app-local device identity.
        // A fresh QR can intentionally move an app instance to another Studio host.
    }

    private func savePairingDocument(_ document: ParsedPairingDocument, connectedHostAddress: String?, connectResponse: ConnectResponse) {
        var discovery = document.discoveryHint ?? PairingDiscoveryHint(source: "live-session")
        discovery.source = discovery.source ?? "live-session"
        if let connectedHostAddress {
            discovery.hostAddresses = [connectedHostAddress]
        }
        discovery.hostName = connectResponse.hostName
        discovery.wifiName = connectResponse.hostWifiName
        discovery.capturedAt = AnsightClock.isoNow()

        let configDocument = PairingConfigDocument(config: document.config, discovery: discovery)
        if let data = try? JSONEncoder.ansightEncoder.encode(configDocument),
           let json = String(data: data, encoding: .utf8) {
            try? savedPairingStore.save(json)
        }
        saveCachedPairingProfile(configDocument)
    }

    private func saveCachedPairingProfile(_ document: PairingConfigDocument) {
        let cachedAt = Date()
        let expiresAt = cachedAt.addingTimeInterval(
            Double(lock.withLock { options.hostConnection.connectionProfileRetentionSeconds })
        )
        let networkKey = Self.cachedPairingNetworkKey(for: document)
        let profile = CachedPairingProfileDocument(
            networkKey: networkKey,
            wifiName: Self.normalizedCacheString(document.discovery?.wifiName),
            hostName: Self.normalizedCacheString(document.discovery?.hostName),
            cachedAtUtc: AnsightClock.isoString(from: cachedAt),
            expiresAtUtc: AnsightClock.isoString(from: expiresAt),
            document: document
        )

        var profiles = cachedPairingProfileDocuments()
        profiles.removeAll { existing in
            Self.cachedPairingNetworkKey(for: existing) == networkKey
        }
        profiles.append(profile)
        saveCachedPairingProfileDocuments(profiles)
    }

    private func requestExpectedAppId(
        for document: ParsedPairingDocument,
        request: HostConnectionRequest
    ) -> String? {
        let expectedAppId = request.expectedAppId?.trimmingCharacters(in: .whitespacesAndNewlines)
        if expectedAppId?.isEmpty == false {
            return expectedAppId
        }
        let bundleId = Bundle.main.bundleIdentifier?.trimmingCharacters(in: .whitespacesAndNewlines)
        return bundleId?.isEmpty == false ? bundleId : document.config.appId
    }

    private func resolveClientName(_ overrideClientName: String?) -> String {
        if let overrideClientName = overrideClientName?.trimmingCharacters(in: .whitespacesAndNewlines), !overrideClientName.isEmpty {
            return overrideClientName
        }
        if let configured = lock.withLock({ options.hostAutoProbe.clientName })?.trimmingCharacters(in: .whitespacesAndNewlines), !configured.isEmpty {
            return configured
        }
        if let appName = Bundle.main.object(forInfoDictionaryKey: "CFBundleDisplayName") as? String, !appName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            return appName
        }
        if let bundleName = Bundle.main.object(forInfoDictionaryKey: "CFBundleName") as? String, !bundleName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            return bundleName
        }
        return Bundle.main.bundleIdentifier ?? "Ansight App"
    }

    private func nextDeviceProfile() -> DeviceAppProfile {
        let seq = lock.withLock { () -> Int in
            profileSequence += 1
            return profileSequence
        }
        return AnsightDeviceAppProfileCollector.collect(reasonCode: 1, profileSeq: seq)
    }

    private func trimMetricsLocked() {
        let maxCount = options.maximumBufferSize
        if metrics.count > maxCount {
            metrics.removeFirst(metrics.count - maxCount)
        }
        trimOldSamplesLocked()
    }

    private func trimEventsLocked() {
        let maxCount = options.maximumBufferSize
        if events.count > maxCount {
            events.removeFirst(events.count - maxCount)
        }
        trimOldSamplesLocked()
    }

    private func trimOldSamplesLocked() {
        let cutoff = Date().addingTimeInterval(-Double(options.retentionPeriodSeconds))
        let cutoffIso = AnsightClock.isoString(from: cutoff)
        metrics.removeAll { $0.capturedAtUtc < cutoffIso }
        events.removeAll { $0.capturedAtUtc < cutoffIso }
    }

    private func publishHostConnectionStatusIfChanged(force: Bool = false) {
        let notification = lock.withLock { () -> HostConnectionStatusNotification? in
            let snapshot = hostConnectionSnapshotLocked()
            if !force,
               snapshot.status == lastPublishedHostConnectionStatus,
               snapshot.capabilities == lastPublishedHostConnectionCapabilities {
                return nil
            }

            lastPublishedHostConnectionStatus = snapshot.status
            lastPublishedHostConnectionCapabilities = snapshot.capabilities

            guard !hostConnectionStatusListeners.isEmpty else {
                return nil
            }

            return HostConnectionStatusNotification(
                listeners: Array(hostConnectionStatusListeners.values),
                status: snapshot.status,
                capabilities: snapshot.capabilities
            )
        }

        guard let notification else {
            return
        }

        for listener in notification.listeners {
            listener(notification.status, notification.capabilities)
        }
    }

    private func hostConnectionSnapshotLocked() -> HostConnectionSnapshot {
        let status = hostConnectionStatusLocked
        return HostConnectionSnapshot(
            status: status,
            capabilities: hostConnectionCapabilitiesLocked(status: status)
        )
    }

    private var hostConnectionStatusLocked: HostConnectionStatus {
        let saved = hasSavedConfigLocked
        let bundled = hasBundledConfigLocked
        let cached = hasCachedPairingProfileLocked
        let summary: HostConnectionSummaryKind
        let message: String

        let availableConfigCount = [saved, bundled, cached].filter { $0 }.count

        if !initialized {
            summary = .runtimeUnavailable
            message = "Runtime is not initialized."
        } else if !active {
            summary = .runtimeInactive
            message = "Runtime is inactive."
        } else if connectionState == .connecting {
            summary = .connecting
            message = "Connecting to Ansight host."
        } else if connectionState == .connected {
            summary = .connected
            message = hostName.map { "Connected to \($0)." } ?? "Connected to Ansight host."
        } else if availableConfigCount > 1 {
            summary = .disconnectedMultipleConfigsAvailable
            message = "Disconnected. Multiple Studio registrations are available."
        } else if saved {
            summary = .disconnectedSavedConfigAvailable
            message = "Disconnected. A saved Studio registration is available."
        } else if bundled {
            summary = .disconnectedBundledConfigAvailable
            message = "Disconnected. A bundled enrollment invite is available."
        } else if cached {
            summary = .disconnectedCachedSessionAvailable
            message = "Disconnected. A cached session is available."
        } else {
            summary = .disconnectedNoConfigs
            message = "Disconnected. Scan an enrollment QR in Ansight Studio."
        }

        return HostConnectionStatus(
            isRuntimeActive: active,
            isConnected: connectionState == .connected,
            connectionState: connectionState,
            hasCachedSession: cached,
            hasSavedConfig: saved,
            hasBundledConfig: bundled,
            summaryKind: summary,
            summaryMessage: message,
            hostId: hostId,
            hostName: hostName
        )
    }

    private var hasSavedConfigLocked: Bool {
        guard let saved = savedPairingStore.load() else {
            return false
        }
        return !saved.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
    }

    private var hasBundledConfigLocked: Bool {
        options.hostConnection.bundledConfigJson?
            .trimmingCharacters(in: .whitespacesAndNewlines)
            .isEmpty == false
    }

    private var hasCachedPairingProfileLocked: Bool {
        !cachedPairingProfiles().isEmpty
    }

    private var hasUnattendedProvisioningPayloadLocked: Bool {
        AnsightUnattendedProvisioning.payload(
            enabled: options.hostConnection.allowUnattendedProvisioning
        ) != nil
    }

    private var hostConnectionCapabilitiesLocked: HostConnectionCapabilities {
        let status = hostConnectionStatusLocked
        return hostConnectionCapabilitiesLocked(status: status)
    }

    private func hostConnectionCapabilitiesLocked(status: HostConnectionStatus) -> HostConnectionCapabilities {
        return HostConnectionCapabilities(
            canConnectUsingSavedConfig: initialized && status.hasSavedConfig,
            canConnectUsingBundledConfig: initialized && status.hasBundledConfig,
            canChooseConfigFile: initialized && (hostConnectionConfigReader?.canRead(.file) ?? false),
            canScanConfigQrCode: initialized && (hostConnectionConfigReader?.canRead(.qrCode) ?? false),
            canClearSavedConfigs: initialized && status.hasSavedConfig
        )
    }

    private struct HostConnectionSnapshot: Equatable {
        let status: HostConnectionStatus
        let capabilities: HostConnectionCapabilities
    }

    private struct HostConnectionStatusNotification {
        let listeners: [HostConnectionStatusListener]
        let status: HostConnectionStatus
        let capabilities: HostConnectionCapabilities
    }

    private struct ScreenCaptureSendResult {
        let operationResult: OperationResult
        let renderMilliseconds: Int?
        let frameWidth: Int?

        init(
            operationResult: OperationResult,
            renderMilliseconds: Int? = nil,
            frameWidth: Int? = nil
        ) {
            self.operationResult = operationResult
            self.renderMilliseconds = renderMilliseconds
            self.frameWidth = frameWidth
        }
    }

    private struct PreparedScreenFrame: Sendable {
        let frame: AnsightCapturedScreenFrame
        let payload: Data
        let captureStarted: TimeInterval
        let readyAt: TimeInterval
        let renderMilliseconds: Int
        let encodeMilliseconds: Int
        let visualTrees: [JSONValue]
    }

    private static func sessionVisualTreeEvent(
        payload: JSONValue,
        capturedAtUtc: String,
        screenshotCapturedAtUtc: String?,
        trigger: AnsightTouchVisualTreeCaptureTrigger?
    ) -> JSONValue {
        guard case .object(var payloadObject) = payload else {
            var event: [String: JSONValue] = [
                "type": .string("CLIENT_VISUAL_TREE"),
                "snapshotId": .string("stream-\(UUID().uuidString)"),
                "capturedAtUtc": .string(capturedAtUtc),
                "visualTreeKind": .string("unknown"),
                "source": .string(trigger == nil ? "sdk.sessionCapture" : "sdk.touchCapture"),
                "nodeCount": .integer(0),
                "truncated": .bool(false),
                "payload": payload,
            ]
            if let screenshotCapturedAtUtc {
                event["screenshotCapturedAtUtc"] = .string(screenshotCapturedAtUtc)
            }
            addTouchTrigger(trigger, to: &event)
            return .object(event)
        }

        payloadObject["capturedAtUtc"] = .string(capturedAtUtc)
        if let trigger {
            payloadObject["captureTrigger"] = touchTriggerPayload(trigger)
        }
        let source = jsonString(payloadObject["source"]) ?? "native"
        let format = jsonString(payloadObject["format"] ?? payloadObject["schema"]) ?? "ansight.visual-tree.compact.v2"
        let platform = jsonString(payloadObject["platform"]) ?? "ios"
        let nodeCount = jsonInteger(payloadObject["nodeCount"]) ?? countVisualTreeNodes(.object(payloadObject))
        let truncated = jsonBoolean(payloadObject["truncated"]) ?? false

        var event: [String: JSONValue] = [
            "type": .string("CLIENT_VISUAL_TREE"),
            "snapshotId": .string("stream-\(UUID().uuidString)"),
            "capturedAtUtc": .string(capturedAtUtc),
            "visualTreeKind": .string(source),
            "visualTreeFormat": .string(format),
            "runtimePlatform": .string(platform),
            "source": .string(trigger == nil ? "sdk.sessionCapture" : "sdk.touchCapture"),
            "maxDepth": .integer(40),
            "includeProperties": .bool(true),
            "includeBindableProperties": .bool(false),
            "nodeCount": .integer(Int64(nodeCount)),
            "truncated": .bool(truncated),
            "payload": .object(payloadObject),
        ]
        if let screenshotCapturedAtUtc {
            event["screenshotCapturedAtUtc"] = .string(screenshotCapturedAtUtc)
        }
        addTouchTrigger(trigger, to: &event)
        return .object(event)
    }

    private static func addTouchTrigger(
        _ trigger: AnsightTouchVisualTreeCaptureTrigger?,
        to event: inout [String: JSONValue]
    ) {
        guard let trigger else {
            return
        }
        event["captureTrigger"] = .string("touch")
        event["gestureId"] = .string(trigger.gestureId)
        event["gesturePhase"] = .string(trigger.gesturePhase.rawValue)
        event["touchAction"] = .string(trigger.touchAction)
        event["touchCapturedAtUtc"] = .string(trigger.touchCapturedAtUtc)
    }

    private static func touchTriggerPayload(
        _ trigger: AnsightTouchVisualTreeCaptureTrigger
    ) -> JSONValue {
        .object([
            "kind": .string("touch"),
            "gestureId": .string(trigger.gestureId),
            "gesturePhase": .string(trigger.gesturePhase.rawValue),
            "touchAction": .string(trigger.touchAction),
            "touchCapturedAtUtc": .string(trigger.touchCapturedAtUtc),
        ])
    }

    private static func jsonString(_ value: JSONValue?) -> String? {
        guard case .string(let value) = value else { return nil }
        return value
    }

    private static func jsonInteger(_ value: JSONValue?) -> Int? {
        guard case .integer(let value) = value else { return nil }
        return Int(exactly: value)
    }

    private static func jsonBoolean(_ value: JSONValue?) -> Bool? {
        guard case .bool(let value) = value else { return nil }
        return value
    }

    private static func countVisualTreeNodes(_ value: JSONValue) -> Int {
        guard case .object(let object) = value else { return 0 }
        if case .array(let nodes) = object["nodes"] {
            return nodes.count
        }
        if let root = object["root"] {
            return countVisualTreeNode(root)
        }
        return 0
    }

    private static func countVisualTreeNode(_ value: JSONValue) -> Int {
        guard case .object(let object) = value else { return 0 }
        guard case .array(let children) = object["children"] else { return 1 }
        return 1 + children.reduce(0) { $0 + countVisualTreeNode($1) }
    }

    private struct ScreenCapturePreparationResult {
        let operationResult: OperationResult
        let preparedFrame: PreparedScreenFrame?
        let renderMilliseconds: Int?
        let frameWidth: Int?

        init(
            operationResult: OperationResult,
            preparedFrame: PreparedScreenFrame? = nil,
            renderMilliseconds: Int? = nil,
            frameWidth: Int? = nil
        ) {
            self.operationResult = operationResult
            self.preparedFrame = preparedFrame
            self.renderMilliseconds = renderMilliseconds
            self.frameWidth = frameWidth
        }
    }

    private func cachedPairingProfile() -> ResolvedConnectionRequest? {
        cachedPairingProfiles().first
    }

    private func cachedPairingProfiles() -> [ResolvedConnectionRequest] {
        cachedPairingProfileDocuments().map { profile in
            ResolvedConnectionRequest(
                document: ParsedPairingDocument(
                    config: profile.document.config,
                    discoveryHint: profile.document.discovery
                ),
                source: .cachedSession
            )
        }
    }

    private func cachedPairingProfileDocuments() -> [CachedPairingProfileDocument] {
        guard let json = cachedPairingProfileStore.load(),
              let data = json.data(using: .utf8)
        else {
            return []
        }

        guard let collection = try? JSONDecoder.ansightDecoder.decode(
            CachedPairingProfileCollectionDocument.self,
            from: data
        ), collection.schema == CachedPairingProfileCollectionDocument.schemaName else {
            cachedPairingProfileStore.clear()
            return []
        }
        let decodedProfiles = collection.profiles

        let validProfiles = Self.sortedCachedPairingProfiles(
            decodedProfiles.compactMap { profile in
                guard profile.schema == CachedPairingProfileDocument.schemaName,
                      let expiresAt = AnsightClock.parseISO8601(profile.expiresAtUtc),
                      expiresAt > Date()
                else {
                    return nil
                }
                return normalizedCachedPairingProfile(profile)
            }
        )

        if validProfiles.count != decodedProfiles.count {
            saveCachedPairingProfileDocuments(validProfiles)
        }

        return validProfiles
    }

    private func saveCachedPairingProfileDocuments(_ profiles: [CachedPairingProfileDocument]) {
        let sortedProfiles = Self.sortedCachedPairingProfiles(profiles)
        guard !sortedProfiles.isEmpty else {
            cachedPairingProfileStore.clear()
            return
        }

        let document = CachedPairingProfileCollectionDocument(profiles: sortedProfiles)
        guard let data = try? JSONEncoder.ansightEncoder.encode(document),
              let json = String(data: data, encoding: .utf8)
        else {
            return
        }

        try? cachedPairingProfileStore.save(json)
    }

    private func normalizedCachedPairingProfile(_ profile: CachedPairingProfileDocument) -> CachedPairingProfileDocument {
        CachedPairingProfileDocument(
            networkKey: Self.cachedPairingNetworkKey(for: profile),
            wifiName: Self.normalizedCacheString(profile.wifiName ?? profile.document.discovery?.wifiName),
            hostName: Self.normalizedCacheString(profile.hostName ?? profile.document.discovery?.hostName),
            cachedAtUtc: profile.cachedAtUtc,
            expiresAtUtc: profile.expiresAtUtc,
            document: profile.document
        )
    }

    private static func sortedCachedPairingProfiles(
        _ profiles: [CachedPairingProfileDocument]
    ) -> [CachedPairingProfileDocument] {
        profiles.sorted { left, right in
            let leftDate = AnsightClock.parseISO8601(left.cachedAtUtc) ?? .distantPast
            let rightDate = AnsightClock.parseISO8601(right.cachedAtUtc) ?? .distantPast
            if leftDate != rightDate {
                return leftDate > rightDate
            }
            return cachedPairingNetworkKey(for: left).localizedCaseInsensitiveCompare(
                cachedPairingNetworkKey(for: right)
            ) == .orderedAscending
        }
    }

    private static func cachedPairingNetworkKey(for profile: CachedPairingProfileDocument) -> String {
        normalizedCacheString(profile.networkKey) ?? cachedPairingNetworkKey(for: profile.document)
    }

    private static func cachedPairingNetworkKey(for document: PairingConfigDocument) -> String {
        if let wifiName = normalizedCacheString(document.discovery?.wifiName) {
            return "wifi:\(wifiName)"
        }
        return "wifi:<unknown>"
    }

    private static func normalizedCacheString(_ value: String?) -> String? {
        let trimmed = value?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        return trimmed.isEmpty ? nil : trimmed
    }

    private static func cachedPairingProfileKey(for savedConfigKey: String) -> String {
        "\(savedConfigKey).cached-profile"
    }

    private func source(for kind: HostConnectionRequestKind) -> HostConnectionSource {
        switch kind {
        case .auto:
            return .autoProbe
        case .savedConfig:
            return .savedConfig
        case .bundledConfig:
            return .bundledConfig
        case .file, .qrCode:
            return .configReader
        case .payload:
            return .payload
        case .config:
            return .configReader
        }
    }

    private func reasonCode(for error: Error) -> String? {
        let message = error.localizedDescription
        if message.localizedCaseInsensitiveContains("expired") {
            return PairingFailureCodes.registrationExpired
        }
        if message.localizedCaseInsensitiveContains("does not match expected app id") {
            return PairingFailureCodes.enrollmentRequired
        }
        if message.contains("No host config reader") ||
            message.contains("File and QR config readers") {
            return PairingFailureCodes.unsupportedSource
        }
        if message.contains("No saved Studio registration") {
            return PairingFailureCodes.noSavedConfig
        }
        if message.contains("No bundled enrollment invite") {
            return PairingFailureCodes.noBundledConfig
        }
        if message.contains("Saved host identity") {
            return PairingFailureCodes.hostIdentityMismatch
        }
        if message.contains("File and QR") {
            return PairingFailureCodes.unsupportedSource
        }
        return nil
    }

    static func makeClientLogPayload(_ logLine: String) -> JSONValue {
        .object([
            "data": .string(logLine.trimmingCharacters(in: .whitespacesAndNewlines)),
        ])
    }

    static func makeSessionPropertiesPayload(_ customProperties: [String: [String: String]]) -> JSONValue {
        .object([
            "customProperties": .object(fromGrouped: normalizedCustomProperties(customProperties)),
            "updatedAtUtc": .string(AnsightClock.isoNow()),
        ])
    }

    static func makeSessionCompletePayload() -> JSONValue {
        .object([
            "reason": .string("client log stream complete"),
        ])
    }

    static func validatedMetricChannel(
        _ channel: AnsightChannel,
        allowReservedIds: Bool,
        allowUnspecified: Bool
    ) throws -> AnsightChannel {
        guard (0...255).contains(channel.id) else {
            throw RuntimeError.invalidInput("Channel ids must be between 0 and 255.")
        }

        if !allowReservedIds, AnsightChannels.reservedIds.contains(channel.id) {
            throw RuntimeError.invalidInput("Channel id \(channel.id) is reserved.")
        }

        if !allowUnspecified, channel.id == AnsightChannels.unspecified {
            throw RuntimeError.invalidInput("Channel id \(channel.id) is reserved.")
        }

        let name = channel.name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty else {
            throw RuntimeError.invalidInput("Channel name must not be blank.")
        }

        let color = channel.color?.trimmingCharacters(in: .whitespacesAndNewlines)
        let unit = channel.unit?.trimmingCharacters(in: .whitespacesAndNewlines)
        let type = channel.type.trimmingCharacters(in: .whitespacesAndNewlines)
        let source = channel.source?.trimmingCharacters(in: .whitespacesAndNewlines)
        let group = channel.group?.trimmingCharacters(in: .whitespacesAndNewlines)
        let kind = channel.kind?.trimmingCharacters(in: .whitespacesAndNewlines)

        return AnsightChannel(
            id: channel.id,
            name: name,
            color: color?.isEmpty == true ? nil : color,
            unit: unit?.isEmpty == true ? nil : unit,
            type: type.isEmpty ? "custom" : type,
            source: source?.isEmpty == true ? nil : source,
            group: group?.isEmpty == true ? nil : group,
            kind: kind?.isEmpty == true ? nil : kind
        )
    }

    static func makeMetricsPayload(_ metrics: [RecordedMetric]) -> JSONValue {
        .object([
            "source": .string("client"),
            "type": .string("CLIENT_METRICS"),
            "sentAtUtc": .string(AnsightClock.isoNow()),
            "metrics": .array(metrics.map { metric in
                .object([
                    "channel": .integer(Int64(metric.channel)),
                    "value": .integer(metric.value),
                    "capturedAtUtc": .string(metric.capturedAtUtc),
                ])
            }),
        ])
    }

    static func makeEventsPayload(_ events: [RecordedEvent]) -> JSONValue {
        .object([
            "source": .string("client"),
            "type": .string("CLIENT_EVENTS"),
            "sentAtUtc": .string(AnsightClock.isoNow()),
            "events": .array(events.map { event in
                .object([
                    "id": .string(event.id),
                    "label": .string(event.label),
                    "eventType": .string(event.type.wireName),
                    "details": event.details.map(JSONValue.string) ?? .null,
                    "capturedAtUtc": .string(event.capturedAtUtc),
                    "channel": .integer(Int64(event.channel)),
                ])
            }),
        ])
    }

    static func normalizedCustomProperties(_ customProperties: [String: [String: String]]) -> [String: [String: String]] {
        customProperties.reduce(into: [String: [String: String]]()) { result, groupEntry in
            let group = groupEntry.key.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !group.isEmpty else {
                return
            }

            let values = groupEntry.value.reduce(into: [String: String]()) { values, keyEntry in
                let key = keyEntry.key.trimmingCharacters(in: .whitespacesAndNewlines)
                guard !key.isEmpty else {
                    return
                }

                values[key] = keyEntry.value
            }

            if !values.isEmpty {
                result[group] = values
            }
        }
    }

    private static func makeChannelDictionary(options: AnsightOptions) -> [Int: AnsightChannel] {
        var result: [Int: AnsightChannel] = [:]
        if options.defaultMemoryChannels.contains(.managedHeap) {
            result[AnsightChannels.managedHeap] = AnsightChannels.managedHeapChannel
        }
        if options.defaultMemoryChannels.contains(.physicalFootprint) {
            result[AnsightChannels.physicalFootprint] = AnsightChannels.physicalFootprintChannel
        }
        if options.enableFramesPerSecond {
            result[AnsightChannels.framesPerSecond] = AnsightChannels.framesPerSecondChannel
        }
        if options.enableBatteryLevel {
            result[AnsightChannels.batteryLevel] = AnsightChannels.batteryLevelChannel
        }
        if options.enableOpenFileHandleTracking {
            result[AnsightChannels.openFileHandles] = AnsightChannels.openFileHandlesChannel
        }
        result[AnsightChannels.lifecycle] = AnsightChannels.lifecycleChannel
        result[AnsightChannels.unspecified] = AnsightChannels.unspecifiedChannel
        for channel in options.additionalChannels {
            result[channel.id] = channel
        }
        return result
    }

    private static func currentPhysicalFootprintBytes() -> Int64? {
        #if canImport(Darwin)
        var info = task_vm_info_data_t()
        var count = mach_msg_type_number_t(MemoryLayout<task_vm_info_data_t>.stride / MemoryLayout<natural_t>.stride)
        let result = withUnsafeMutablePointer(to: &info) { pointer in
            pointer.withMemoryRebound(to: integer_t.self, capacity: Int(count)) { rebound in
                task_info(mach_task_self_, task_flavor_t(TASK_VM_INFO), rebound, &count)
            }
        }
        guard result == KERN_SUCCESS else {
            return currentResidentMemoryBytes()
        }
        return Int64(info.phys_footprint)
        #else
        return nil
        #endif
    }

    private static func currentOpenFileHandleCount() -> Int64? {
        #if canImport(Darwin)
        guard let descriptors = try? FileManager.default.contentsOfDirectory(atPath: "/dev/fd") else {
            return nil
        }
        let descriptorCount = descriptors.lazy.compactMap(Int.init).filter { $0 >= 0 }.count
        return Int64(max(0, descriptorCount - 1))
        #else
        return nil
        #endif
    }

    private static func currentResidentMemoryBytes() -> Int64? {
        #if canImport(Darwin)
        var info = mach_task_basic_info()
        var count = mach_msg_type_number_t(MemoryLayout<mach_task_basic_info>.stride / MemoryLayout<natural_t>.stride)
        let result = withUnsafeMutablePointer(to: &info) { pointer in
            pointer.withMemoryRebound(to: integer_t.self, capacity: Int(count)) { rebound in
                task_info(mach_task_self_, task_flavor_t(MACH_TASK_BASIC_INFO), rebound, &count)
            }
        }
        guard result == KERN_SUCCESS else {
            return nil
        }
        return Int64(info.resident_size)
        #else
        return nil
        #endif
    }

    private static func currentBatteryLevelPercentage() -> Int? {
        #if canImport(UIKit)
        let device = UIDevice.current
        let previous = device.isBatteryMonitoringEnabled
        device.isBatteryMonitoringEnabled = true
        defer {
            if !previous {
                device.isBatteryMonitoringEnabled = false
            }
        }
        guard device.batteryLevel >= 0 else {
            return nil
        }
        return Int((device.batteryLevel * 100).rounded())
        #else
        return nil
        #endif
    }
}
