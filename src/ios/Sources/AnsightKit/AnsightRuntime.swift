import Foundation

public final class AnsightRuntime: @unchecked Sendable {
    public static let shared = AnsightRuntime()

    private let lock = NSLock()

    private var options = AnsightOptions()
    private var initialized = false
    private var active = false
    private var sessionOpen = false
    private var sessionMessage: String?
    private var metrics: [RecordedMetric] = []
    private var events: [RecordedEvent] = []
    private var tools: [String: AnsightToolDescriptor] = [:]

    private init() {}

    public func initialize(options: AnsightOptions = .init()) {
        lock.withLock {
            self.options = options
            initialized = true
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

            guard !pairingJson.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
                return OpenSessionResult(success: false, message: "Pairing JSON is required.", sessionId: nil)
            }

            guard !options.manualHostAddress.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
                return OpenSessionResult(success: false, message: "Manual host address is required.", sessionId: nil)
            }

            sessionOpen = true
            let sessionId = "ios-\(UUID().uuidString)"
            sessionMessage =
                "Harness session opened locally for \(options.clientName). Network transport is not implemented yet."
            return OpenSessionResult(success: true, message: sessionMessage ?? "Session opened.", sessionId: sessionId)
        }
    }

    public func completeSession() {
        lock.withLock {
            sessionOpen = false
            sessionMessage = "Harness session completed locally."
        }
    }

    public func closeSession() {
        lock.withLock {
            sessionOpen = false
            sessionMessage = "Harness session closed."
        }
    }

    public func registerTool(_ tool: AnsightToolDescriptor) throws {
        guard !tool.id.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw RuntimeError.invalidInput("Tool id must not be blank.")
        }

        lock.withLock {
            tools[tool.id] = tool
            sessionMessage = "Registered tool \(tool.id)."
        }
    }

    public func snapshot() -> AnsightDebugSnapshot {
        lock.withLock {
            AnsightDebugSnapshot(
                initialized: initialized,
                active: active,
                sessionOpen: sessionOpen,
                metricsRecorded: metrics.count,
                eventsRecorded: events.count,
                registeredTools: tools.count,
                lastMetric: metrics.last,
                lastEvent: events.last,
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
