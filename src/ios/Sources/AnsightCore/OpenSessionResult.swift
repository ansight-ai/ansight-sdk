import Foundation

public struct OpenSessionResult: Sendable, Codable, Equatable {
    public let success: Bool
    public let accepted: Bool
    public let message: String
    public let sessionId: String?
    public let configId: String?
    public let appId: String?
    public let resolvedHostAddress: String?
    public let usedEmbeddedDeveloperPairing: Bool
    public let discoverySource: String?
    public let reasonCode: String?
    public let hostId: String?
    public let hostName: String?

    public init(
        success: Bool,
        accepted: Bool? = nil,
        message: String,
        sessionId: String?,
        configId: String? = nil,
        appId: String? = nil,
        resolvedHostAddress: String? = nil,
        usedEmbeddedDeveloperPairing: Bool = false,
        discoverySource: String? = nil,
        reasonCode: String? = nil,
        hostId: String? = nil,
        hostName: String? = nil
    ) {
        self.success = success
        self.accepted = accepted ?? success
        self.message = message
        self.sessionId = sessionId
        self.configId = configId
        self.appId = appId
        self.resolvedHostAddress = resolvedHostAddress
        self.usedEmbeddedDeveloperPairing = usedEmbeddedDeveloperPairing
        self.discoverySource = discoverySource
        self.reasonCode = reasonCode
        self.hostId = hostId
        self.hostName = hostName
    }
}
