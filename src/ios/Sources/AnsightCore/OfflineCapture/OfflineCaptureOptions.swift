import Foundation

/// Applies to native recording on Apple platforms, including framework SDKs.
public struct OfflineCaptureOptions: Sendable, Codable, Equatable {
    public var rootDirectory: String?
    public var enabled: Bool
    public var recordingEnabled: Bool
    public var transferMode: OfflineCaptureTransferMode
    public var maximumSessions: Int
    public var maximumSessionBytes: Int64
    public var maximumRetainedBytes: Int64
    public var maximumQueuedBytes: Int

    public init(
        enabled: Bool = false,
        recordingEnabled: Bool = true,
        rootDirectory: String? = nil,
        transferMode: OfflineCaptureTransferMode = .automatic,
        maximumSessions: Int = 10,
        maximumSessionBytes: Int64 = 128 * 1024 * 1024,
        maximumRetainedBytes: Int64 = 512 * 1024 * 1024,
        maximumQueuedBytes: Int = 8 * 1024 * 1024
    ) {
        self.rootDirectory = rootDirectory
        self.enabled = enabled
        self.recordingEnabled = recordingEnabled
        self.transferMode = transferMode
        self.maximumSessions = maximumSessions
        self.maximumSessionBytes = maximumSessionBytes
        self.maximumRetainedBytes = maximumRetainedBytes
        self.maximumQueuedBytes = maximumQueuedBytes
    }

    private enum CodingKeys: String, CodingKey {
        case enabled, recordingEnabled, rootDirectory, transferMode, maximumSessions, maximumSessionBytes, maximumRetainedBytes, maximumQueuedBytes
    }

    public init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        self.init(
            enabled: try values.decodeIfPresent(Bool.self, forKey: .enabled) ?? false,
            recordingEnabled: try values.decodeIfPresent(Bool.self, forKey: .recordingEnabled) ?? true,
            rootDirectory: try values.decodeIfPresent(String.self, forKey: .rootDirectory),
            transferMode: try values.decodeIfPresent(OfflineCaptureTransferMode.self, forKey: .transferMode) ?? .automatic,
            maximumSessions: try values.decodeIfPresent(Int.self, forKey: .maximumSessions) ?? 10,
            maximumSessionBytes: try values.decodeIfPresent(Int64.self, forKey: .maximumSessionBytes) ?? 128 * 1024 * 1024,
            maximumRetainedBytes: try values.decodeIfPresent(Int64.self, forKey: .maximumRetainedBytes) ?? 512 * 1024 * 1024,
            maximumQueuedBytes: try values.decodeIfPresent(Int.self, forKey: .maximumQueuedBytes) ?? 8 * 1024 * 1024)
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
