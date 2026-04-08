import Foundation

public final class AnsightRuntime: @unchecked Sendable {
    public static let shared = AnsightRuntime()

    private let lock = NSLock()
    private let pairingDocumentService = PairingConfigDocumentService()

    private var options = AnsightOptions()
    private var initialized = false
    private var active = false
    private var sessionOpen = false
    private var sessionMessage: String?
    private var metrics: [RecordedMetric] = []
    private var events: [RecordedEvent] = []
    private var tools: [String: RegisteredTool] = [:]
    private var lastPairingDocument: ParsedPairingDocument?
    private var resolvedHostAddress: String?

    private init() {}

    public func initialize(options: AnsightOptions = .init()) throws {
        let validatedOptions = try options.validated()

        lock.withLock {
            self.options = validatedOptions
            initialized = true
            sessionOpen = false
            lastPairingDocument = nil
            resolvedHostAddress = nil
            sessionMessage = "Runtime initialized."
        }
    }

    public func activate() throws {
        try lock.withLock {
            guard initialized else {
                throw RuntimeError.notInitialized("AnsightRuntime must be initialized before activation.")
            }

            active = true
            sessionMessage = "Runtime activated."
        }
    }

    public func deactivate() {
        lock.withLock {
            active = false
            sessionMessage = "Runtime deactivated."
        }
    }

    public func clear() {
        lock.withLock {
            metrics.removeAll()
            events.removeAll()
            lastPairingDocument = nil
            resolvedHostAddress = nil
            sessionMessage = "Runtime buffers cleared."
        }
    }

    public func metric(_ value: Int64, channel: Int = AnsightChannels.unspecified) throws {
        try lock.withLock {
            guard initialized else {
                throw RuntimeError.notInitialized("AnsightRuntime must be initialized before recording metrics.")
            }

            metrics.append(
                RecordedMetric(
                    value: value,
                    channel: try validateChannel(channel),
                    capturedAtEpochMs: Int64(Date().timeIntervalSince1970 * 1000)
                )
            )
            sessionMessage = "Recorded metric \(value)."
        }
    }

    public func event(
        _ label: String,
        type: AnsightEventType = .info,
        details: String? = nil,
        channel: Int = AnsightChannels.unspecified,
        id: String = UUID().uuidString
    ) throws {
        let trimmedLabel = label.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmedLabel.isEmpty else {
            throw RuntimeError.invalidInput("Event label must not be blank.")
        }

        try lock.withLock {
            guard initialized else {
                throw RuntimeError.notInitialized("AnsightRuntime must be initialized before recording events.")
            }

            events.append(
                RecordedEvent(
                    id: id,
                    label: trimmedLabel,
                    type: type,
                    details: details?.trimmingCharacters(in: .whitespacesAndNewlines),
                    channel: try validateChannel(channel),
                    capturedAtEpochMs: Int64(Date().timeIntervalSince1970 * 1000)
                )
            )
            sessionMessage = "Recorded event \(trimmedLabel)."
        }
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
                    message: "Pairing ticket JSON is required unless an embedded developer pairing ticket is available.",
                    sessionId: nil
                )
            }

            let document = try pairingDocumentService.parseAndValidateDocument(
                effectivePairingJson,
                expectedAppId: options.expectedAppId
            )

            let hintedHostAddress = document.discoveryHint?.hostAddresses?
                .first(where: { !$0.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty })?
                .trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
            guard !hintedHostAddress.isEmpty else {
                return OpenSessionResult(
                    success: false,
                    message: "Pairing ticket must include a discovery host hint.",
                    sessionId: nil,
                    configId: document.config.configId,
                    appId: document.config.appId,
                    usedEmbeddedDeveloperPairing: usedEmbeddedDeveloperPairing,
                    discoverySource: document.discoveryHint?.source
                )
            }

            sessionOpen = true
            let sessionId = "ios-\(UUID().uuidString)"
            lastPairingDocument = document
            resolvedHostAddress = hintedHostAddress
            sessionMessage =
                "Harness session opened locally for \(options.clientName) using config \(document.config.configId) at \(hintedHostAddress). Network transport is not implemented yet."
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

    public func completeSession() {
        lock.withLock {
            sessionOpen = false
            resolvedHostAddress = nil
            sessionMessage = "Harness session completed locally."
        }
    }

    public func closeSession() {
        lock.withLock {
            sessionOpen = false
            resolvedHostAddress = nil
            sessionMessage = "Harness session closed."
        }
    }

    public func registerTool(_ tool: AnsightToolDescriptor) throws {
        guard !tool.id.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw RuntimeError.invalidInput("Tool id must not be blank.")
        }

        lock.withLock {
            tools[tool.id] = RegisteredTool(descriptor: tool, execute: nil)
            sessionMessage = "Registered tool \(tool.id)."
        }
    }

    public func registerTool(_ tool: any AnsightTool) throws {
        let descriptor = tool.descriptor
        guard !descriptor.id.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw RuntimeError.invalidInput("Tool id must not be blank.")
        }

        lock.withLock {
            tools[descriptor.id] = RegisteredTool(
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
                sessionMessage: sessionMessage
            )
        }
    }

    public func currentOptions() -> AnsightOptions {
        lock.withLock { options }
    }

    private func validateChannel(_ channel: Int) throws -> Int {
        guard (0...255).contains(channel) else {
            throw RuntimeError.invalidInput("Channel ids must be between 0 and 255.")
        }

        return channel
    }
}

private extension NSLock {
    @discardableResult
    func withLock<T>(_ work: () throws -> T) rethrows -> T {
        lock()
        defer { unlock() }
        return try work()
    }
}

public enum RuntimeError: LocalizedError {
    case notInitialized(String)
    case invalidInput(String)

    public var errorDescription: String? {
        switch self {
        case .notInitialized(let message), .invalidInput(let message):
            return message
        }
    }
}
