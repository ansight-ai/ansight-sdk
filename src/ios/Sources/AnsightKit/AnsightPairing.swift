import CryptoKit
import Foundation

public enum PairingProtocolDefaults {
    public static let discoveryPort = 45123
    public static let webSocketPort = 45124
    public static let webSocketPath = "/ws"
}

public struct PairingHost: Sendable, Codable, Equatable {
    public var hostId: String?
    public var hostName: String?
    public var discoveryPort: Int = PairingProtocolDefaults.discoveryPort
    public var hostPubKey: String
    public var hostPubKeyFingerprint: String

    public init(
        hostId: String? = nil,
        hostName: String? = nil,
        discoveryPort: Int = PairingProtocolDefaults.discoveryPort,
        hostPubKey: String,
        hostPubKeyFingerprint: String
    ) {
        self.hostId = hostId
        self.hostName = hostName
        self.discoveryPort = discoveryPort
        self.hostPubKey = hostPubKey
        self.hostPubKeyFingerprint = hostPubKeyFingerprint
    }
}

public struct PairingChallenge: Sendable, Codable, Equatable {
    public var alg: String
    public var challengePubKey: String
    public var requireProofOnFirstPair: Bool

    public init(alg: String, challengePubKey: String, requireProofOnFirstPair: Bool) {
        self.alg = alg
        self.challengePubKey = challengePubKey
        self.requireProofOnFirstPair = requireProofOnFirstPair
    }
}

public struct PairingTrust: Sendable, Codable, Equatable {
    public var mode: String
    public var requireTokenOnFirstPair: Bool
    public var allowLanDiscovery: Bool

    public init(mode: String, requireTokenOnFirstPair: Bool, allowLanDiscovery: Bool) {
        self.mode = mode
        self.requireTokenOnFirstPair = requireTokenOnFirstPair
        self.allowLanDiscovery = allowLanDiscovery
    }
}

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

public struct PairingDiscoveryHint: Sendable, Codable, Equatable {
    public static let schemaName = "ansight.discovery-hint.v1"

    public var schema: String
    public var source: String?
    public var hostAddress: String?
    public var hostName: String?
    public var wifiName: String?
    public var capturedAt: String?

    public init(
        schema: String = PairingDiscoveryHint.schemaName,
        source: String? = nil,
        hostAddress: String? = nil,
        hostName: String? = nil,
        wifiName: String? = nil,
        capturedAt: String? = nil
    ) {
        self.schema = schema
        self.source = source
        self.hostAddress = hostAddress
        self.hostName = hostName
        self.wifiName = wifiName
        self.capturedAt = capturedAt
    }
}

public struct PairingConnectionHint: Sendable, Codable, Equatable {
    public static let schemaName = "ansight.pairing-connection-hint.v1"

    public var schema: String
    public var source: String?
    public var configId: String
    public var issuedAt: String
    public var expiresAt: String
    public var oneTimeToken: String
    public var challenge: PairingChallenge

    public init(
        schema: String = PairingConnectionHint.schemaName,
        source: String? = nil,
        configId: String,
        issuedAt: String,
        expiresAt: String,
        oneTimeToken: String,
        challenge: PairingChallenge
    ) {
        self.schema = schema
        self.source = source
        self.configId = configId
        self.issuedAt = issuedAt
        self.expiresAt = expiresAt
        self.oneTimeToken = oneTimeToken
        self.challenge = challenge
    }
}

public struct PairingBootstrapDocument: Sendable, Codable, Equatable {
    public static let schemaName = "ansight.pairing-bootstrap.v1"

    public var schema: String
    public var pairingConfig: PairingConfig
    public var discovery: PairingDiscoveryHint?
    public var connectionHint: PairingConnectionHint?

    public init(
        schema: String = PairingBootstrapDocument.schemaName,
        pairingConfig: PairingConfig,
        discovery: PairingDiscoveryHint? = nil,
        connectionHint: PairingConnectionHint? = nil
    ) {
        self.schema = schema
        self.pairingConfig = pairingConfig
        self.discovery = discovery
        self.connectionHint = connectionHint
    }
}

public struct ParsedPairingDocument: Sendable, Codable, Equatable {
    public var config: PairingConfig
    public var discoveryHint: PairingDiscoveryHint?
    public var trustAnchorConfig: PairingConfig?
    public var connectionHint: PairingConnectionHint?

