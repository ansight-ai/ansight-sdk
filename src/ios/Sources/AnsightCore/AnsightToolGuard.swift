import Foundation

public struct AnsightToolGuard: Sendable, Codable, Equatable {
    /// No tools are allowed until an optional feature supplies an exact grant.
    /// Unlike an explicit `disabled`, this default permits such opt-ins.
    public static let unconfigured: Self = {
        var value = Self(discoveryEnabled: false, executionEnabled: false, maxPolicy: .read)
        value.permitsDefaultPolicy = false
        value.permitsScopedActivation = true
        return value
    }()

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

    public private(set) var discoveryEnabled: Bool
    public private(set) var executionEnabled: Bool
    public let maxPolicy: AnsightToolPolicy
    private var permitsDefaultPolicy = true
    private var permitsScopedActivation = false
    public private(set) var toolGrants: [String: AnsightToolPolicy] = [:]

    /// Authorizes one exact tool without changing the global policy or disabled switches.
    public func granting(_ toolId: String, through policy: AnsightToolPolicy) -> Self {
        precondition(!toolId.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
        var copy = self
        if copy.permitsScopedActivation {
            copy.discoveryEnabled = true
            copy.executionEnabled = true
        }
        copy.toolGrants[toolId.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()] = policy
        return copy
    }

    private enum CodingKeys: String, CodingKey {
        case discoveryEnabled, executionEnabled, maxPolicy, toolGrants, permitsDefaultPolicy, permitsScopedActivation
    }

    public init(from decoder: any Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        discoveryEnabled = try values.decode(Bool.self, forKey: .discoveryEnabled)
        executionEnabled = try values.decode(Bool.self, forKey: .executionEnabled)
        maxPolicy = try values.decode(AnsightToolPolicy.self, forKey: .maxPolicy)
        permitsDefaultPolicy = try values.decodeIfPresent(Bool.self, forKey: .permitsDefaultPolicy) ?? true
        permitsScopedActivation = try values.decodeIfPresent(Bool.self, forKey: .permitsScopedActivation) ?? false
        toolGrants = try values.decodeIfPresent([String: AnsightToolPolicy].self, forKey: .toolGrants) ?? [:]
    }

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
            return "Tool '\(descriptor.id)' is not authorized at policy '\(descriptor.policy.rawValue)' by the current guard."
        }

        return nil
    }

    private func isAllowed(_ descriptor: AnsightToolDescriptor) -> Bool {
        (permitsDefaultPolicy && descriptor.policy <= maxPolicy)
            || toolGrants[descriptor.id.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()].map { descriptor.policy <= $0 } == true
    }
}
