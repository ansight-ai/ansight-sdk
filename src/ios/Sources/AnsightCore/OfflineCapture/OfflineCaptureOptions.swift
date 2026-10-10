import Foundation

/// Applies to native recording on Apple platforms, including framework SDKs.
public struct OfflineCaptureOptions: Sendable, Codable, Equatable {
    public var enabled: Bool
    public var transferMode: OfflineCaptureTransferMode
    public var maximumSessions: Int
    public var maximumSessionBytes: Int64
    public var maximumRetainedBytes: Int64
    public var maximumQueuedBytes: Int

    public init(
        enabled: Bool = false,
        transferMode: OfflineCaptureTransferMode = .automatic,
        maximumSessions: Int = 10,
        maximumSessionBytes: Int64 = 128 * 1024 * 1024,
        maximumRetainedBytes: Int64 = 512 * 1024 * 1024,
        maximumQueuedBytes: Int = 8 * 1024 * 1024
    ) {
        self.enabled = enabled
        self.transferMode = transferMode
        self.maximumSessions = maximumSessions
        self.maximumSessionBytes = maximumSessionBytes
        self.maximumRetainedBytes = maximumRetainedBytes
        self.maximumQueuedBytes = maximumQueuedBytes
    }

    public func validated() throws -> OfflineCaptureOptions {
        guard maximumSessions > 0,
              maximumSessionBytes > 0,
              maximumRetainedBytes >= maximumSessionBytes,
              maximumQueuedBytes > 0 else {
            throw OfflineCaptureError.invalidOptions
        }
        return self
    }
}
