import CryptoKit
import Foundation

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

        if let compactDocument = PairingConfigCodeGenerator.tryParse(trimmedJson) {
            return ParsedPairingDocument(
                config: compactDocument.config,
                discoveryHint: normalizeDiscovery(compactDocument.discovery)
            )
        }

        let data = Data(trimmedJson.utf8)
        let rootObject = try decodeJSONObject(data)
        let schema = rootObject["schema"] as? String

        if schema == "ansight.pairing-bootstrap.v1" {
            throw PairingDocumentError.invalidDocument(
                "Legacy bootstrap pairing payloads are no longer supported. Export a fresh pairing ticket from Ansight Studio."
            )
        }

        if schema == PairingConfig.schemaName {
            let config = try JSONDecoder.ansightDecoder.decode(PairingConfig.self, from: data)
            return ParsedPairingDocument(config: config)
        }

        if schema == PairingConfigDocument.schemaName || schema == PairingConfigDocument.legacySchemaName {
            let document = try JSONDecoder.ansightDecoder.decode(PairingConfigDocument.self, from: data)
            return ParsedPairingDocument(
                config: document.config,
                discoveryHint: normalizeDiscovery(document.discovery)
            )
        }

        let resolvedSchema = schema?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        if resolvedSchema.isEmpty {
            throw PairingDocumentError.invalidDocument("Pairing payloads must be pairing configs.")
        }

        throw PairingDocumentError.invalidDocument(
            "Unsupported pairing payload schema '\(resolvedSchema)'. Export a fresh pairing config from Ansight Studio."
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
                "Pairing config expired at \(document.config.expiresAt)."
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

        for signable in [
            PairingCanonicalJSON.serializePairingConfigForSignature(config),
            PairingCanonicalJSON.serializePairingConfigWithLegacyTrustForSignature(config),
        ] {
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

    private func normalizeDiscovery(_ discoveryHint: PairingDiscoveryHint?) -> PairingDiscoveryHint? {
        guard var discoveryHint else {
            return nil
        }

        var seenAddresses = Set<String>()
        let normalizedAddresses = (discoveryHint.hostAddresses ?? [])
            .compactMap { address -> String? in
                let normalizedAddress = address.trimmingCharacters(in: .whitespacesAndNewlines)
                guard !normalizedAddress.isEmpty else {
                    return nil
                }

                let key = normalizedAddress.lowercased()
                guard !seenAddresses.contains(key) else {
                    return nil
                }

                seenAddresses.insert(key)
                return normalizedAddress
            }
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
