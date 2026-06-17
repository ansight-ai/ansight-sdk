import Foundation

public struct AnsightToolGuard: Sendable, Codable, Equatable {
    public static let disabled = AnsightToolGuard(
        discoveryEnabled: false,
        executionEnabled: false,
        allowedScopes: []
    )

    public static let readOnly = AnsightToolGuard(
        discoveryEnabled: true,
        executionEnabled: true,
        allowedScopes: [.read]
    )

    public static let readWrite = AnsightToolGuard(
        discoveryEnabled: true,
        executionEnabled: true,
        allowedScopes: [.read, .write]
    )

    public static let fullAccess = AnsightToolGuard(
        discoveryEnabled: true,
        executionEnabled: true,
        allowedScopes: AnsightToolScope.allCases
    )

    public let discoveryEnabled: Bool
    public let executionEnabled: Bool
    public let allowedScopes: [AnsightToolScope]

    public init(
        discoveryEnabled: Bool,
        executionEnabled: Bool,
        allowedScopes: [AnsightToolScope]
    ) {
        self.discoveryEnabled = discoveryEnabled
        self.executionEnabled = executionEnabled
        self.allowedScopes = allowedScopes
    }

    public func validate() throws {
        if executionEnabled && allowedScopes.isEmpty {
            throw RuntimeError.invalidInput(
                "Tool execution cannot be enabled without at least one allowed scope."
            )
        }
    }

    func isVisible(_ descriptor: AnsightToolDescriptor) -> Bool {
        discoveryEnabled && isAllowed(descriptor)
    }

    func executionDenialReason(for descriptor: AnsightToolDescriptor) -> String? {
        guard executionEnabled else {
            return "Tool execution is disabled by the current guard policy."
        }

        guard isAllowed(descriptor) else {
            return "Tool scope '\(descriptor.scope)' is not enabled by the current guard policy."
        }

        return nil
    }

    private func isAllowed(_ descriptor: AnsightToolDescriptor) -> Bool {
        guard let scope = descriptor.scopeValue else {
            return false
        }

        return allowedScopes.contains(scope)
    }
}
