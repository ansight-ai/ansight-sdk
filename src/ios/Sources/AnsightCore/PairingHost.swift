import CryptoKit
import Foundation

public struct PairingHost: Sendable, Codable, Equatable {
    private enum CodingKeys: String, CodingKey {
        case hostId
        case hostName
        case discoveryPort
        case hostPubKey
        case hostPubKeyFingerprint
        case tlsPins
    }

    public var hostId: String?
    public var hostName: String?
    public var discoveryPort: Int = PairingProtocolDefaults.discoveryPort
    public var hostPubKey: String
    public var hostPubKeyFingerprint: String
    public var tlsPins: [PairingTlsPin]?

    public init(
        hostId: String? = nil,
        hostName: String? = nil,
        discoveryPort: Int = PairingProtocolDefaults.discoveryPort,
        hostPubKey: String,
        hostPubKeyFingerprint: String,
        tlsPins: [PairingTlsPin]? = nil
    ) {
        self.hostId = hostId
        self.hostName = hostName
        self.discoveryPort = discoveryPort
        self.hostPubKey = hostPubKey
        self.hostPubKeyFingerprint = hostPubKeyFingerprint
        self.tlsPins = tlsPins
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        hostId = try container.decodeIfPresent(String.self, forKey: .hostId)
        hostName = try container.decodeIfPresent(String.self, forKey: .hostName)
        discoveryPort = try container.decodeIfPresent(Int.self, forKey: .discoveryPort)
            ?? PairingProtocolDefaults.discoveryPort
        hostPubKey = try container.decode(String.self, forKey: .hostPubKey)
        hostPubKeyFingerprint = try container.decode(String.self, forKey: .hostPubKeyFingerprint)
        tlsPins = try container.decodeIfPresent([PairingTlsPin].self, forKey: .tlsPins)
    }
}
