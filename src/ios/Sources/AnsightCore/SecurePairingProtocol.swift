import CryptoKit
import Foundation
import Security

enum SecurePairingProtocol {
    static let version = 2
    static let signatureAlgorithm = "ES256-P1363"
    static let proofAlgorithm = "HMAC-SHA256"

    static func makeConnectInit(config: PairingConfig) throws -> ConnectInitV2 {
        ConnectInitV2(
            type: "CONNECT_INIT_V2",
            ver: version,
            requestId: try randomBase64URL(byteCount: 16),
            configId: config.configId,
            appId: config.appId,
            clientNonce: try randomBase64URL(byteCount: 32),
            supportedVersions: [version],
            supportedTransports: ["wss"]
        )
    }

    static func validateOffer(
        _ offer: ConnectOfferV2,
        request: ConnectInitV2,
        config: PairingConfig,
        now: Date = Date()
    ) throws {
        guard offer.type == "CONNECT_OFFER_V2", offer.ver == version,
              offer.requestId == request.requestId,
              offer.configId == request.configId,
              offer.appId == request.appId,
              offer.clientNonce == request.clientNonce,
              offer.hostId == config.host.hostId,
              offer.selectedVersion == version,
              offer.selectedTransport == "wss",
              (1...65_535).contains(offer.webSocketPort),
              isValidWebSocketPath(offer.webSocketPath)
        else {
            throw PairingDocumentError.invalidDocument("Secure host offer did not match the connection request.")
        }

        guard decodedBase64URL(offer.hostNonce)?.count == 32,
              decodedBase64URL(request.clientNonce)?.count == 32,
              parseTimestamp(offer.expiresAt).map({ now <= $0 && $0 <= now.addingTimeInterval(60) }) == true
        else {
            throw PairingDocumentError.invalidDocument("Secure host offer nonce or expiry is invalid.")
        }

        guard offer.signatureAlgorithm == signatureAlgorithm,
              let hostKeyData = Data(base64Encoded: config.host.hostPubKey),
              let hostKey = try? P256.Signing.PublicKey(derRepresentation: hostKeyData),
              let signatureData = Data(base64Encoded: offer.signature),
              let signature = try? P256.Signing.ECDSASignature(rawRepresentation: signatureData),
              hostKey.isValidSignature(signature, for: Data(offerSignatureInput(request: request, offer: offer).utf8))
        else {
            throw PairingDocumentError.invalidDocument("Secure host offer signature is invalid.")
        }

        guard validTlsPins(config.host.tlsPins, now: now).contains(offer.tlsSpkiSha256) else {
            throw PairingDocumentError.invalidDocument("Secure host offer used an untrusted TLS public key.")
        }
    }

    static func verifyConfigSignature(_ config: PairingConfig) -> Bool {
        guard config.schema == PairingConfig.secureSchemaName,
              config.signatureAlgorithm == signatureAlgorithm,
              let publicKeyData = Data(base64Encoded: config.host.hostPubKey),
              let publicKey = try? P256.Signing.PublicKey(derRepresentation: publicKeyData),
              let signatureData = Data(base64Encoded: config.signature),
              let signature = try? P256.Signing.ECDSASignature(rawRepresentation: signatureData)
        else {
            return false
        }

        return publicKey.isValidSignature(signature, for: Data(canonicalConfig(config).utf8))
    }

    static func validateConfigShape(_ config: PairingConfig, now: Date = Date()) throws {
        guard config.schema == PairingConfig.secureSchemaName,
              config.minProtocolVersion == version,
              config.allowedTransports == ["wss"],
              config.signatureAlgorithm == signatureAlgorithm,
              config.oneTimeToken.isEmpty,
              config.challenge.alg.isEmpty,
              config.challenge.challengePubKey.isEmpty,
              let hostId = config.host.hostId,
              !hostId.isEmpty,
              fingerprint(publicKeyBase64: config.host.hostPubKey) == hostId,
              config.host.hostPubKeyFingerprint == hostId,
              !validTlsPins(config.host.tlsPins, now: now).isEmpty,
              let enrollment = config.enrollment,
              enrollment.maxUses == 1,
              decodedBase64URL(enrollment.secret)?.count == 32,
              !enrollment.ticketId.isEmpty,
              canonicalScopes(enrollment.maxScopes) == enrollment.maxScopes,
              parseTimestamp(enrollment.expiresAt).map({ now <= $0 }) == true,
              parseTimestamp(enrollment.grantExpiresAt).map({ now <= $0 }) == true
        else {
            throw PairingDocumentError.invalidDocument("Secure pairing config has invalid trust or enrollment constraints.")
        }
    }

