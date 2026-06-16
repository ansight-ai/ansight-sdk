import CryptoKit
import Foundation

public struct PairingChallenge: Sendable, Codable, Equatable {
    public var alg: String
    public var challengePubKey: String
    public var requireProofOnFirstPair: Bool

    public init(alg: String, challengePubKey: String, requireProofOnFirstPair: Bool) {
        self.alg = alg
        self.challengePubKey = challengePubKey
        self.requireProofOnFirstPair = requireProofOnFirstPair
    }
}
