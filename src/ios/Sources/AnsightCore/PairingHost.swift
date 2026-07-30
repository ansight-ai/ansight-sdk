import Foundation

public struct PairingHost: Sendable, Codable, Equatable {
    public var hostId: String?
    public var hostName: String?
    public var discoveryPort: Int

    public init(
        hostId: String? = nil,
        hostName: String? = nil,
        discoveryPort: Int = PairingProtocolDefaults.discoveryPort
    ) {
        self.hostId = hostId
        self.hostName = hostName
        self.discoveryPort = discoveryPort
    }

    private enum CodingKeys: String, CodingKey {
        case hostId
        case hostName
        case discoveryPort
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        hostId = try container.decodeIfPresent(String.self, forKey: .hostId)
        hostName = try container.decodeIfPresent(String.self, forKey: .hostName)
        discoveryPort = try container.decodeIfPresent(Int.self, forKey: .discoveryPort)
            ?? PairingProtocolDefaults.discoveryPort
    }
}
