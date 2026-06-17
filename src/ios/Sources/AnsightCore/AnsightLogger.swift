import Foundation

public enum AnsightLogLevel: String, Sendable, Codable, CaseIterable {
    case debug
    case info
    case warning
    case error
}

public protocol AnsightLogCallback: AnyObject, Sendable {
    func log(level: AnsightLogLevel, message: String, error: Error?)
}

public final class AnsightClosureLogCallback: AnsightLogCallback, @unchecked Sendable {
    private let handler: @Sendable (AnsightLogLevel, String, Error?) -> Void

    public init(_ handler: @escaping @Sendable (AnsightLogLevel, String, Error?) -> Void) {
        self.handler = handler
    }

    public func log(level: AnsightLogLevel, message: String, error: Error?) {
        handler(level, message, error)
    }
}

public enum AnsightLogger {
    private static let lock = NSLock()
    nonisolated(unsafe) private static var callbacks: [ObjectIdentifier: any AnsightLogCallback] = [:]

    public static func registerCallback(_ callback: any AnsightLogCallback) {
        lock.withLock {
            callbacks[ObjectIdentifier(callback)] = callback
        }
    }

    public static func removeCallback(_ callback: any AnsightLogCallback) {
        lock.withLock {
            callbacks.removeValue(forKey: ObjectIdentifier(callback))
        }
    }

    public static func clearCallbacks() {
        lock.withLock {
            callbacks.removeAll()
        }
    }

    public static func debug(_ message: String) {
        emit(.debug, message)
    }

    public static func info(_ message: String) {
        emit(.info, message)
    }

    public static func warning(_ message: String, error: Error? = nil) {
        emit(.warning, message, error: error)
    }

    public static func error(_ message: String, error: Error? = nil) {
        emit(.error, message, error: error)
    }

    private static func emit(_ level: AnsightLogLevel, _ message: String, error: Error? = nil) {
        let normalized = message.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !normalized.isEmpty else {
            return
        }

        let snapshot = lock.withLock { Array(callbacks.values) }
        for callback in snapshot {
            callback.log(level: level, message: normalized, error: error)
        }
    }
}
