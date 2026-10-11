import Foundation

/// The store pins this immutable directory until the transfer is released or acknowledged.
public struct OfflineCaptureLease: Sendable {
    public let sessionId: String
    public let token: String
    public let directory: URL
}