    static func validateRememberedProfile(
        _ config: PairingConfig,
        expectedAppId: String?,
        now: Date = Date()
    ) throws {
        let normalizedExpectedAppId = expectedAppId?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        guard config.isSecureRememberedProfile,
              !config.configId.isEmpty,
              !config.appId.isEmpty,
              normalizedExpectedAppId.isEmpty || config.appId == normalizedExpectedAppId,
              config.minProtocolVersion == version,
              config.allowedTransports == ["wss"],
              config.signatureAlgorithm == signatureAlgorithm,
              config.signature.isEmpty,
              config.enrollment == nil,
              config.oneTimeToken.isEmpty,
              config.challenge.alg.isEmpty,
              config.challenge.challengePubKey.isEmpty,
              let hostId = config.host.hostId,
              !hostId.isEmpty,
              fingerprint(publicKeyBase64: config.host.hostPubKey) == hostId,
              config.host.hostPubKeyFingerprint == hostId,
              !validTlsPins(config.host.tlsPins, now: now).isEmpty,
              parseTimestamp(config.expiresAt).map({ now <= $0 }) == true
        else {
            throw PairingDocumentError.invalidDocument("Remembered secure pairing profile is invalid or expired.")
        }
    }

    static func canonicalConfig(_ config: PairingConfig) -> String {
        let pins = (config.host.tlsPins ?? []).sorted {
            if $0.notBefore == $1.notBefore {
                return $0.tlsSpkiSha256 < $1.tlsSpkiSha256
            }
            return $0.notBefore < $1.notBefore
        }
        let pinJSON = pins.map { pin in
            object([
                field("tlsSpkiSha256", pin.tlsSpkiSha256),
                field("notBefore", pin.notBefore),
                field("notAfter", pin.notAfter),
            ])
        }.joined(separator: ",")
        let hostJSON = object([
            field("hostId", config.host.hostId ?? ""),
            field("hostName", config.host.hostName ?? ""),
            numberField("discoveryPort", config.host.discoveryPort),
            field("hostPubKey", config.host.hostPubKey),
            field("hostPubKeyFingerprint", config.host.hostPubKeyFingerprint),
            #"\"tlsPins\":[\#(pinJSON)]"#,
        ])
        let enrollment = config.enrollment
        let enrollmentJSON = object([
            field("ticketId", enrollment?.ticketId ?? ""),
            field("secret", enrollment?.secret ?? ""),
            field("expiresAt", enrollment?.expiresAt ?? ""),
            field("grantExpiresAt", enrollment?.grantExpiresAt ?? ""),
            numberField("maxUses", enrollment?.maxUses ?? 0),
            stringArrayField("maxScopes", enrollment?.maxScopes ?? []),
            boolField("allowCritical", enrollment?.allowCritical ?? false),
        ])

        return object([
            field("schema", config.schema),
            field("configId", config.configId),
            field("appId", config.appId),
            field("appName", config.appName),
            field("issuedAt", config.issuedAt),
            field("expiresAt", config.expiresAt),
            numberField("minProtocolVersion", config.minProtocolVersion ?? 0),
            stringArrayField("allowedTransports", config.allowedTransports ?? []),
            #"\"host\":\#(hostJSON)"#,
            #"\"enrollment\":\#(enrollmentJSON)"#,
            field("signatureAlgorithm", config.signatureAlgorithm ?? ""),
        ])
    }

