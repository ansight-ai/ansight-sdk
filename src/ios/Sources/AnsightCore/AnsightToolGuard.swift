import Foundation

public struct AnsightToolGuard: Sendable, Codable, Equatable {
    public static let disabled = AnsightToolGuard(
        discoveryEnabled: false,
        executionEnabled: false,
        maxPolicy: .read
    )

    public static let readOnly = AnsightToolGuard(
        discoveryEnabled: true,
        executionEnabled: true,
        maxPolicy: .read
    )

    public static let readWrite = AnsightToolGuard(
        discoveryEnabled: true,
        executionEnabled: true,
        maxPolicy: .write
    )

    public static let fullAccess = AnsightToolGuard(
        discoveryEnabled: true,
        executionEnabled: true,
        maxPolicy: .critical
    )

    public let discoveryEnabled: Bool
    public let executionEnabled: Bool
    public let maxPolicy: AnsightToolPolicy

    public init(
        discoveryEnabled: Bool,
        executionEnabled: Bool,
        maxPolicy: AnsightToolPolicy
    ) {
        self.discoveryEnabled = discoveryEnabled
        self.executionEnabled = executionEnabled
        self.maxPolicy = maxPolicy
    }

    public func validate() throws {
        // The enum guarantees that maxPolicy is valid.
    }

    func isVisible(_ descriptor: AnsightToolDescriptor) -> Bool {
        discoveryEnabled && isAllowed(descriptor)
    }

    func executionDenialReason(for descriptor: AnsightToolDescriptor) -> String? {
        guard executionEnabled else {
            return "Tool execution is disabled by the current guard policy."
        }

        guard isAllowed(descriptor) else {
            return "Tool policy '\(descriptor.policy.rawValue)' exceeds the current '\(maxPolicy.rawValue)' grant."
        }

        return nil
    }

    private func isAllowed(_ descriptor: AnsightToolDescriptor) -> Bool {
        return descriptor.policy <= maxPolicy
    }
}
