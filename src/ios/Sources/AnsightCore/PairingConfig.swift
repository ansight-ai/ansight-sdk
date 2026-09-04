import Foundation

enum PairingEnrollmentModes {
    static let invite = "invite"
    static let local = "local"
    static let localConfigPrefix = "local:"
}

public struct PairingConfig: Sendable, Codable, Equatable {
    public static let schemaName = "ansight.enrollment-invite.v2"
    public static let anyAppId = "*"

    public var schema: String
    public var configId: String
    public var appId: String
    public var appName: String
    public var issuedAt: String
    public var expiresAt: String
    public var minProtocolVersion: Int
    public var allowedTransports: [String]
    public var host: PairingHost
    public var enrollment: PairingEnrollment

    public init(
        schema: String = PairingConfig.schemaName,
        configId: String,
        appId: String,
        appName: String,
        issuedAt: String,
        expiresAt: String,
        minProtocolVersion: Int = 2,
        allowedTransports: [String] = ["ws"],
        host: PairingHost,
        enrollment: PairingEnrollment
    ) {
        self.schema = schema
        self.configId = configId
        self.appId = appId
        self.appName = appName
        self.issuedAt = issuedAt
        self.expiresAt = expiresAt
        self.minProtocolVersion = minProtocolVersion
        self.allowedTransports = allowedTransports
        self.host = host
        self.enrollment = enrollment
    }

    private enum CodingKeys: String, CodingKey {
        case schema
        case configId = "inviteId"
        case appId
        case appName
        case issuedAt
        case expiresAt
        case minProtocolVersion
        case allowedTransports
        case host
        case enrollment
    }
}

enum LocalPairingDocumentFactory {
    static func create(
        appId: String,
        appName: String,
        hostAddress: String,
        discoveryPort: Int
    ) -> ParsedPairingDocument {
        let now = Date()
        let expiresAt = now.addingTimeInterval(10 * 365 * 24 * 60 * 60)
        let nowValue = AnsightClock.isoString(from: now)
        let expiresAtValue = AnsightClock.isoString(from: expiresAt)
        return ParsedPairingDocument(
            config: PairingConfig(
                configId: "\(PairingEnrollmentModes.localConfigPrefix)\(appId)",
                appId: appId,
                appName: appName,
                issuedAt: nowValue,
                expiresAt: expiresAtValue,
                host: PairingHost(
                    hostId: nil,
                    hostName: "Local Ansight host",
                    discoveryPort: discoveryPort
                ),
                enrollment: PairingEnrollment(
                    accessToken: PairingDeviceIdentity.resolveAccessToken(),
                    expiresAt: expiresAtValue,
                    grantExpiresAt: expiresAtValue,
                    maxToolPolicy: "write"
                )
            ),
            discoveryHint: PairingDiscoveryHint(
                source: "runtime-local",
                hostAddresses: [hostAddress],
                discoveryPort: discoveryPort,
                hostName: "Local Ansight host",
                capturedAt: nowValue
            )
        )
    }
}

public struct PairingEnrollment: Sendable, Codable, Equatable {
    public var accessToken: String
    public var expiresAt: String
    public var grantExpiresAt: String
    public var maxUses: Int
    public var maxToolPolicy: String

    public init(
        accessToken: String,
        expiresAt: String,
        grantExpiresAt: String,
        maxUses: Int = 1,
        maxToolPolicy: String = "read"
    ) {
        self.accessToken = accessToken
        self.expiresAt = expiresAt
        self.grantExpiresAt = grantExpiresAt
        self.maxUses = maxUses
        self.maxToolPolicy = maxToolPolicy
    }
}
