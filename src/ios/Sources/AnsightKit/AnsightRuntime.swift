import Foundation

#if canImport(Darwin)
import Darwin
#endif

#if canImport(UIKit)
import UIKit
#endif

public final class AnsightRuntime: @unchecked Sendable {
    public static let shared = AnsightRuntime()

    private let lock = NSLock()
    private let pairingDocumentService = PairingConfigDocumentService()
    private let connector = PairingSessionConnector()
    private let liveTransport = PairingLiveSessionTransport()

    private var savedPairingStore: any PairingConfigStore = KeychainPairingConfigStore(account: "ai.ansight.ios.saved-pairing")
    private var cachedPairingProfileStore: any PairingConfigStore = KeychainPairingConfigStore(account: "ai.ansight.ios.saved-pairing.cached-profile")
    private var options = AnsightOptions()
    private var initialized = false
    private var active = false
    private var sessionOpen = false
    private var sessionId: String?
    private var sessionMessage: String?
    private var metrics: [RecordedMetric] = []
    private var events: [RecordedEvent] = []
    private var channels: [Int: AnsightChannel] = [:]
    private var tools: [String: RegisteredTool] = [:]
    private var lastPairingDocument: ParsedPairingDocument?
    private var resolvedHostAddress: String?
    private var currentLifecycleState: AppLifecycleState = .unknown
    private var currentLifecycleChangedAtUtc: String?
    private var currentScreen: RecordedScreenView?
    private var connectionState: HostConnectionState = .disconnected
    private var hostId: String?
    private var hostName: String?
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
    private var screenCaptureTask: Task<Void, Never>?
    private let lifecycleObserver = AnsightLifecycleObserver()
    private var frameRateSampler: AnsightFrameRateSampler?
    private var touchCaptureSession: AnsightTouchCaptureSession?
    private var touchCaptureStreamer: AnsightTouchCaptureStreamer?
    private var screenCaptureGeneration = 0
    private var screenFramesCaptured = 0
    private var screenFramesSent = 0
    private var lastScreenCaptureMessage: String?
    private var lastFrameRate: Int?
    private var touchCaptureRuntimeEnabled = true
    private var touchesCaptured = 0
    private var touchesSent = 0
    private var lastTouchCaptureMessage: String?
    private var pendingBinaryTransfers: [String: AnsightPendingBinaryTransfer] = [:]

    private init() {}

    public func initialize(options: AnsightOptions = .init()) throws {
        lock.withLock {
            telemetryGeneration += 1
            active = false
            sessionOpen = false
            connectionState = .disconnected
        }
        telemetrySamplingTask?.cancel()
        telemetrySamplingTask = nil
        autoProbeTask?.cancel()
        autoProbeTask = nil
        stopLifecycleCapture()
        stopScreenCapture()
        stopFrameRateSampling()
        stopTouchCapture(message: "Touch capture stopped.")
        let validatedOptions = try options.validated()

        lock.withLock {
            self.options = validatedOptions
            self.savedPairingStore = KeychainPairingConfigStore(account: validatedOptions.hostConnection.savedConfigKey)
            self.cachedPairingProfileStore = KeychainPairingConfigStore(
                account: Self.cachedPairingProfileKey(for: validatedOptions.hostConnection.savedConfigKey)
            )
            self.channels = Self.makeChannelDictionary(options: validatedOptions)
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
            screenFramesCaptured = 0
            screenFramesSent = 0
            lastScreenCaptureMessage = nil
            lastFrameRate = nil
            touchCaptureRuntimeEnabled = validatedOptions.touchCapture != nil
            touchesCaptured = 0
            touchesSent = 0
            lastTouchCaptureMessage = nil
            pendingBinaryTransfers.removeAll()
            lastPairingDocument = nil
            resolvedHostAddress = nil
            hostId = nil
            hostName = nil
            connectionState = .disconnected
            sessionMessage = "Runtime initialized."
        }
    }

    public func initializeAndActivate(options: AnsightOptions = .init()) throws {
        try initialize(options: options)
        try activate()
    }

