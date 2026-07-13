import CryptoKit
import Foundation

public struct PairingConfig: Sendable, Codable, Equatable {
    private enum CodingKeys: String, CodingKey {
        case schema
        case configId
        case appId
        case appName
        case issuedAt
        case expiresAt
        case oneTimeToken
        case host
        case challenge
        case minProtocolVersion
        case allowedTransports
        case enrollment
        case signatureAlgorithm
        case signature
    }

    public static let schemaName = "ansight.pairing-config.v1"
    public static let secureSchemaName = "ansight.pairing-config.v2"
    public static let secureRememberedProfileSchemaName = "ansight.pairing-profile.v2"

    public var schema: String
    public var configId: String
    public var appId: String
    public var appName: String
    public var issuedAt: String
    public var expiresAt: String
    public var oneTimeToken: String
    public var host: PairingHost
    public var challenge: PairingChallenge
    public var minProtocolVersion: Int?
    public var allowedTransports: [String]?
    public var enrollment: PairingEnrollment?
    public var signatureAlgorithm: String?
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
        minProtocolVersion = nil
        allowedTransports = nil
        enrollment = nil
        signatureAlgorithm = nil
        self.signature = signature
    }

    public init(
        schema: String = PairingConfig.secureSchemaName,
        configId: String,
        appId: String,
        appName: String,
        issuedAt: String,
        expiresAt: String,
        minProtocolVersion: Int = 2,
        allowedTransports: [String] = ["wss"],
        host: PairingHost,
        enrollment: PairingEnrollment,
        signatureAlgorithm: String = "ES256-P1363",
        signature: String
    ) {
        self.schema = schema
        self.configId = configId
        self.appId = appId
        self.appName = appName
        self.issuedAt = issuedAt
        self.expiresAt = expiresAt
        oneTimeToken = ""
        self.host = host
        challenge = PairingChallenge(alg: "", challengePubKey: "", requireProofOnFirstPair: false)
        self.minProtocolVersion = minProtocolVersion
        self.allowedTransports = allowedTransports
        self.enrollment = enrollment
        self.signatureAlgorithm = signatureAlgorithm
        self.signature = signature
    }

    public var isSecureV2: Bool {
        schema == Self.secureSchemaName || isSecureRememberedProfile
    }

    public var isSecureRememberedProfile: Bool {
        schema == Self.secureRememberedProfileSchemaName
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        schema = try container.decode(String.self, forKey: .schema)
        configId = try container.decode(String.self, forKey: .configId)
        appId = try container.decode(String.self, forKey: .appId)
        appName = try container.decode(String.self, forKey: .appName)
        issuedAt = try container.decode(String.self, forKey: .issuedAt)
        expiresAt = try container.decode(String.self, forKey: .expiresAt)
        oneTimeToken = try container.decodeIfPresent(String.self, forKey: .oneTimeToken) ?? ""
        host = try container.decode(PairingHost.self, forKey: .host)
        challenge = try container.decodeIfPresent(PairingChallenge.self, forKey: .challenge)
            ?? PairingChallenge(alg: "", challengePubKey: "", requireProofOnFirstPair: false)
        minProtocolVersion = try container.decodeIfPresent(Int.self, forKey: .minProtocolVersion)
        allowedTransports = try container.decodeIfPresent([String].self, forKey: .allowedTransports)
        enrollment = try container.decodeIfPresent(PairingEnrollment.self, forKey: .enrollment)
        signatureAlgorithm = try container.decodeIfPresent(String.self, forKey: .signatureAlgorithm)
        signature = try container.decode(String.self, forKey: .signature)
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        try container.encode(schema, forKey: .schema)
        try container.encode(configId, forKey: .configId)
        try container.encode(appId, forKey: .appId)
        try container.encode(appName, forKey: .appName)
        try container.encode(issuedAt, forKey: .issuedAt)
        try container.encode(expiresAt, forKey: .expiresAt)
        if isSecureV2 {
            try container.encode(minProtocolVersion, forKey: .minProtocolVersion)
            try container.encode(allowedTransports, forKey: .allowedTransports)
            try container.encodeIfPresent(enrollment, forKey: .enrollment)
            try container.encode(signatureAlgorithm, forKey: .signatureAlgorithm)
        } else {
            try container.encode(oneTimeToken, forKey: .oneTimeToken)
            try container.encode(challenge, forKey: .challenge)
        }
        try container.encode(host, forKey: .host)
        try container.encode(signature, forKey: .signature)
    }
}