    static func canonicalConnectInit(_ request: ConnectInitV2) -> String {
        object([
            field("type", request.type),
            numberField("ver", request.ver),
            field("requestId", request.requestId),
            field("configId", request.configId),
            field("appId", request.appId),
            field("clientNonce", request.clientNonce),
            numberArrayField("supportedVersions", request.supportedVersions),
            stringArrayField("supportedTransports", request.supportedTransports),
        ])
    }

    static func canonicalConnectOffer(_ offer: ConnectOfferV2) -> String {
        object([
            field("type", offer.type),
            numberField("ver", offer.ver),
            field("requestId", offer.requestId),
            field("configId", offer.configId),
            field("appId", offer.appId),
            field("clientNonce", offer.clientNonce),
            field("hostNonce", offer.hostNonce),
            field("hostId", offer.hostId),
            numberField("selectedVersion", offer.selectedVersion),
            field("selectedTransport", offer.selectedTransport),
            numberField("webSocketPort", offer.webSocketPort),
            field("webSocketPath", offer.webSocketPath),
            field("tlsSpkiSha256", offer.tlsSpkiSha256),
            field("expiresAt", offer.expiresAt),
            field("signatureAlgorithm", offer.signatureAlgorithm),
        ])
    }

    static func offerSignatureInput(request: ConnectInitV2, offer: ConnectOfferV2) -> String {
        "ANSIGHT-CONNECT-OFFER-V2\n\(canonicalConnectInit(request))\n\(canonicalConnectOffer(offer))"
    }

    static func canonicalChallenge(_ challenge: AuthChallengeV2) -> String {
        object([
            field("type", challenge.type),
            numberField("ver", challenge.ver),
            field("authSessionId", challenge.authSessionId),
            field("requestId", challenge.requestId),
            field("configId", challenge.configId),
            field("appId", challenge.appId),
            field("clientNonce", challenge.clientNonce),
            field("hostNonce", challenge.hostNonce),
            field("serverChallenge", challenge.serverChallenge),
            field("expiresAt", challenge.expiresAt),
        ])
    }

    static func enrollmentProofInput(
        config: PairingConfig,
        request: ConnectInitV2,
        offer: ConnectOfferV2,
        challenge: AuthChallengeV2,
        clientKeyId: String,
        clientPublicKey: String,
        requestedScopes: [String],
        requestCritical: Bool
    ) -> String {
        object([
            field("context", "ANSIGHT-AUTH-ENROLL-V2"),
            field("configSignatureSha256", configSignatureFingerprint(config.signature)),
            field("requestId", request.requestId),
            field("clientNonce", request.clientNonce),
            field("hostNonce", offer.hostNonce),
            field("tlsSpkiSha256", offer.tlsSpkiSha256),
            field("authSessionId", challenge.authSessionId),
            field("serverChallenge", challenge.serverChallenge),
            field("ticketId", config.enrollment?.ticketId ?? ""),
            field("clientKeyId", clientKeyId),
            field("clientPublicKey", clientPublicKey),
            stringArrayField("requestedScopes", requestedScopes),
            boolField("requestCritical", requestCritical),
        ])
    }

    static func reconnectProofInput(
        request: ConnectInitV2,
        offer: ConnectOfferV2,
        challenge: AuthChallengeV2,
        grantId: String,
        clientKeyId: String
    ) -> String {
        object([
            field("context", "ANSIGHT-AUTH-PROVE-V2"),
            field("requestId", request.requestId),
            field("clientNonce", request.clientNonce),
            field("hostNonce", offer.hostNonce),
            field("tlsSpkiSha256", offer.tlsSpkiSha256),
            field("authSessionId", challenge.authSessionId),
            field("serverChallenge", challenge.serverChallenge),
            field("grantId", grantId),
            field("clientKeyId", clientKeyId),
        ])
    }

