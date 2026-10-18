import Foundation

/// A timed operation (zone). Explicit parent IDs support nesting across async boundaries.
public final class AnsightSpan: @unchecked Sendable {
    public let id = UUID().uuidString
    public let name: String
    public let channel: Int
    public let parentSpanId: String?
    private let startedAtUtc = AnsightClock.isoNow()
    private let startedNanos = DispatchTime.now().uptimeNanoseconds
    private let lock = NSLock()
    private var ended = false
    private let emit: @Sendable (String, AnsightEventType, String, Int) throws -> Void

    init(name: String, channel: Int, parentSpanId: String?, emit: @escaping @Sendable (String, AnsightEventType, String, Int) throws -> Void) throws {
        guard !name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { throw RuntimeError.invalidInput("Span name must not be blank.") }
        self.name = name
        self.channel = channel
        self.parentSpanId = parentSpanId
        self.emit = emit
        try publish(phase: "start", durationMs: nil, status: nil)
    }

    public func end(status: String = "ok") throws {
        guard ["ok", "error", "cancelled"].contains(status) else { throw RuntimeError.invalidInput("Span status must be ok, error, or cancelled.") }
        let shouldEnd = lock.withLock { () -> Bool in
            if ended { return false }
            ended = true
            return true
        }
        if shouldEnd {
            try publish(phase: "end", durationMs: Double(DispatchTime.now().uptimeNanoseconds - startedNanos) / 1_000_000, status: status)
        }
    }

    private func publish(phase: String, durationMs: Double?, status: String?) throws {
        var fields: [String: Any] = ["spanId": id, "phase": phase, "startedAtUtc": startedAtUtc]
        fields["parentSpanId"] = parentSpanId
        fields["durationMs"] = durationMs
        fields["status"] = status
        let data = try JSONSerialization.data(withJSONObject: fields, options: [.sortedKeys])
        try emit(name, .span, String(decoding: data, as: UTF8.self), channel)
    }
}

extension AnsightRuntime {
    public func beginSpan(_ name: String, channel: Int = AnsightChannels.unspecified, parentSpanId: String? = nil) throws -> AnsightSpan {
        try AnsightSpan(name: name, channel: channel, parentSpanId: parentSpanId) { [self] label, type, details, id in
            try event(label, type: type, details: details, channel: id)
        }
    }

    public func measure<T>(_ name: String, channel: Int = AnsightChannels.unspecified, parentSpanId: String? = nil, operation: (AnsightSpan) throws -> T) throws -> T {
        let span = try beginSpan(name, channel: channel, parentSpanId: parentSpanId)
        do {
            let result = try operation(span)
            try span.end()
            return result
        } catch {
            try? span.end(status: error is CancellationError ? "cancelled" : "error")
            throw error
        }
    }

    public func measure<T>(_ name: String, channel: Int = AnsightChannels.unspecified, parentSpanId: String? = nil, operation: (AnsightSpan) async throws -> T) async throws -> T {
        let span = try beginSpan(name, channel: channel, parentSpanId: parentSpanId)
        do {
            let result = try await operation(span)
            try span.end()
            return result
        } catch {
            try? span.end(status: error is CancellationError ? "cancelled" : "error")
            throw error
        }
    }
}