    public func activate() throws {
        let shouldStartAutoProbe = try lock.withLock {
            guard initialized else {
                throw RuntimeError.notInitialized("AnsightRuntime must be initialized before activation.")
            }

            guard !active else {
                return false
            }

            active = true
            sessionMessage = "Runtime activated."
            return options.hostAutoProbe.enabled && (hasSavedConfigLocked || hasBundledConfigLocked || hasCachedPairingProfileLocked)
        }

        startTelemetrySamplingIfNeeded()
        startLifecycleCaptureIfNeeded()
        startFrameRateSamplingIfNeeded()
        startTouchCaptureIfNeeded()
        if shouldStartAutoProbe {
            startAutoProbeIfNeeded()
        }
    }

    public func deactivate() {
        stopLifecycleCapture()
        stopScreenCapture(message: "Screen capture stopped.")
        stopFrameRateSampling()
        stopTouchCapture(message: "Touch capture stopped.")
        lock.withLock {
            telemetryGeneration += 1
        }
        telemetrySamplingTask?.cancel()
        telemetrySamplingTask = nil
        autoProbeTask?.cancel()
        autoProbeTask = nil

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
            pendingBinaryTransfers.removeAll()
            sessionMessage = "Runtime deactivated."
        }

        Task {
            await liveTransport.close(notify: false)
        }
    }

    public func clear() {
        lock.withLock {
            metrics.removeAll()
            events.removeAll()
            screenFramesCaptured = 0
            screenFramesSent = 0
            lastScreenCaptureMessage = nil
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
            resolvedHostAddress = nil
            pendingBinaryTransfers.removeAll()
            sessionMessage = "Runtime buffers cleared."
        }
    }

    public func clearSavedPairing() {
        savedPairingStore.clear()
        lock.withLock {
            sessionMessage = "Saved pairing config cleared."
        }
    }

    public func clearCachedSession() {
        cachedPairingProfileStore.clear()
        lock.withLock {
            sessionMessage = "Cached pairing session cleared."
        }
    }

    func replacePairingStoresForTesting(saved: any PairingConfigStore, cached: any PairingConfigStore) {
        lock.withLock {
            savedPairingStore = saved
            cachedPairingProfileStore = cached
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
        }
    }

    public func disableTouchCapture() {
        lock.withLock {
            touchCaptureRuntimeEnabled = false
        }
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

        guard lock.withLock({ initialized && active && sessionOpen && connectionState == .connected }),
              liveTransport.isOpen
        else {
            let message = "A connected live session is required before capturing a screen frame."
            lock.withLock {
                lastScreenCaptureMessage = message
            }
            return .failure(message)
        }

        do {
            let frame = try await AnsightScreenCapture.capture(options: captureOptions)
            let payload = SessionJpegWireProtocol.encode(frame)
            let result = await liveTransport.sendData(payload)
            let message = result.success
                ? "Captured and sent screen frame \(frame.width)x\(frame.height) (\(frame.jpegData.count) bytes)."
                : result.message

            lock.withLock {
                screenFramesCaptured += 1
                if result.success {
                    screenFramesSent += 1
                }
                lastScreenCaptureMessage = message
                sessionMessage = message
            }

            return OperationResult(success: result.success, message: message)
        } catch {
            let message = "Failed to capture screen frame: \(error.localizedDescription)"
            lock.withLock {
                lastScreenCaptureMessage = message
                sessionMessage = message
            }
            return .failure(message)
        }
    }

    public func registerMetricChannel(_ channel: AnsightChannel) throws {
        guard (0...255).contains(channel.id) else {
            throw RuntimeError.invalidInput("Channel ids must be between 0 and 255.")
        }
        guard !AnsightChannels.reservedIds.contains(channel.id) else {
            throw RuntimeError.invalidInput("Channel id \(channel.id) is reserved.")
        }
        guard !channel.name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw RuntimeError.invalidInput("Channel name must not be blank.")
        }

        lock.withLock {
            channels[channel.id] = channel
            sessionMessage = "Registered metric channel \(channel.id)."
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

            nextMetricSequence += 1
            metrics.append(
                RecordedMetric(
                    value: value,
                    channel: try validateChannel(channel),
                    sequence: nextMetricSequence
                )
            )
            trimMetricsLocked()
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

        streamPendingTelemetry()
    }

    public func screenViewed(_ name: String, details: [String: String] = [:]) throws {
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
                    channel: AnsightChannels.lifecycle,
                    capturedAtUtc: capturedAtUtc,
                    sequence: nextEventSequence
                )
            )
            trimEventsLocked()
            sessionMessage = "Recorded screen view \(trimmedName)."
        }

        streamPendingTelemetry()
    }

    public func setAppLifecycleState(_ state: AppLifecycleState, changedAtUtc: String = AnsightClock.isoNow()) {
        let shouldSend: Bool = lock.withLock {
            guard initialized, currentLifecycleState != state else {
                return false
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
            return sessionOpen
        }

        streamPendingTelemetry()

        if shouldSend {
            Task {
                _ = await liveTransport.sendControlRequest(
                    action: PairingControlActions.appState,
                    payload: .object([
                        "state": .string(state.rawValue),
                        "changedAtUtc": .string(changedAtUtc),
                    ])
                )
            }
        }
    }

    func recordFrameRateSample(_ framesPerSecond: Int) {
        let normalized = max(0, min(framesPerSecond, 1_000))
        lock.withLock {
            guard initialized, active, options.enableFramesPerSecond else {
                return
            }

            lastFrameRate = normalized
        }

        try? metric(Int64(normalized), channel: AnsightChannels.framesPerSecond)
    }

    func recordCapturedTouch(_ touch: AnsightCapturedTouch) {
        let streamer = lock.withLock { () -> AnsightTouchCaptureStreamer? in
            guard initialized,
                  active,
                  touchCaptureRuntimeEnabled,
                  options.touchCapture != nil
            else {
                return nil
            }

            touchesCaptured += 1
            lastTouchCaptureMessage = "Captured touch input."
            return touchCaptureStreamer
        }

        streamer?.record(touch)
    }

    public func connect(_ request: HostConnectionRequest = .auto()) async -> HostConnectionResult {
        let resolvedRequest: ResolvedConnectionRequest
        do {
            resolvedRequest = try resolveConnectionRequest(request)
        } catch {
            return HostConnectionResult(
                success: false,
                message: error.localizedDescription,
                kind: request.kind,
                source: source(for: request.kind),
                reasonCode: reasonCode(for: error)
            )
        }

        let expectedAppId = requestExpectedAppId(for: resolvedRequest.document)
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

        let clientName = resolveClientName(request.clientName)
        lock.withLock {
            connectionState = .connecting
            sessionMessage = "Connecting to Ansight host."
        }

        let connectionOptions = PairingConnectionOptions(
            hostAddressOverride: nil,
            discoveryPort: options.hostConnection.discoveryPort,
            deviceAppProfile: nextDeviceProfile(),
            customProperties: options.customProperties
        )
        let attempt = await connector.connect(
            document: resolvedRequest.document,
            clientName: clientName,
            options: connectionOptions
        )

        guard attempt.success, let webSocketURL = attempt.webSocketURL, let connectResponse = attempt.connectResponse else {
            lock.withLock {
                connectionState = .disconnected
                sessionOpen = false
                sessionId = nil
                lastStreamedMetricSequence = 0
                lastStreamedEventSequence = 0
                sessionMessage = attempt.message
            }

            let open = OpenSessionResult(
                success: false,
                accepted: attempt.accepted,
                message: attempt.message,
                sessionId: nil,
                configId: resolvedRequest.document.config.configId,
                appId: resolvedRequest.document.config.appId,
                resolvedHostAddress: attempt.hostAddress,
                usedEmbeddedDeveloperPairing: resolvedRequest.usedEmbeddedDeveloperPairing,
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

        do {
            try await liveTransport.attach(
                url: webSocketURL,
                toolMessageHandler: { [weak self] message in
                    try? self?.handleToolProtocolMessage(message)
                },
                toolResponseSentHandler: { [weak self] request, _ in
                    self?.startQueuedBinaryTransferIfNeeded(forToolProtocolMessage: request)
                },
                closeHandler: { [weak self] reason in
                    self?.handleLiveTransportClosed(reason: reason)
                }
            )
        } catch {
            lock.withLock {
                connectionState = .disconnected
                sessionOpen = false
                sessionId = nil
                lastStreamedMetricSequence = 0
                lastStreamedEventSequence = 0
                sessionMessage = error.localizedDescription
            }
            return HostConnectionResult(
                success: false,
                message: "WebSocket endpoint did not become reachable: \(error.localizedDescription)",
                kind: request.kind,
                source: .transport,
                reasonCode: PairingFailureCodes.webSocketEndpointUnreachable
            )
        }

        let sessionOpenResult = await sendSessionOpen(config: resolvedRequest.document.config, clientName: clientName)
        guard sessionOpenResult.success else {
            await liveTransport.close(notify: false)
            lock.withLock {
                connectionState = .disconnected
                sessionOpen = false
                sessionId = nil
                lastStreamedMetricSequence = 0
                lastStreamedEventSequence = 0
                sessionMessage = sessionOpenResult.message
            }
            return HostConnectionResult(
                success: false,
                message: sessionOpenResult.message,
                kind: request.kind,
                source: .transport,
                reasonCode: PairingFailureCodes.webSocketHandshakeFailed
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
            sessionMessage = attempt.message
        }
        streamPendingTelemetry()

        startScreenCaptureIfNeeded()
        savePairingDocument(resolvedRequest.document, connectedHostAddress: attempt.hostAddress, connectResponse: connectResponse)

        let open = OpenSessionResult(
            success: true,
            accepted: true,
            message: attempt.message,
            sessionId: newSessionId,
            configId: resolvedRequest.document.config.configId,
            appId: resolvedRequest.document.config.appId,
            resolvedHostAddress: attempt.hostAddress,
            usedEmbeddedDeveloperPairing: resolvedRequest.usedEmbeddedDeveloperPairing,
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

    public func disconnect() async -> HostConnectionResult {
        stopScreenCapture(message: "Screen capture stopped.")
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
            resolvedHostAddress = nil
            hostId = nil
            hostName = nil
            pendingBinaryTransfers.removeAll()
            sessionMessage = "Session disconnected."
        }
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
            pendingBinaryTransfers.removeAll()
            sessionMessage = reason

            return initialized &&
                active &&
                options.hostAutoProbe.enabled &&
                (hasSavedConfigLocked || hasBundledConfigLocked)
        }

        if shouldReconnect {
            startAutoProbeIfNeeded()
        }
    }

    public func openLiveSession(pairingJson: String, options: PairingOpenOptions) async throws -> OpenSessionResult {
        let request = HostConnectionRequest.payloadText(pairingJson, clientName: options.clientName)
        let result = await connect(request)
        return result.openSession ?? OpenSessionResult(
            success: result.success,
            message: result.message,
            sessionId: nil,
            reasonCode: result.reasonCode
        )
    }

    public func openSession(pairingJson: String, options: PairingOpenOptions) throws -> OpenSessionResult {
        try lock.withLock {
            guard initialized else {
                throw RuntimeError.notInitialized("AnsightRuntime must be initialized before opening a session.")
            }

            let trimmedPairingJson = pairingJson.trimmingCharacters(in: .whitespacesAndNewlines)
            let embeddedPairingJson = AnsightDeveloperMode.embeddedPairingJson
            let effectivePairingJson = trimmedPairingJson.isEmpty ? embeddedPairingJson ?? "" : pairingJson
            let usedEmbeddedDeveloperPairing = trimmedPairingJson.isEmpty && embeddedPairingJson != nil

            guard !effectivePairingJson.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
                return OpenSessionResult(
                    success: false,
                    message: "Pairing config JSON is required unless an embedded developer pairing config is available.",
                    sessionId: nil
                )
            }

            let document = try pairingDocumentService.parseAndValidateDocument(
                effectivePairingJson,
                expectedAppId: options.expectedAppId
            )

            let hintedHostAddress = options.hostAddressOverride?.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty == false
                ? options.hostAddressOverride?.trimmingCharacters(in: .whitespacesAndNewlines)
                : document.discoveryHint?.hostAddress?.trimmingCharacters(in: .whitespacesAndNewlines)
            guard let hintedHostAddress, !hintedHostAddress.isEmpty else {
                return OpenSessionResult(
                    success: false,
                    message: "Pairing config must include a discovery host hint.",
                    sessionId: nil,
                    configId: document.config.configId,
                    appId: document.config.appId,
                    usedEmbeddedDeveloperPairing: usedEmbeddedDeveloperPairing,
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
                usedEmbeddedDeveloperPairing: usedEmbeddedDeveloperPairing,
                discoverySource: document.discoveryHint?.source
            )
        }
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

    public func updateSessionProperties(_ customProperties: [String: [String: String]]) async -> OperationResult {
        let normalized = Self.normalizedCustomProperties(customProperties)
        lock.withLock {
            options.customProperties = normalized
        }

        guard liveTransport.isOpen else {
            lock.withLock {
                sessionMessage = "Session properties updated locally."
            }
            return .success("Session properties updated locally.")
        }

        let result = await liveTransport.sendControlRequest(
            action: PairingControlActions.sessionProperties,
            payload: Self.makeSessionPropertiesPayload(normalized),
            acknowledgementTimeoutSeconds: 10
        )

        lock.withLock {
            sessionMessage = result.success ? "Session properties updated." : result.message
        }
        return result.success ? .success("Session properties updated.") : result
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

        closeSession()
        return result
    }

    public func closeSession() {
        stopScreenCapture(message: "Screen capture stopped.")
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
            pendingBinaryTransfers.removeAll()
            sessionMessage = "Session closed."
        }

        Task {
            await liveTransport.close(notify: false)
        }
    }

    public func registerTool(_ tool: AnsightToolDescriptor) throws {
        let normalizedId = AnsightToolProtocolBridge.normalizedToolId(tool.id)
        guard !normalizedId.isEmpty else {
            throw RuntimeError.invalidInput("Tool id must not be blank.")
        }

        try lock.withLock {
            guard tools[normalizedId] == nil else {
                throw RuntimeError.invalidInput("A tool with id '\(tool.id)' has already been registered.")
            }

            tools[normalizedId] = RegisteredTool(descriptor: tool, execute: nil)
            sessionMessage = "Registered tool \(tool.id)."
        }
    }

    public func registerTool(_ tool: any AnsightTool) throws {
        let descriptor = tool.descriptor
        let normalizedId = AnsightToolProtocolBridge.normalizedToolId(descriptor.id)
        guard !normalizedId.isEmpty else {
            throw RuntimeError.invalidInput("Tool id must not be blank.")
        }

        try lock.withLock {
            guard tools[normalizedId] == nil else {
                throw RuntimeError.invalidInput("A tool with id '\(descriptor.id)' has already been registered.")
            }

            tools[normalizedId] = RegisteredTool(
                descriptor: descriptor,
                execute: tool.execute(arguments:)
            )
            sessionMessage = "Registered executable tool \(descriptor.id)."
        }
    }

    public func handleToolProtocolMessage(_ json: String) throws -> String? {
        let bridge = lock.withLock {
            AnsightToolProtocolBridge(registry: tools, guardPolicy: options.toolGuard)
        }

        guard initialized else {
            return bridge.runtimeNotInitializedResponse(for: json)
        }

        return try bridge.handleIfSupported(json)
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
        guard lock.withLock({ initialized && active }) else {
            return
        }

        let configuredChannels = lock.withLock { Set(channels.keys) }
        if configuredChannels.contains(AnsightChannels.physicalFootprint),
           let bytes = Self.currentResidentMemoryBytes() {
            try? metric(bytes, channel: AnsightChannels.physicalFootprint)
        }

        if configuredChannels.contains(AnsightChannels.batteryLevel),
           lock.withLock({ options.enableBatteryLevel }),
           let level = Self.currentBatteryLevelPercentage() {
            try? metric(Int64(level), channel: AnsightChannels.batteryLevel)
        }
    }

    public func hostConnectionStatus() -> HostConnectionStatus {
        lock.withLock { hostConnectionStatusLocked }
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
                embeddedDeveloperPairingAvailable: AnsightDeveloperMode.embeddedPairingJson != nil,
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
                  options.enableFramesPerSecond,
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

    private func startAutoProbeIfNeeded() {
        autoProbeTask?.cancel()
        let autoOptions = lock.withLock { options.hostAutoProbe }
        autoProbeTask = Task { [weak self] in
            guard let self else {
                return
            }
            try? await Task.sleep(nanoseconds: UInt64(autoOptions.initialDelayMilliseconds) * 1_000_000)
            guard !Task.isCancelled else {
                return
            }

            let result = await self.connect(.auto(clientName: autoOptions.clientName, sourceDescription: "auto-probe"))
            if !result.success {
                self.lock.withLock {
                    if self.active {
                        self.sessionMessage = result.message
                    }
                }
            }
        }
    }

    private func startScreenCaptureIfNeeded() {
        let state = lock.withLock { () -> (Int, AnsightSessionJpegCaptureOptions)? in
            guard initialized,
                  active,
                  sessionOpen,
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
        while !Task.isCancelled {
            guard liveTransport.isOpen,
                  lock.withLock({ active && sessionOpen && screenCaptureGeneration == generation })
            else {
                break
            }

            let result = await captureScreenFrame(options: options)
            if !result.success && !liveTransport.isOpen {
                break
            }

            try? await Task.sleep(nanoseconds: UInt64(options.intervalMilliseconds) * 1_000_000)
        }

        lock.withLock {
            if screenCaptureGeneration == generation {
                screenCaptureTask = nil
                lastScreenCaptureMessage = "Screen capture stopped."
            }
        }
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
            return await liveTransport.sendControlRequest(
                action: PairingControlActions.deviceProfile,
                payload: try .fromEncodable(profile)
            )
        } catch {
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
                        "color": channel.color.map(JSONValue.string) ?? .null,
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

    private func resolveConnectionRequest(_ request: HostConnectionRequest) throws -> ResolvedConnectionRequest {
        guard let resolvedRequest = try resolveConnectionRequests(request).first else {
            throw RuntimeError.invalidInput("No pairing config is available.")
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
            if let embedded = AnsightDeveloperMode.embeddedPairingJson {
                resolvedRequests.append(try resolvedRequest(
                    fromPayload: embedded,
                    source: .bundledDeveloperConfig,
                    usedEmbeddedDeveloperPairing: true
                ))
            }
            resolvedRequests.append(contentsOf: cachedPairingProfiles())
            if let savedJson = savedPairingStore.load(), !savedJson.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                resolvedRequests.append(try resolvedRequest(
                    fromPayload: savedJson,
                    source: .savedConfig,
                    usedEmbeddedDeveloperPairing: false
                ))
            }
            if let bundled = lock.withLock({ options.hostConnection.bundledConfigJson }) {
                resolvedRequests.append(try resolvedRequest(
                    fromPayload: bundled,
                    source: .bundledConfig,
                    usedEmbeddedDeveloperPairing: false
                ))
            }
            guard !resolvedRequests.isEmpty else {
                throw RuntimeError.invalidInput("No cached, saved, bundled, or developer pairing config is available.")
            }
            return resolvedRequests
        case .savedConfig:
            guard let savedJson = savedPairingStore.load(), !savedJson.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
                throw PairingDocumentError.invalidDocument("No saved pairing config is available.")
            }
            return [try resolvedRequest(fromPayload: savedJson, source: .savedConfig, usedEmbeddedDeveloperPairing: false)]
        case .bundledConfig:
            if let bundled = lock.withLock({ options.hostConnection.bundledConfigJson }) {
                return [try resolvedRequest(fromPayload: bundled, source: .bundledConfig, usedEmbeddedDeveloperPairing: false)]
            }
            if let embedded = AnsightDeveloperMode.embeddedPairingJson {
                return [try resolvedRequest(fromPayload: embedded, source: .bundledDeveloperConfig, usedEmbeddedDeveloperPairing: true)]
            }
            throw PairingDocumentError.invalidDocument("No bundled pairing config is available.")
        case .payload:
            guard let payload = request.payload else {
                throw PairingDocumentError.invalidDocument("Pairing payload is required.")
            }
            return [try resolvedRequest(fromPayload: payload, source: .payload, usedEmbeddedDeveloperPairing: false)]
        case .config:
            guard let config = request.config else {
                throw PairingDocumentError.invalidDocument("Pairing config value is required.")
            }
            return [ResolvedConnectionRequest(
                document: ParsedPairingDocument(config: config.config, discoveryHint: config.discovery),
                source: .configReader,
                usedEmbeddedDeveloperPairing: false
            )]
        case .file, .qrCode:
            throw RuntimeError.invalidInput("File and QR config readers are not configured in this first iOS pass.")
        }
    }

    private func resolvedRequest(
        fromPayload payload: String,
        source: HostConnectionSource,
        usedEmbeddedDeveloperPairing: Bool
    ) throws -> ResolvedConnectionRequest {
        ResolvedConnectionRequest(
            document: try pairingDocumentService.parseDocument(payload),
            source: source,
            usedEmbeddedDeveloperPairing: usedEmbeddedDeveloperPairing
        )
    }

    private func validatePinnedHostIdentity(for document: ParsedPairingDocument, source: HostConnectionSource) throws {
        guard source != .savedConfig,
              let savedJson = savedPairingStore.load(),
              let savedDocument = try? pairingDocumentService.parseDocument(savedJson),
              savedDocument.config.appId == document.config.appId
        else {
            return
        }

        let savedFingerprint = savedDocument.config.host.hostPubKeyFingerprint.trimmingCharacters(in: .whitespacesAndNewlines)
        let incomingFingerprint = document.config.host.hostPubKeyFingerprint.trimmingCharacters(in: .whitespacesAndNewlines)
        if !savedFingerprint.isEmpty, !incomingFingerprint.isEmpty, savedFingerprint != incomingFingerprint {
            throw PairingDocumentError.invalidDocument("Saved host identity does not match the incoming pairing config. Clear saved pairing before trusting a different host.")
        }
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

    private func requestExpectedAppId(for document: ParsedPairingDocument) -> String? {
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
            message = "Disconnected. Multiple pairing configs are available."
        } else if saved {
            summary = .disconnectedSavedConfigAvailable
            message = "Disconnected. A saved pairing config is available."
        } else if bundled {
            summary = .disconnectedBundledConfigAvailable
            message = "Disconnected. A bundled pairing config is available."
        } else if cached {
            summary = .disconnectedCachedSessionAvailable
            message = "Disconnected. A cached session is available."
        } else {
            summary = .disconnectedNoConfigs
            message = "Disconnected. No pairing config is available."
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
        if options.hostConnection.bundledConfigJson?.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty == false {
            return true
        }
        return AnsightDeveloperMode.embeddedPairingJson != nil
    }

    private var hasCachedPairingProfileLocked: Bool {
        !cachedPairingProfiles().isEmpty
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
                source: .cachedSession,
                usedEmbeddedDeveloperPairing: false
            )
        }
    }

    private func cachedPairingProfileDocuments() -> [CachedPairingProfileDocument] {
        guard let json = cachedPairingProfileStore.load(),
              let data = json.data(using: .utf8)
        else {
            return []
        }

        let decodedProfiles: [CachedPairingProfileDocument]
        let shouldRewriteForSchema: Bool
        if let collection = try? JSONDecoder.ansightDecoder.decode(
            CachedPairingProfileCollectionDocument.self,
            from: data
        ), collection.schema == CachedPairingProfileCollectionDocument.schemaName {
            decodedProfiles = collection.profiles
            shouldRewriteForSchema = false
        } else if let profile = try? JSONDecoder.ansightDecoder.decode(
            CachedPairingProfileDocument.self,
            from: data
        ), profile.schema == CachedPairingProfileDocument.schemaName {
            decodedProfiles = [profile]
            shouldRewriteForSchema = true
        } else {
            cachedPairingProfileStore.clear()
            return []
        }

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

        if shouldRewriteForSchema || validProfiles.count != decodedProfiles.count {
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
        if message.contains("No saved pairing config") {
            return PairingFailureCodes.noSavedConfig
        }
        if message.contains("No bundled pairing config") {
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
        result[AnsightChannels.lifecycle] = AnsightChannels.lifecycleChannel
        result[AnsightChannels.unspecified] = AnsightChannels.unspecifiedChannel
        for channel in options.additionalChannels {
            result[channel.id] = channel
        }
        return result
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