    public init(
        config: PairingConfig,
        discoveryHint: PairingDiscoveryHint? = nil,
        trustAnchorConfig: PairingConfig? = nil,
        connectionHint: PairingConnectionHint? = nil
    ) {
        self.config = config
        self.discoveryHint = discoveryHint
        self.trustAnchorConfig = trustAnchorConfig
        self.connectionHint = connectionHint
    }
}

public struct PairingConfigDocumentService: Sendable {
    public init() {}

    public func parseAndValidateDocument(
        _ configJson: String,
        expectedAppId: String? = nil
    ) throws -> ParsedPairingDocument {
        let document = try parseDocument(configJson)
        try validateDocument(document, expectedAppId: expectedAppId)
        return document
    }

    public func parseDocument(_ configJson: String) throws -> ParsedPairingDocument {
        let trimmedJson = configJson.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmedJson.isEmpty else {
            throw PairingDocumentError.invalidDocument("Paste or load a pairing config.")
        }

        let data = Data(trimmedJson.utf8)
        let rootObject = try decodeJSONObject(data)
        let schema = rootObject["schema"] as? String

        if schema == PairingBootstrapDocument.schemaName {
            let bootstrap = try JSONDecoder.ansightDecoder.decode(PairingBootstrapDocument.self, from: data)
            let effectiveConfig = bootstrap.connectionHint.map {
                applyConnectionHint(trustAnchorConfig: bootstrap.pairingConfig, connectionHint: $0)
            } ?? bootstrap.pairingConfig

            return ParsedPairingDocument(
                config: effectiveConfig,
                discoveryHint: bootstrap.discovery,
                trustAnchorConfig: bootstrap.connectionHint == nil ? nil : bootstrap.pairingConfig,
                connectionHint: bootstrap.connectionHint
            )
        }

        let config = try JSONDecoder.ansightDecoder.decode(PairingConfig.self, from: data)
        return ParsedPairingDocument(config: config)
    }

    public func validateDocument(_ document: ParsedPairingDocument, expectedAppId: String? = nil) throws {
        let trustAnchor = document.trustAnchorConfig ?? document.config
        guard verifyPairingConfigSignature(trustAnchor) else {
            throw PairingDocumentError.invalidDocument("Connection config signature is invalid.")
        }

        guard let expiresAt = parseTimestamp(document.config.expiresAt) else {
            throw PairingDocumentError.invalidDocument("Connection config expiry could not be parsed.")
        }

        guard Date() <= expiresAt else {
            throw PairingDocumentError.invalidDocument(
                "Connection config expired at \(document.config.expiresAt)."
            )
        }

        let normalizedExpected = expectedAppId?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        if !normalizedExpected.isEmpty,
           document.config.appId.trimmingCharacters(in: .whitespacesAndNewlines) != normalizedExpected {
            throw PairingDocumentError.invalidDocument(
                "Config appId '\(document.config.appId)' does not match expected app id '\(normalizedExpected)'."
            )
        }
    }

    private func decodeJSONObject(_ data: Data) throws -> [String: Any] {
        let raw = try JSONSerialization.jsonObject(with: data, options: [])
        guard let object = raw as? [String: Any] else {
            throw PairingDocumentError.invalidDocument("Config JSON root must be an object.")
        }

        return object
    }

    private func verifyPairingConfigSignature(_ config: PairingConfig) -> Bool {
        guard let publicKeyData = Data(base64Encoded: config.host.hostPubKey),
              let signatureData = Data(base64Encoded: config.signature)
        else {
            return false
        }

        let publicKey = (try? P256.Signing.PublicKey(derRepresentation: publicKeyData))
            ?? (try? P256.Signing.PublicKey(x963Representation: publicKeyData))
        guard let publicKey else {
            return false
        }

        let signables = PairingCanonicalJSON.signables(for: config)
        for signable in signables {
            let data = Data(signable.utf8)

            if let rawSignature = try? P256.Signing.ECDSASignature(rawRepresentation: signatureData),
               publicKey.isValidSignature(rawSignature, for: data) {
                return true
            }

            if let derSignature = try? P256.Signing.ECDSASignature(derRepresentation: signatureData),
               publicKey.isValidSignature(derSignature, for: data) {
                return true
            }
        }

        return false
    }

