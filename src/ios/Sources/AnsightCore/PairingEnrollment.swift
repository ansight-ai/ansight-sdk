import Foundation

public struct PairingEnrollment: Sendable, Codable, Equatable {
    public var ticketId: String
    public var secret: String
    public var expiresAt: String
    public var grantExpiresAt: String
    public var maxUses: Int
    public var maxScopes: [String]
    public var allowCritical: Bool

    public init(
        ticketId: String,
        secret: String,
        expiresAt: String,
        grantExpiresAt: String,
        maxUses: Int = 1,
        maxScopes: [String] = ["Read"],
        allowCritical: Bool = false
    ) {
        self.ticketId = ticketId
        self.secret = secret
        self.expiresAt = expiresAt
        self.grantExpiresAt = grantExpiresAt
        self.maxUses = maxUses
        self.maxScopes = maxScopes
        self.allowCritical = allowCritical
    }
}
