import Foundation

public struct AnsightToolGuard: Sendable, Codable, Equatable {
    public static let disabled = AnsightToolGuard(
        discoveryEnabled: false,
        executionEnabled: false,
        allowedScopes: [],
        allowCritical: false
    )

    public static let readOnly = AnsightToolGuard(
        discoveryEnabled: true,
        executionEnabled: true,
        allowedScopes: [.read],
        allowCritical: false
    )

    public static let readWrite = AnsightToolGuard(
        discoveryEnabled: true,
        executionEnabled: true,
        allowedScopes: [.read, .write],
        allowCritical: false
    )

    public static let fullAccess = AnsightToolGuard(
        discoveryEnabled: true,
        executionEnabled: true,
        allowedScopes: AnsightToolScope.allCases,
        allowCritical: true
    )

    public let discoveryEnabled: Bool
    public let executionEnabled: Bool
    public let allowedScopes: [AnsightToolScope]
    public let allowCritical: Bool

    public init(
        discoveryEnabled: Bool,
        executionEnabled: Bool,
        allowedScopes: [AnsightToolScope],
        allowCritical: Bool = false
    ) {
        self.discoveryEnabled = discoveryEnabled
        self.executionEnabled = executionEnabled
        self.allowedScopes = allowedScopes
        self.allowCritical = allowCritical
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

    func constrained(allowedScopes grantScopes: [String], allowCritical grantAllowsCritical: Bool) -> AnsightToolGuard {
        let allowed = allowedScopes.filter { grantScopes.contains($0.rawValue) }
        return AnsightToolGuard(
            discoveryEnabled: discoveryEnabled,
            executionEnabled: executionEnabled && !allowed.isEmpty,
            allowedScopes: allowed,
            allowCritical: allowCritical && grantAllowsCritical
        )
    }

    private func isAllowed(_ descriptor: AnsightToolDescriptor) -> Bool {
        guard let scope = descriptor.scopeValue else {
            return false
        }

        if descriptor.security.level == .critical && !allowCritical {
            return false
        }

        return allowedScopes.contains(scope)
    }
}