    private func applyConnectionHint(
        trustAnchorConfig: PairingConfig,
        connectionHint: PairingConnectionHint
    ) -> PairingConfig {
        PairingConfig(
            schema: trustAnchorConfig.schema,
            configId: connectionHint.configId,
            appId: trustAnchorConfig.appId,
            appName: trustAnchorConfig.appName,
            issuedAt: connectionHint.issuedAt,
            expiresAt: connectionHint.expiresAt,
            oneTimeToken: connectionHint.oneTimeToken,
            host: PairingHost(
                hostId: trustAnchorConfig.host.hostId,
                hostName: trustAnchorConfig.host.hostName,
                discoveryPort: trustAnchorConfig.host.discoveryPort,
                hostPubKey: trustAnchorConfig.host.hostPubKey,
                hostPubKeyFingerprint: trustAnchorConfig.host.hostPubKeyFingerprint
            ),
            challenge: connectionHint.challenge,
            trust: PairingTrust(
                mode: trustAnchorConfig.trust.mode,
                requireTokenOnFirstPair: trustAnchorConfig.trust.requireTokenOnFirstPair,
                allowLanDiscovery: trustAnchorConfig.trust.allowLanDiscovery
            ),
            signature: trustAnchorConfig.signature
        )
    }

    private func parseTimestamp(_ rawValue: String) -> Date? {
        if let parsed = Self.makeFractionalFormatter().date(from: rawValue) {
            return parsed
        }

        return Self.makeStandardFormatter().date(from: rawValue)
    }

    private static func makeFractionalFormatter() -> ISO8601DateFormatter {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter
    }

    private static func makeStandardFormatter() -> ISO8601DateFormatter {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime]
        return formatter
    }
}

enum PairingCanonicalJSON {
    static func signables(for config: PairingConfig) -> [String] {
        [
            serializePairingConfigForSignature(config),
            serializePairingConfigForSignatureWithoutHostIdentity(config),
            serializeTransportPairingConfigForSignature(config),
            serializeTransportPairingConfigForSignatureWithoutHostIdentity(config),
            serializeLegacyPairingConfigForSignature(config),
            serializeLegacyPairingConfigForSignatureWithoutHostIdentity(config),
        ]
    }

    private static func serializePairingConfigForSignature(_ config: PairingConfig) -> String {
        serializeConfig(
            config: config,
            hostJson: serializeHost(
                hostId: config.host.hostId,
                hostName: config.host.hostName,
                wsPort: nil,
                wsPath: nil,
                discoveryPort: config.host.discoveryPort,
                hostPubKey: config.host.hostPubKey,
                hostPubKeyFingerprint: config.host.hostPubKeyFingerprint
            )
        )
    }

    private static func serializePairingConfigForSignatureWithoutHostIdentity(_ config: PairingConfig) -> String {
        serializeConfig(
            config: config,
            hostJson: serializeHost(
                hostId: nil,
                hostName: nil,
                wsPort: nil,
                wsPath: nil,
                discoveryPort: config.host.discoveryPort,
                hostPubKey: config.host.hostPubKey,
                hostPubKeyFingerprint: config.host.hostPubKeyFingerprint
            )
        )
    }

    private static func serializeTransportPairingConfigForSignature(_ config: PairingConfig) -> String {
        serializeConfig(
            config: config,
            hostJson: serializeHost(
                hostId: config.host.hostId,
                hostName: config.host.hostName,
                wsPort: PairingProtocolDefaults.webSocketPort,
                wsPath: PairingProtocolDefaults.webSocketPath,
                discoveryPort: config.host.discoveryPort,
                hostPubKey: config.host.hostPubKey,
                hostPubKeyFingerprint: config.host.hostPubKeyFingerprint
            )
        )
    }

    private static func serializeTransportPairingConfigForSignatureWithoutHostIdentity(_ config: PairingConfig) -> String {
        serializeConfig(
            config: config,
            hostJson: serializeHost(
                hostId: nil,
                hostName: nil,
                wsPort: PairingProtocolDefaults.webSocketPort,
                wsPath: PairingProtocolDefaults.webSocketPath,
                discoveryPort: config.host.discoveryPort,
                hostPubKey: config.host.hostPubKey,
                hostPubKeyFingerprint: config.host.hostPubKeyFingerprint
            )
        )
    }

    private static func serializeLegacyPairingConfigForSignature(_ config: PairingConfig) -> String {
        serializeConfig(
            config: config,
            hostJson: serializeHost(
                hostId: config.host.hostId,
                hostName: config.host.hostName,
                wsPort: nil,
                wsPath: nil,
                discoveryPort: nil,
                hostPubKey: config.host.hostPubKey,
                hostPubKeyFingerprint: config.host.hostPubKeyFingerprint
            )
        )
    }

