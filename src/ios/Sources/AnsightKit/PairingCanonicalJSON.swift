import CryptoKit
import Foundation

enum PairingCanonicalJSON {
    static func serializePairingConfigForSignature(_ config: PairingConfig) -> String {
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
            jsonDateTimeOffsetField("issuedAt", config.issuedAt),
            jsonDateTimeOffsetField("expiresAt", config.expiresAt),
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
        #""\#(name)":"\#(escapeJSONString(value, escapePlus: true))""#
    }

    private static func jsonDateTimeOffsetField(_ name: String, _ value: String) -> String {
        #""\#(name)":"\#(escapeJSONString(value, escapePlus: false))""#
    }

    private static func escapeJSONString(_ value: String, escapePlus: Bool) -> String {
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
            case "+" where escapePlus:
                result.append("\\u002B")
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