    static func canonicalGrant(_ grant: PairingGrantV2) -> String {
        object([
            field("grantId", grant.grantId),
            field("hostId", grant.hostId),
            field("configId", grant.configId),
            field("appId", grant.appId),
            field("clientKeyId", grant.clientKeyId),
            stringArrayField("allowedScopes", grant.allowedScopes),
            boolField("allowCritical", grant.allowCritical),
            field("issuedAt", grant.issuedAt),
            field("expiresAt", grant.expiresAt),
            field("signatureAlgorithm", grant.signatureAlgorithm),
        ])
    }

    static func verifyGrant(_ grant: PairingGrantV2, hostPublicKey: String, now: Date = Date()) -> Bool {
        guard grant.signatureAlgorithm == signatureAlgorithm,
              parseTimestamp(grant.expiresAt).map({ now <= $0 }) == true,
              let keyData = Data(base64Encoded: hostPublicKey),
              let key = try? P256.Signing.PublicKey(derRepresentation: keyData),
              let signatureData = Data(base64Encoded: grant.signature),
              let signature = try? P256.Signing.ECDSASignature(rawRepresentation: signatureData)
        else {
            return false
        }
        return key.isValidSignature(signature, for: Data(canonicalGrant(grant).utf8))
    }

    static func enrollmentProof(secret: String, input: String) throws -> String {
        guard let secretData = decodedBase64URL(secret), secretData.count == 32 else {
            throw PairingDocumentError.invalidDocument("Enrollment secret is invalid.")
        }
        let key = SymmetricKey(data: secretData)
        let code = HMAC<SHA256>.authenticationCode(for: Data(input.utf8), using: key)
        return base64URL(Data(code))
    }

    static func fingerprint(publicKeyBase64: String) -> String? {
        guard let data = Data(base64Encoded: publicKeyBase64) else { return nil }
        return base64URL(Data(SHA256.hash(data: data)))
    }

    static func configSignatureFingerprint(_ signature: String) -> String {
        guard let data = Data(base64Encoded: signature) else { return "" }
        return base64URL(Data(SHA256.hash(data: data)))
    }

    static func base64URL(_ data: Data) -> String {
        data.base64EncodedString()
            .replacingOccurrences(of: "+", with: "-")
            .replacingOccurrences(of: "/", with: "_")
            .replacingOccurrences(of: "=", with: "")
    }

    static func decodedBase64URL(_ value: String) -> Data? {
        var base64 = value.replacingOccurrences(of: "-", with: "+")
            .replacingOccurrences(of: "_", with: "/")
        base64.append(String(repeating: "=", count: (4 - base64.count % 4) % 4))
        return Data(base64Encoded: base64)
    }

    static func randomBase64URL(byteCount: Int) throws -> String {
        var bytes = [UInt8](repeating: 0, count: byteCount)
        guard SecRandomCopyBytes(kSecRandomDefault, bytes.count, &bytes) == errSecSuccess else {
            throw PairingDocumentError.invalidDocument("Secure random generation failed.")
        }
        return base64URL(Data(bytes))
    }

    static func canonicalScopes(_ scopes: [String]) -> [String] {
        ["Read", "Write", "Delete"].filter { scopes.contains($0) }
    }

    static func parseTimestamp(_ value: String) -> Date? {
        let fractional = ISO8601DateFormatter()
        fractional.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return fractional.date(from: value) ?? ISO8601DateFormatter().date(from: value)
    }

    private static func validTlsPins(_ pins: [PairingTlsPin]?, now: Date) -> Set<String> {
        Set((pins ?? []).compactMap { pin in
            guard decodedBase64URL(pin.tlsSpkiSha256)?.count == 32,
                  let notBefore = parseTimestamp(pin.notBefore),
                  let notAfter = parseTimestamp(pin.notAfter),
                  notBefore <= now,
                  now <= notAfter
            else { return nil }
            return pin.tlsSpkiSha256
        })
    }

    private static func isValidWebSocketPath(_ value: String) -> Bool {
        let prefix = "/ws/v2/"
        guard value.hasPrefix(prefix), value.count > prefix.count else {
            return false
        }
        return value.dropFirst(prefix.count).allSatisfy {
            $0.isASCII && ($0.isLetter || $0.isNumber || $0 == "-" || $0 == "_")
        }
    }