    private static func serializeLegacyPairingConfigForSignatureWithoutHostIdentity(_ config: PairingConfig) -> String {
        serializeConfig(
            config: config,
            hostJson: serializeHost(
                hostId: nil,
                hostName: nil,
                wsPort: nil,
                wsPath: nil,
                discoveryPort: nil,
                hostPubKey: config.host.hostPubKey,
                hostPubKeyFingerprint: config.host.hostPubKeyFingerprint
            )
        )
    }

    private static func serializeConfig(config: PairingConfig, hostJson: String) -> String {
        [
            jsonStringField("schema", config.schema),
            jsonStringField("configId", config.configId),
            jsonStringField("appId", config.appId),
            jsonStringField("appName", config.appName),
            jsonStringField("issuedAt", config.issuedAt),
            jsonStringField("expiresAt", config.expiresAt),
            jsonStringField("oneTimeToken", config.oneTimeToken),
            #""host":\#(hostJson)"#,
            #""challenge":\#(serializeChallenge(config.challenge))"#,
            #""trust":\#(serializeTrust(config.trust))"#,
        ].joined(prefix: "{", suffix: "}")
    }

    private static func serializeHost(
        hostId: String?,
        hostName: String?,
        wsPort: Int?,
        wsPath: String?,
        discoveryPort: Int?,
        hostPubKey: String,
        hostPubKeyFingerprint: String
    ) -> String {
        var fields: [String] = []
        if let hostId {
            fields.append(jsonStringField("hostId", hostId))
        }

        if let hostName {
            fields.append(jsonStringField("hostName", hostName))
        }

        if let wsPort {
            fields.append(#""wsPort":\#(wsPort)"#)
        }

        if let wsPath {
            fields.append(jsonStringField("wsPath", wsPath))
        }

        if let discoveryPort {
            fields.append(#""discoveryPort":\#(discoveryPort)"#)
        }

        fields.append(jsonStringField("hostPubKey", hostPubKey))
        fields.append(jsonStringField("hostPubKeyFingerprint", hostPubKeyFingerprint))
        return fields.joined(prefix: "{", suffix: "}")
    }

    private static func serializeChallenge(_ challenge: PairingChallenge) -> String {
        [
            jsonStringField("alg", challenge.alg),
            jsonStringField("challengePubKey", challenge.challengePubKey),
            #""requireProofOnFirstPair":\#(challenge.requireProofOnFirstPair ? "true" : "false")"#,
        ].joined(prefix: "{", suffix: "}")
    }

    private static func serializeTrust(_ trust: PairingTrust) -> String {
        [
            jsonStringField("mode", trust.mode),
            #""requireTokenOnFirstPair":\#(trust.requireTokenOnFirstPair ? "true" : "false")"#,
            #""allowLanDiscovery":\#(trust.allowLanDiscovery ? "true" : "false")"#,
        ].joined(prefix: "{", suffix: "}")
    }

    private static func jsonStringField(_ name: String, _ value: String) -> String {
        #""\#(name)":"\#(escapeJSONString(value))""#
    }

    private static func escapeJSONString(_ value: String) -> String {
        var result = ""
        result.reserveCapacity(value.count)

        for scalar in value.unicodeScalars {
            switch scalar {
            case "\"":
                result.append("\\\"")
            case "\\":
                result.append("\\\\")
            case "\n":
                result.append("\\n")
            case "\r":
                result.append("\\r")
            case "\t":
                result.append("\\t")
            default:
                if scalar.value < 0x20 {
                    result.append(String(format: "\\u%04X", scalar.value))
                } else {
                    result.append(String(scalar))
                }
            }
        }

        return result
    }
}

public enum PairingDocumentError: LocalizedError {
    case invalidDocument(String)

    public var errorDescription: String? {
        switch self {
        case .invalidDocument(let message):
            return message
        }
    }
}

private extension JSONDecoder {
    static let ansightDecoder: JSONDecoder = {
        let decoder = JSONDecoder()
        decoder.keyDecodingStrategy = .useDefaultKeys
        return decoder
    }()
}

private extension Array where Element == String {
    func joined(prefix: String, suffix: String) -> String {
        prefix + joined(separator: ",") + suffix
    }
}
