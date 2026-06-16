import CryptoKit
import Foundation

public struct PairingTrust: Sendable, Codable, Equatable {
    public var mode: String
    public var requireTokenOnFirstPair: Bool
    public var allowLanDiscovery: Bool

    public init(mode: String, requireTokenOnFirstPair: Bool, allowLanDiscovery: Bool) {
        self.mode = mode
        self.requireTokenOnFirstPair = requireTokenOnFirstPair
        self.allowLanDiscovery = allowLanDiscovery
    }
}
