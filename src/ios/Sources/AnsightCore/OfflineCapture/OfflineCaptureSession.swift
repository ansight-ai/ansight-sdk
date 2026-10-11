import Foundation

public struct OfflineCaptureSession: Sendable, Codable, Equatable {
    public let sessionId: String
    public let startedAtUtc: String
    public let ordinal: Int64
    public var stoppedAtUtc: String?
    public var terminationKind: String?
    public let appId: String
    public let processSessionId: String
    public var bytes: Int64
    public var records: Int64
    public var droppedRecords: Int64
}
