import CryptoKit
import Foundation

public struct PairingTicket: Sendable, Codable, Equatable {
    public static let schemaName = PairingConfigDocument.legacySchemaName

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
