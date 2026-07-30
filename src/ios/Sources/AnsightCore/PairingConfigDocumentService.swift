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
            throw PairingDocumentError.invalidDocument("Scan an Ansight enrollment QR code.")
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
        if schema == PairingConfig.schemaName {
            return ParsedPairingDocument(
                config: try JSONDecoder.ansightDecoder.decode(PairingConfig.self, from: data)
            )
        }
        if schema == PairingConfigDocument.schemaName {
            let document = try JSONDecoder.ansightDecoder.decode(PairingConfigDocument.self, from: data)
            return ParsedPairingDocument(
                config: document.config,
                discoveryHint: normalizeDiscovery(document.discovery)
            )
        }

        let resolvedSchema = schema?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        if resolvedSchema.isEmpty {
            throw PairingDocumentError.invalidDocument("The QR code is not an Ansight enrollment invite.")
        }
        throw PairingDocumentError.invalidDocument(
            "Unsupported enrollment invite schema '\(resolvedSchema)'."
        )
    }

    public func validateDocument(_ document: ParsedPairingDocument, expectedAppId: String? = nil) throws {
        let config = document.config
        guard config.schema == PairingConfig.schemaName,
              config.minProtocolVersion == 2,
              config.allowedTransports == ["ws"],
              !config.configId.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
              !config.appId.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
              !config.appName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
              (1...65_535).contains(config.host.discoveryPort),
              config.enrollment.maxUses == 1,
              decodeBase64URL(config.enrollment.accessToken)?.count == 32
        else {
            throw PairingDocumentError.invalidDocument(
                "Enrollment invite is incomplete or uses an unsupported connection protocol."
            )
        }

        guard let registrationExpiry = parseTimestamp(config.enrollment.grantExpiresAt) else {
            throw PairingDocumentError.invalidDocument("Device registration expiry could not be parsed.")
        }
        guard Date() <= registrationExpiry else {
            throw PairingDocumentError.invalidDocument(
                "Device registration expired at \(config.enrollment.grantExpiresAt). Scan a fresh QR code."
            )
        }

        let normalizedExpected = expectedAppId?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        if !normalizedExpected.isEmpty,
           config.appId.trimmingCharacters(in: .whitespacesAndNewlines) != normalizedExpected {
            throw PairingDocumentError.invalidDocument(
                "Enrollment invite appId '\(config.appId)' does not match expected app id '\(normalizedExpected)'."
            )
        }
    }

    private func decodeJSONObject(_ data: Data) throws -> [String: Any] {
        let raw = try JSONSerialization.jsonObject(with: data, options: [])
        guard let object = raw as? [String: Any] else {
            throw PairingDocumentError.invalidDocument("Enrollment invite JSON must be an object.")
        }
        return object
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
                guard seenAddresses.insert(key).inserted else {
                    return nil
                }
                return normalizedAddress
            }
        discoveryHint.hostAddresses = normalizedAddresses.isEmpty ? nil : normalizedAddresses
        if discoveryHint.schema.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            discoveryHint.schema = PairingDiscoveryHint.schemaName
        }
        return discoveryHint
    }

    private func parseTimestamp(_ rawValue: String) -> Date? {
        let fractionalFormatter = ISO8601DateFormatter()
        fractionalFormatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let parsed = fractionalFormatter.date(from: rawValue) {
            return parsed
        }

        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime]
        return formatter.date(from: rawValue)
    }

    private func decodeBase64URL(_ value: String) -> Data? {
        var normalized = value
            .trimmingCharacters(in: .whitespacesAndNewlines)
            .replacingOccurrences(of: "-", with: "+")
            .replacingOccurrences(of: "_", with: "/")
        switch normalized.count % 4 {
        case 0:
            break
        case 2:
            normalized.append("==")
        case 3:
            normalized.append("=")
        default:
            return nil
        }
        return Data(base64Encoded: normalized)
    }
}
