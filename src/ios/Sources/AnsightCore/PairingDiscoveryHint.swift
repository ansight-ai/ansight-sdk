import CryptoKit
import Foundation

public struct PairingDiscoveryHint: Sendable, Codable, Equatable {
    public static let schemaName = "ansight.discovery-hint.v1"

    public var schema: String
    public var source: String?
    public var hostAddresses: [String]?
    public var discoveryPort: Int?
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
        discoveryPort: Int? = nil,
        hostName: String? = nil,
        wifiName: String? = nil,
        capturedAt: String? = nil
    ) {
        self.schema = schema
        self.source = source
        self.hostAddresses = hostAddresses ?? hostAddress.map { [$0] }
        self.discoveryPort = discoveryPort
        self.hostName = hostName
        self.wifiName = wifiName
        self.capturedAt = capturedAt
    }
}
