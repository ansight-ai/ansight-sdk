import Foundation

public struct PairingTlsPin: Sendable, Codable, Equatable {
    public var tlsSpkiSha256: String
    public var notBefore: String
    public var notAfter: String

    public init(tlsSpkiSha256: String, notBefore: String, notAfter: String) {
        self.tlsSpkiSha256 = tlsSpkiSha256
        self.notBefore = notBefore
        self.notAfter = notAfter
    }
}
