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
    public var hostAddresses: [String]?
    public var hostName: String?
    public var wifiName: String?
    public var capturedAt: String?

    public var hostAddress: String? {
        get { hostAddresses?.first(where: { !$0.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }) }
        set {
            hostAddresses = newValue.map { [$0] }
        }
    }

    public init(
        schema: String = PairingDiscoveryHint.schemaName,
        source: String? = nil,
        hostAddress: String? = nil,
        hostAddresses: [String]? = nil,
        hostName: String? = nil,
        wifiName: String? = nil,
        capturedAt: String? = nil
    ) {
        self.schema = schema
        self.source = source
        self.hostAddresses = hostAddresses ?? hostAddress.map { [$0] }
        self.hostName = hostName
        self.wifiName = wifiName
        self.capturedAt = capturedAt
    }
}

public struct PairingTicket: Sendable, Codable, Equatable {
    public static let schemaName = "ansight.pairing-ticket.v1"

    public var schema: String
    public var config: PairingConfig
    public var discovery: PairingDiscoveryHint?

    public init(
        schema: String = PairingTicket.schemaName,
        config: PairingConfig,
        discovery: PairingDiscoveryHint? = nil
    ) {
        self.schema = schema
        self.config = config
        self.discovery = discovery
    }
}

public struct ParsedPairingDocument: Sendable, Codable, Equatable {
    public var config: PairingConfig
    public var discoveryHint: PairingDiscoveryHint?

    public init(
        config: PairingConfig,
        discoveryHint: PairingDiscoveryHint? = nil
    ) {
        self.config = config
        self.discoveryHint = discoveryHint
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
            throw PairingDocumentError.invalidDocument("Paste or load a pairing ticket.")
        }

        let data = Data(trimmedJson.utf8)
        let rootObject = try decodeJSONObject(data)
        let schema = rootObject["schema"] as? String

        if schema == "ansight.pairing-bootstrap.v1" {
            throw PairingDocumentError.invalidDocument(
                "Legacy bootstrap pairing payloads are no longer supported. Export a fresh pairing ticket from Ansight Studio."
            )
        }

        guard schema == PairingTicket.schemaName else {
            let resolvedSchema = schema?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
            if resolvedSchema.isEmpty {
                throw PairingDocumentError.invalidDocument("Pairing payloads must be pairing tickets.")
            }

            throw PairingDocumentError.invalidDocument(
                "Unsupported pairing payload schema '\(resolvedSchema)'. Export a fresh pairing ticket from Ansight Studio."
            )
        }

        let ticket = try JSONDecoder.ansightDecoder.decode(PairingTicket.self, from: data)
        return ParsedPairingDocument(
            config: ticket.config,
            discoveryHint: normalizeDiscovery(ticket.discovery)
        )
    }

    public func validateDocument(_ document: ParsedPairingDocument, expectedAppId: String? = nil) throws {
        guard verifyPairingConfigSignature(document.config) else {
            throw PairingDocumentError.invalidDocument("Pairing ticket config signature is invalid.")
        }

        guard let expiresAt = parseTimestamp(document.config.expiresAt) else {
            throw PairingDocumentError.invalidDocument("Pairing ticket config expiry could not be parsed.")
        }

        guard Date() <= expiresAt else {
            throw PairingDocumentError.invalidDocument(
                "Pairing ticket expired at \(document.config.expiresAt)."
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

        let data = Data(PairingCanonicalJSON.serializePairingConfigForSignature(config).utf8)

        if let rawSignature = try? P256.Signing.ECDSASignature(rawRepresentation: signatureData),
           publicKey.isValidSignature(rawSignature, for: data) {
            return true
        }

        if let derSignature = try? P256.Signing.ECDSASignature(derRepresentation: signatureData),
           publicKey.isValidSignature(derSignature, for: data) {
            return true
        }

        return false
    }

    private func normalizeDiscovery(_ discoveryHint: PairingDiscoveryHint?) -> PairingDiscoveryHint? {
        guard var discoveryHint else {
            return nil
        }

        let normalizedAddresses = (discoveryHint.hostAddresses ?? [])
            .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
            .filter { !$0.isEmpty }
        discoveryHint.hostAddresses = normalizedAddresses.isEmpty ? nil : normalizedAddresses
        if discoveryHint.schema.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            discoveryHint.schema = PairingDiscoveryHint.schemaName
        }

        return discoveryHint
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
    static func serializePairingConfigForSignature(_ config: PairingConfig) -> String {
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
