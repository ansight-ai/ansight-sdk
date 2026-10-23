import Foundation

/// An optional package's in-process setup, run during SDK initialization.
/// Plugins are not serialized in the native options protocol.
public final class AnsightPlugin: Sendable, Equatable {
    public let id: String
    private let initializeBody: @Sendable (AnsightRuntime) throws -> Void

    public init(id: String, initialize: @escaping @Sendable (AnsightRuntime) throws -> Void) {
        self.id = id
        self.initializeBody = initialize
    }

    public func initialize(runtime: AnsightRuntime) throws {
        try initializeBody(runtime)
    }

    public static func == (lhs: AnsightPlugin, rhs: AnsightPlugin) -> Bool { lhs === rhs }
}
