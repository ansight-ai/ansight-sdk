import Foundation

public struct RecordedMetric: Sendable, Codable, Equatable {
    public let value: Int64
    public let capturedAtUtc: String
    public let capturedAtEpochMs: Int64
    public let channel: Int
    public let sequence: Int64

    public init(
        value: Int64,
        channel: Int,
        capturedAtUtc: String = AnsightClock.isoNow(),
        sequence: Int64 = 0
    ) {
        self.value = value
        self.capturedAtUtc = capturedAtUtc
        self.capturedAtEpochMs = AnsightClock.epochMilliseconds(fromISO8601: capturedAtUtc)
        self.channel = channel
        self.sequence = sequence
    }
}
