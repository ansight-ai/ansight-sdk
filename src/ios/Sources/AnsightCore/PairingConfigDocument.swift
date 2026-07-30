import Foundation

public struct PairingConfigDocument: Sendable, Codable, Equatable {
    public static let schemaName = "ansight.enrollment-invite-document.v2"

    public var schema: String
    public var config: PairingConfig
    public var discovery: PairingDiscoveryHint?

    private enum CodingKeys: String, CodingKey {
        case schema
        case config = "invite"
        case discovery
    }

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
