import CryptoKit
import Foundation

public struct ParsedPairingDocument: Sendable, Codable, Equatable {
    public var config: PairingConfig
    public var discoveryHint: PairingDiscoveryHint?

    public init(
        config: PairingConfig,
        discoveryHint: PairingDiscoveryHint? = nil
    ) {
        self.config = config
        self.discoveryHint = discoveryHint
    }
}
