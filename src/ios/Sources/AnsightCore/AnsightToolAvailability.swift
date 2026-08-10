import Foundation

public struct AnsightToolAvailability: Sendable, Codable, Equatable {
    public let available: Bool
    public let reasonCode: String?
    public let reason: String?
    public let requiredState: String?
    public let remediation: String?
    public let retryable: Bool

    public init(
        available: Bool,
        reasonCode: String? = nil,
        reason: String? = nil,
        requiredState: String? = nil,
        remediation: String? = nil,
        retryable: Bool = false
    ) {
        self.available = available
        self.reasonCode = reasonCode
        self.reason = reason
        self.requiredState = requiredState
        self.remediation = remediation
        self.retryable = retryable
    }

    public static let availableNow = AnsightToolAvailability(available: true)

    public static func unavailable(
        reasonCode: String,
        reason: String,
        requiredState: String? = nil,
        remediation: String? = nil,
        retryable: Bool = true
    ) -> AnsightToolAvailability {
        AnsightToolAvailability(
            available: false,
            reasonCode: reasonCode,
            reason: reason,
            requiredState: requiredState,
            remediation: remediation,
            retryable: retryable
        )
    }

    internal var jsonValue: JSONValue {
        .object([
            "available": .bool(available),
            "reasonCode": reasonCode.map(JSONValue.string) ?? .null,
            "reason": reason.map(JSONValue.string) ?? .null,
            "requiredState": requiredState.map(JSONValue.string) ?? .null,
            "remediation": remediation.map(JSONValue.string) ?? .null,
            "retryable": .bool(retryable),
            "evaluatedAtUtc": .string(ISO8601DateFormatter().string(from: Date())),
        ])
    }
}

public struct AnsightToolAvailabilityContext: Sendable, Equatable {
    public let sessionId: String?
    public let requestId: String?

    public init(sessionId: String?, requestId: String?) {
        self.sessionId = sessionId
        self.requestId = requestId
    }
}