    private static func object(_ fields: [String]) -> String {
        "{\(fields.joined(separator: ","))}"
    }

    private static func field(_ name: String, _ value: String, escapePlus: Bool = true) -> String {
        "\"\(name)\":\"\(escape(value, escapePlus: escapePlus))\""
    }

    private static func numberField(_ name: String, _ value: Int) -> String {
        "\"\(name)\":\(value)"
    }

    private static func boolField(_ name: String, _ value: Bool) -> String {
        "\"\(name)\":\(value ? "true" : "false")"
    }

    private static func stringArrayField(_ name: String, _ values: [String]) -> String {
        let valuesJSON = values.map { "\"\(escape($0, escapePlus: true))\"" }.joined(separator: ",")
        return "\"\(name)\":[\(valuesJSON)]"
    }

    private static func numberArrayField(_ name: String, _ values: [Int]) -> String {
        "\"\(name)\":[\(values.map(String.init).joined(separator: ","))]"
    }

    private static func escape(_ value: String, escapePlus: Bool) -> String {
        var result = ""
        for scalar in value.unicodeScalars {
            switch scalar {
            case "\"": result.append("\\\"")
            case "\\": result.append("\\\\")
            case "\n": result.append("\\n")
            case "\r": result.append("\\r")
            case "\t": result.append("\\t")
            case "+" where escapePlus: result.append("\\u002B")
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

struct ConnectInitV2: Sendable, Codable, Equatable {
    let type: String
    let ver: Int
    let requestId: String
    let configId: String
    let appId: String
    let clientNonce: String
    let supportedVersions: [Int]
    let supportedTransports: [String]
}

struct ConnectOfferV2: Sendable, Codable, Equatable {
    let type: String
    let ver: Int
    let requestId: String
    let configId: String
    let appId: String
    let clientNonce: String
    let hostNonce: String
    let hostId: String
    let selectedVersion: Int
    let selectedTransport: String
    let webSocketPort: Int
    let webSocketPath: String
    let tlsSpkiSha256: String
    let expiresAt: String
    let signatureAlgorithm: String
    let signature: String
}

struct AuthChallengeV2: Sendable, Codable, Equatable {
    let type: String
    let ver: Int
    let authSessionId: String
    let requestId: String
    let configId: String
    let appId: String
    let clientNonce: String
    let hostNonce: String
    let serverChallenge: String
    let expiresAt: String
}

struct AuthEnrollV2: Sendable, Codable, Equatable {
    let type: String
    let ver: Int
    let authSessionId: String
    let ticketId: String
    let clientKeyId: String
    let clientPublicKey: String
    let requestedScopes: [String]
    let requestCritical: Bool
    let proofAlgorithm: String
    let proof: String
}

struct AuthProveV2: Sendable, Codable, Equatable {
    let type: String
    let ver: Int
    let authSessionId: String
    let grantId: String
    let clientKeyId: String
    let signatureAlgorithm: String
    let signature: String
}

struct PairingGrantV2: Sendable, Codable, Equatable {
    let grantId: String
    let hostId: String
    let configId: String
    let appId: String
    let clientKeyId: String
    let allowedScopes: [String]
    let allowCritical: Bool
    let issuedAt: String
    let expiresAt: String
    let signatureAlgorithm: String
    let signature: String
}

struct AuthOkV2: Sendable, Codable, Equatable {
    let type: String
    let ver: Int
    let sessionId: String
    let grant: PairingGrantV2
}

struct AuthErrorV2: Sendable, Codable, Equatable, Error {
    let type: String
    let ver: Int
    let code: String
    let message: String
    let retryable: Bool
}

struct SecurePairingContext: Sendable {
    let config: PairingConfig
    let request: ConnectInitV2
    let offer: ConnectOfferV2
    let requestedScopes: [String]
    let requestCritical: Bool
}
