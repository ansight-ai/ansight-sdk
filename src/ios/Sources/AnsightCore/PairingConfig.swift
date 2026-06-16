import CryptoKit
import Foundation

public struct PairingConfig: Sendable, Codable, Equatable {
    public static let schemaName = "ansight.pairing-config.v1"

    public var schema: String
    public var configId: String
    public var appId: String
    public var appName: String
    public var issuedAt: String
    public var expiresAt: String
    public var oneTimeToken: String
    public var host: PairingHost
    public var challenge: PairingChallenge
    public var trust: PairingTrust
    public var signature: String

    public init(
        schema: String = PairingConfig.schemaName,
        configId: String,
        appId: String,
        appName: String,
        issuedAt: String,
        expiresAt: String,
        oneTimeToken: String,
        host: PairingHost,
        challenge: PairingChallenge,
        trust: PairingTrust,
        signature: String
    ) {
        self.schema = schema
        self.configId = configId
        self.appId = appId
        self.appName = appName
        self.issuedAt = issuedAt
        self.expiresAt = expiresAt
        self.oneTimeToken = oneTimeToken
        self.host = host
        self.challenge = challenge
        self.trust = trust
        self.signature = signature
    }
}
