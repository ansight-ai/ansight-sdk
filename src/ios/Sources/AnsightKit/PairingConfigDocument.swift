import CryptoKit
import Foundation

public struct PairingConfigDocument: Sendable, Codable, Equatable {
    public static let schemaName = "ansight.pairing-config-document.v1"
    public static let legacySchemaName = "ansight.pairing-ticket.v1"

    public var schema: String
    public var config: PairingConfig
    public var discovery: PairingDiscoveryHint?

    public init(
        schema: String = PairingConfigDocument.schemaName,
        config: PairingConfig,
        discovery: PairingDiscoveryHint? = nil
    ) {
        self.schema = schema
        self.config = config
        self.discovery = discovery
    }
}
